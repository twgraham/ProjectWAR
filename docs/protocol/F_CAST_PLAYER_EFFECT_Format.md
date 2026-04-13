# F_CAST_PLAYER_EFFECT Packet Format (Server → Client)

Opcode: `F_CAST_PLAYER_EFFECT` (`0xB3`) — the server sends this packet to display a
combat effect (damage numbers, heals, defense events, ability animations) on the client.
Sent to the target, caster, and all nearby players.

> **Direction**: Server → Client (outbound from the server).
>
> **Client RE Source**: WAR client main dispatch handler at `sub_4C30CE` routes opcode
> `0xB3` to **case 0x68** at `0x4C479C`, which byte-swaps three big-endian `u16` fields
> and calls `sub_4C74DD` — the core handler. Verified via IDA Pro and Ghidra MCP
> decompilation of all helper functions. V1 server code (`CombatManager`) and
> WorldServerV2 `CastPlayerEffectResponse` DTO cross-referenced.

---

## Overview

The packet has a **9-byte fixed header** followed by an optional **variable-length
payload** of zigzag-encoded signed integers (WAR's custom varint encoding). The `Flags`
byte (offset 8) is a bitfield that controls which variable fields are present,
what client-side processing occurs, and how the combat result is displayed.

---

## Fixed Header (9 bytes)

All multi-byte integers in the header are **big-endian** (network byte order). The client
calls `ntohs()` on the three `u16` fields at offsets 0, 2, and 4 before passing them to
the handler.

| Offset | Size | Type | Field | Description |
|--------|------|------|-------|-------------|
| 0 | 2 | `u16 BE` | **CasterOid** | Object ID of the entity that produced the effect. Compared against the local player's OID to determine "self-cast" perspective. |
| 2 | 2 | `u16 BE` | **TargetOid** | Object ID of the entity receiving the effect. Also compared against local player OID. |
| 4 | 2 | `u16 BE` | **AbilityEntry** | Ability display ID. Used to look up the ability record in the client's `AbilityTable` (via `sub_554879`). Drives the icon, name, and tooltip shown in SCT / combat log. |
| 6 | 1 | `u8` | **CommandIndex** | Index into the ability's effect lines (0–9). The client reads `AbilityData[0xB8 + CommandIndex]` to resolve the specific effect component. Values ≥ 10 are clamped (treated as no sub-effect). Also called "CastPlayerSubID" in V1 server. |
| 7 | 1 | `u8` | **CombatEvent** | Determines the type of combat event displayed. See [CombatEvent Values](#combatevent-values) below. Passed to `sub_4C73FF` (clears aura display slots on defense events 4–7) and to `sub_4FDEA6` (drives SCT text and animation). |
| 8 | 1 | `u8` | **Flags** | Bitfield controlling which variable data fields follow and how the effect is processed. See [Flags Bitfield](#flags-bitfield) below. |

---

## CombatEvent Values (offset 7)

Determined from the legacy `CombatEvent` enum and cross-referenced with client
disassembly at `sub_4C73FF` (which clears entity aura display slots for values 4–7)
and `sub_4FDEA6` (which special-cases value `9`).

| Value | Name | Description |
|-------|------|-------------|
| 0 | `HIT` | Normal auto-attack hit |
| 1 | `ABILITY_HIT` | Ability hit (non-critical) |
| 2 | `CRITICAL` | Auto-attack critical hit |
| 4 | `BLOCK` | Attack was blocked — clears aura slot 2 (target) / slot 3 (caster) |
| 5 | `PARRY` | Attack was parried — clears aura slot 0 / slot 1 |
| 6 | `EVADE` | Attack was evaded — clears aura slot 4 / slot 5 |
| 7 | `DISRUPT` | Attack was disrupted — clears aura slot 6 / slot 7 |
| 8 | `ABSORB` | Attack was fully absorbed by a shield/ward |
| 9 | `ABILITY_CRITICAL` | Ability critical hit — receives special "crit" SCT display (client checks `== 9`) |
| 10 | `IMMUNE` | Target is immune to the effect |
| 11 | `FALL_DAMAGE` | Environmental fall damage (used by self-damage packet) |

---

## Flags Bitfield (offset 8)

The client at `sub_4C74DD` (`0x4C7512`–`0x4C7563`) decomposes this byte into 7
individual bit flags. Each bit controls a specific aspect of packet parsing and
effect processing.

| Bit | Mask | Client Local | Name | Description |
|-----|------|-------------|------|-------------|
| 0 | `0x01` | `[ebp-19h]` | **SelfTarget** | When set, the client processes the full "incoming effect" path (health-bar update, SCT, ability-line validation, critical-hit checks). When clear, uses the simpler "outgoing/third-party" display path. |
| 1 | `0x02` | `[ebp+0Bh]` | **HasDamageData** | Two zigzag-encoded values follow in the variable payload: `DamageAmount` and `MitigationAmount`. |
| 2 | `0x04` | `[ebp-58h]` | **ShowVisual** | Passed to effect display functions. Controls whether a hit-flash / visual animation plays on the target model. Also influences VFX attachment slot selection in `sub_4FC654`. |
| 3 | `0x08` | `[ebp-31h]` | **SkipEffectLogic** | When set, the client skips the main combat-result processing (no ability-line validation, no health-bar or combat-log update). The initial display call (`sub_4FDEA6`) still occurs, but the downstream processing is bypassed. Used for partial DoT/HoT ticks and absorption display. |
| 4 | `0x10` | `[ebp-5Ch]` | **UseAlternateAbility** | When set, the displayed ability entry is overridden from the caster entity's field `0x3DE` (current auto-attack weapon ability). Used for auto-attack packets to show the weapon icon. |
| 5 | `0x20` | `[ebp-12h]` | **HasAbsorptionData** | One zigzag-encoded value follows: `AbsorptionAmount`. Read _after_ the damage/mitigation pair (if present). |
| 6 | `0x40` | `[ebp-11h]` | **HasExtendedData** | Ten additional values follow the other variable data: 4 × zigzag `int32`, 1 × `float32`, 5 × zigzag `int32`. Purpose not fully understood — likely for complex multi-hit or channelled ability result detail. Not used by V1 server. |

### Common Flag Compositions

These are the composite flag values observed in V1 server code and the V2 DTO factory methods:

| Flags | Binary | Bits Set | Usage |
|-------|--------|----------|-------|
| `0x01` | `00000001` | SelfTarget | Animation / VFX trigger only — no damage numbers |
| `0x05` | `00000101` | SelfTarget + ShowVisual | Defense event (block, parry, evade, disrupt, immune) |
| `0x07` | `00000111` | SelfTarget + HasDamage + ShowVisual | Standard ability damage or heal |
| `0x0B` | `00001011` | SelfTarget + HasDamage + SkipEffect | DoT/HoT partial tick (skip combat-log extension) |
| `0x0F` | `00001111` | SelfTarget + HasDamage + ShowVisual + SkipEffect | DoT/HoT final tick (show visual but skip extension) |
| `0x13` | `00010011` | SelfTarget + HasDamage + UseAltAbility | Auto-attack (uses weapon ability icon) |
| `0x2A` | `00101010` | HasDamage + SkipEffect + HasAbsorption | Damage with absorption shield consumed |

---

## Variable Payload (offset 9+)

Contains zigzag-encoded signed integers read sequentially by a stream reader
(`sub_993670` initializer, `sub_992A60` per-value reader). Each read can fail
gracefully if the stream is exhausted, leaving the output as `0`.

### WAR ZigZag Encoding

WAR uses a custom variable-length signed integer encoding (not standard protobuf
zigzag). The first byte carries the sign in bit 0 and 6 data bits (bits 1–6), with
bit 7 as a continuation flag. Subsequent bytes carry 7 data bits each.

```
First byte:  [C | d5 d4 d3 d2 d1 d0 | sign]
Next bytes:  [C | d6 d5 d4 d3 d2 d1 d0]
```

**Encoding** (from `CastPlayerEffectResponse.WriteZigZag`):
```csharp
byte sign = (byte)(val < 0 ? 1 : 0);
if (sign == 1) val++;              // adjust negative
val = Math.Abs(val);
dest[pos++] = (byte)(((val << 1) & 0x7F)
            | (val > 0x3F ? 0x80 : 0x00)
            | sign);
val >>= 6;
while (val > 0) {
    dest[pos++] = (byte)((val & 0x7F) | (val > 0x7F ? 0x80 : 0));
    val >>= 7;
}
```

**Decoding** (from client `sub_992A60`):
```
raw = read_varint()
sign = raw & 1
value = raw >> 1
if sign: value = ~value  // bitwise NOT
```

A value of `0` encodes as a single `0x00` byte. Negative values encode with
the low bit set (e.g., `-100` → `0xC7 0x01`).

### Conditional Fields

Fields are read **sequentially** from the stream. The order is fixed; flags
determine which groups are present.

| Condition | Field | Type | Description |
|-----------|-------|------|-------------|
| `Flags & 0x02` | **DamageAmount** | zigzag `int32` | Hit-point change. Negative = damage dealt, positive = health restored (heal). |
| `Flags & 0x02` | **MitigationAmount** | zigzag `int32` | Amount reduced by armor/resistances. Always ≥ 0. If the server doesn't write this value, the stream returns `0` (read succeeds on the terminator byte). |
| `Flags & 0x20` | **AbsorptionAmount** | zigzag `int32` | Amount consumed from an absorption shield or ward. Always ≥ 0. |
| `Flags & 0x40` | *Extended field 1* | zigzag `int32` | Purpose unknown (not used by V1/V2 server). |
| `Flags & 0x40` | *Extended field 2* | zigzag `int32` | " |
| `Flags & 0x40` | *Extended field 3* | zigzag `int32` | " |
| `Flags & 0x40` | *Extended field 4* | zigzag `int32` | " |
| `Flags & 0x40` | *Extended field 5* | `float32` | Read via `sub_993E10`. |
| `Flags & 0x40` | *Extended fields 6–10* | 5 × zigzag `int32` | " |

### Terminator

The server appends a single `0x00` byte after all zigzag values. Since `0x00`
decodes to zigzag value `0`, any additional stream reads harmlessly return `0`.
The client does not explicitly check for a terminator — it relies on the flags
to know how many values to read, and the stream reader fails gracefully on
exhaustion.

---

## Effect Processing Pipeline (client-side)

After parsing, the client executes these steps in `sub_4C74DD`:

1. **Entity Resolution** (`0x4C7566`–`0x4C75A7`): Looks up the caster and target
   entities by OID. Self-OID comparisons set `isCasterSelf` and `isTargetSelf`
   booleans.

2. **Aura Slot Clear** — `sub_4C73FF` (`0x4C75B3`): For `CombatEvent` values 4–7
   (defense events), clears the corresponding aura/effect display slot on both
   the target and caster entity models. Slot mapping:
   - 4 (Block): target slot 2, caster slot 3
   - 5 (Parry): target slot 0, caster slot 1
   - 6 (Evade): target slot 4, caster slot 5
   - 7 (Disrupt): target slot 6, caster slot 7

3. **Stream Decode** (`0x4C75EE`–`0x4C7726`): Reads variable data per flags.

4. **Ability Lookup** (`0x4C7731`–`0x4C77B9`): Fetches the ability record by
   `AbilityEntry`. Reads `CommandIndex`-indexed effect line properties. Extracts
   "is friendly" and "is channelled" flags from the ability data.

5. **Weapon Resolution** (`0x4C77BB`–`0x4C7810`): If caster entity is a player
   (type 3), reads their equipped weapon ID from entity field `0x3BC` (XOR-obfuscated
   with `0x7EDD`). If `UseAlternateAbility` flag is set, overrides the display
   ability ID from entity field `0x3DE`.

6. **Range Calculation** (`0x4C7813`–`0x4C7870`): Computes a display range value
   (default 400 units). Adjusted upward if caster is self and there's a "channelled"
   flag on the target.

7. **SCT / Display Call** — `sub_4FDEA6` (`0x4C7918`): Called with `(CombatEvent,
   UseAlternateAbility, AbilityEntry, isSelfTarget, isFriendly)`. Drives scrolling
   combat text and the combat log entry.

8. **Main Effect Branch** (`0x4C7923`):
   - If `SkipEffectLogic` (bit 3): jump to cleanup.
   - If `SelfTarget` (bit 0) AND (`CombatEvent` is 0 or 9, OR `HasExtendedData`):
     full incoming-damage processing via `sub_4FC047` or `sub_4FBCC8`.
   - Otherwise: simplified outgoing/third-party display via `sub_4FC135` (normal
     entities) or `sub_508E50` (type-8 entities / siege objects).

9. **Damage Blood Splatter** (`0x4C7B4C`–`0x4C7BB6`): If caster entity exists,
   target is self, `SelfTarget` flag set, and damage is negative (i.e., actual damage),
   queues a screen blood-splatter VFX at the entity's world position.

10. **Target Hit Animation** (`0x4C7BB7`–`0x4C7BDF`): If target entity is a player
    (type 3), triggers a "being hit" animation (`sub_4FD184`) parameterized by
    `CombatEvent`.

---

## Packet Examples

### Defense Event (Block)

```
Offsets:  00 01 | 02 03 | 04 05 | 06 | 07 | 08 | 09
Bytes:    AA BB   CC DD   EE FF   00   04   05   00
```

| Field | Value | Meaning |
|-------|-------|---------|
| CasterOid | `0xAABB` | Attacker OID |
| TargetOid | `0xCCDD` | Defender OID (who blocked) |
| AbilityEntry | `0xEEFF` | Ability that was blocked |
| CommandIndex | `0` | No sub-index |
| CombatEvent | `4` | BLOCK |
| Flags | `0x05` | SelfTarget + ShowVisual (no damage data) |
| Terminator | `0x00` | End |

**Total: 10 bytes.** No zigzag values — bit 1 not set.

### Standard Ability Damage (−250 damage, 80 mitigation)

```
Offsets:  00 01 | 02 03 | 04 05 | 06 | 07 | 08 | 09 ... | ...  | N
Bytes:    AA BB   CC DD   07 D1   02   01   07   ZZ...     ZZ...  00
```

| Field | Value | Meaning |
|-------|-------|---------|
| CasterOid | `0xAABB` | |
| TargetOid | `0xCCDD` | |
| AbilityEntry | `0x07D1` (2001) | Ability display ID |
| CommandIndex | `2` | Third effect line |
| CombatEvent | `1` | ABILITY_HIT |
| Flags | `0x07` | SelfTarget + HasDamage + ShowVisual |
| DamageAmount | zigzag(−250) | Negative = damage |
| MitigationAmount | zigzag(80) | Armor reduction |
| Terminator | `0x00` | |

### Cast Animation (VFX trigger, no damage)

```
Offsets:  00 01 | 02 03 | 04 05 | 06 | 07 | 08 | 09
Bytes:    AA BB   CC DD   EE FF   03   00   01   00
```

| Field | Value | Meaning |
|-------|-------|---------|
| CommandIndex | `3` | Effect ID (low byte) — drives particle/VFX |
| CombatEvent | `0` | No hit event |
| Flags | `0x01` | SelfTarget only (animation trigger) |

**Total: 10 bytes.**

### Damage with Absorption (−200 damage, 50 mitigation, 100 absorbed)

```
Offsets:  00 01 | 02 03 | 04 05 | 06 | 07 | 08 | 09 ...  | ...   | ...    | N
Bytes:    AA BB   CC DD   EE FF   01   09   2A   ZZ...    ZZ...   ZZ...    00
```

| Field | Value | Meaning |
|-------|-------|---------|
| CombatEvent | `9` | ABILITY_CRITICAL |
| Flags | `0x2A` | HasDamage + SkipEffect + HasAbsorption |
| DamageAmount | zigzag(−200) | |
| MitigationAmount | zigzag(50) | From bit 1, second read |
| AbsorptionAmount | zigzag(100) | From bit 5 |

---

## Cross-Reference: V1 Server Send Sites

| Location | Usage | Flags |
|----------|-------|-------|
| `CombatManager.CheckDefense` | Block/Parry/Evade/Disrupt | `0x05` |
| `CombatManager.DealAbilityDamage` | Standard ability damage | `0x07` |
| `CombatManager.PrecalculatedDamageTarget` | DoT partial tick | `0x0B` |
| `CombatManager.PrecalculatedDamageTarget` (finalize) | DoT final tick | `0x0F` |
| `Player.Terminate` | Insta-kill | `0x07` |
| `Player.RezUnit` | Rez heal display | `0x07` |
| `Player.ApplyFallDamage` | Fall damage (CombatEvent=11) | `0x07` |
| `Unit.NotifyImmune` | Immune display | `0x05` |
| `NewChannelHandler` | Channel animation | `0x01` |
| `LandMine.OnTakeDamage` | Mine explosion anim | `0x01` |

## Cross-Reference: V2 Server DTO

`CastPlayerEffectResponse` in `WorldServerV2/Network/Dtos/`:

| Factory Method | CombatEvent | Flags | Stream Data |
|----------------|-------------|-------|-------------|
| `Damage(...)` | `1` or `9` | `0x07` or `0x2A` | damage + mitigation [+ absorption] |
| `Defense(...)` | `4`–`7` | `0x05` | none |
| `CastAnimation(...)` | `0` | `0x01` | none |

---

## V1 Server Bug: Absorption Under `Flags = 0x07`

The V1 `CombatManager` writes the absorption zigzag value under `Flags = 0x07`:

```csharp
outl.WriteByte(7);                               // Flags = 0x07
outl.WriteZigZag(-(ushort)damageInfo.Damage);     // read by bit 1 (first)
if (damageInfo.Mitigation > 0)
    outl.WriteZigZag((ushort)damageInfo.Mitigation);  // read by bit 1 (second)
if (damageInfo.Absorption > 0)
    outl.WriteZigZag((ushort)damageInfo.Absorption);  // NOT read — bit 5 not set
outl.WriteByte(0);
```

Under `Flags = 0x07` (bits 0, 1, 2), the client reads exactly **two** zigzag values
(bit 1: DamageAmount + MitigationAmount). Bit 5 (`HasAbsorptionData`) is not set, so
the third zigzag (absorption) is **never consumed**. The absorption value is silently
ignored by the client.

The V2 `CastPlayerEffectResponse.Damage()` corrects this by switching to `Flags = 0x2A`
when absorption is present, which sets bit 5 and enables the absorption read.
