# F_GET_ITEM Packet Format

Opcode: `F_GET_ITEM` (`0xAA`) — sends item data to the client.

> **Client RE Source**: WAR client item parser at `FUN_0050deb3`, verified via Ghidra MCP
> decompilation of sub-functions. V1 server code (`Item.BuildItem`) cross-referenced.
> All field widths and read order confirmed against the client binary.

## JSON Structure (Client-Verified)

```jsonc
// F_GET_ITEM — Batch wrapper (opcode already framed by transport layer)
{
  "ItemCount": "byte",           // 1–N items in this packet
  "Padding": "byte[3]",         // always 0x00 0x00 0x00
  "Items": [                     // ItemCount × concatenated item payloads:
    {
      // ── Preamble (read by outer handler, not FUN_0050deb3) ────
      "SlotId": "uint16",       // wire slot index (omitted when SlotId == 0)

      // ── Pre-Appearance Block (FUN_0050deb3 starts here) ───────
      // V1 calls this "Repairable" but the CLIENT reads it as a
      // "has pre-appearance data" flag. V1 always sends 0 for normal items.
      // BuildRepairableItem (entries 2500000–2600000) sends 1 here.
      "HasPreAppearance": "byte",   // 0 = normal item, 1 = pre-appearance data
      // if HasPreAppearance != 0:
      "PreAppearance": {
        "AltName": "pascal",        // pascal string
        "AltEntry": "uint32",
        "AltUnk1": "uint32",
        "AltUnk2": "uint32"
      },

      // ── Item Entry ────────────────────────────────────────────
      "Entry": "uint32",       // item definition ID (if 0, payload ends here)

      // ── Model & Alt Appearance (FUN_0050d422) ─────────────────
      "ModelId": "uint16",          // primary display model
      "AltModelId": "uint16",      // alt display model (0 if no alt appearance)
      "AltEntry": "uint32",        // alt item entry (0 if no alt appearance)
      "AltName": "pascal",         // alt item name ("" if no alt appearance)
      // V1 writes Fill(0,7) when no alt appearance, which the client reads as:
      //   uint16(0) + uint32(0) + pascal("") = 2+4+1 = 7 bytes ✓

      // ── Attributes ────────────────────────────────────────────
      "EquipSlotId": "uint16",     // equipment slot from definition
      "Type": "byte",              // item type enum (key conditional field)
      "MinRank": "byte",           // minimum character level
      "ObjectLevel": "byte",       // item's internal level
      "MinRenown": "byte",         // minimum renown rank
      "MinRenown2": "byte",        // duplicate of MinRenown (purpose unknown)
      "UniqueEquipped": "byte",    // unique-equipped limit
      "Rarity": "byte",           // 0=white 1=green 2=blue 3=purple 4=gold 5=orange
      "Bind": "byte",             // 0=none 1=BoP 2=BoE

      // ── Race (FUN_0050d572) — reads uint16, extracts 8 race flags ──
      "Race": "uint16",            // race restriction bitmask (8 bits used)

      // ── Career (FUN_0050d615) — reads uint32, extracts 32 career flags ──
      "Career": "uint32",          // career bitmask (32 bits)

      // ── Type-Conditional Bitmask (FUN_0050d6b4) ───────────────
      // Only read when Type == 23 (ENHANCEMENT) or Type == 24 (TROPHY).
      // This is NOT zero padding — it's an actual uint32 bitmask field.
      // V1 sends uint32(0) for both types, which happens to be a valid
      // zero bitmask.
      // if (Type == 23 || Type == 24):
      "TypeBitmask": "uint32",     // only present for enhancement/trophy types

      // ── Trophy-Specific Fields ────────────────────────────────
      // if (Type == 24):
      "TrophyByte1": "byte",       // stored as (value - 1) in client
      "TrophyByte2": "byte",
      // V1 sends uint16(0) here for trophies ← client reads as byte(0)+byte(0)
      // Then V1 sends uint16(0)+uint16(AltAppEntry) as career override,
      // but the client already read Career earlier via FUN_0050d615.
      //
      // BYTE COMPATIBILITY NOTE:
      // V1 trophy: uint32(0)[4] + uint16(0)[2] + uint16(0)+uint16(alt)[4] = 10 extra bytes
      // Client:    uint32(bitmask)[4] + byte+byte[2] + ... = 6 extra bytes
      // Plus Career is read separately (4 bytes) = 10 total. The bytes align!

      // ── Economy & Stack ───────────────────────────────────────
      "BaseColor1": "uint16",
      "BaseColor2": "uint16",
      "SellPrice": "uint32",
      "MaxStack": "uint16",
      "Count": "uint16",           // current stack count (min 1)
      "ItemSetId": "uint32",       // item set entry (0 = none)
      // if ItemSetId != 0: FUN_0050dd5b does internal lookup, NO extra reads

      // ── Skills (FUN_0050d770) ─────────────────────────────────
      "Skills": "uint32",          // skill requirement bitmask (28 bits extracted)

      // ── DPS/Armor/Speed (FUN_0050d80f) ────────────────────────
      // Type-based switch — all branches read exactly 4 bytes (uint16+uint16).
      // Weapon types: uint16(DPS) + uint16(Speed)
      // Shield types: uint16(Armor) + uint16(0)
      // Quest/other:  uint16(val) + uint16(val) — stored differently
      "DpsOrArmor": "uint16",
      "Speed": "uint16",

      // ── Name ──────────────────────────────────────────────────
      "Name": "pascal",

      // ── Stats Array (FUN_0050c3df) ────────────────────────────
      // CLIENT TRUTH: byte count + count × (byte statId + uint16 value + byte isExpiring + uint32 timer)
      // V1 ERROR: V1 sends Fill(0,5) or Fill(1,5) — the "5 padding bytes" are actually
      //   byte(isExpiring) + uint32(timerSeconds). V1's Fill(0,5) = isExpiring=0 + timer=0.
      //   V1's Fill(1,5) for AutoAttackSpeed = isExpiring=1 + timer=0x01010101 (garbage!).
      "StatsCount": "byte",
      "Stats": [
        {
          "StatId": "byte",
          "Value": "uint16",
          "IsExpiring": "byte",    // 0 = permanent, nonzero = has timer
          "Timer": "uint32"        // seconds remaining (0 = permanent)
        }
        // ... StatsCount entries, 8 bytes each
      ],

      // ── Effects Array (FUN_0050c660) ──────────────────────────
      // CLIENT TRUTH: byte count + count × (uint16 effectId + uint32 timer)
      // V1 ERROR: V1 sends uint32(0) as "padding" — it's actually a timer field.
      "EffectsCount": "byte",
      "Effects": [
        {
          "EffectId": "uint16",
          "Timer": "uint32"        // seconds remaining (0 = permanent)
        }
        // ... EffectsCount entries, 6 bytes each
      ],

      // ── Spells Array (FUN_0050c525) ───────────────────────────
      // CLIENT TRUTH: byte count + count × (uint16 + uint16 + uint16 + uint16)
      // Supports MULTIPLE spells per item! Each spell = 8 bytes.
      // V1 ERROR: V1 treats this as boolean (0 or 1) + uint32(SpellId) +
      //   uint16(CD) + uint16(RemainingCD) = 1 or 9 bytes. This works by
      //   coincidence: V1's byte(1) + uint32(SpellId) = count 1 + 2×uint16.
      "SpellsCount": "byte",
      "Spells": [
        {
          "Field1": "uint16",      // V1: high 16 bits of SpellId (often 0)
          "Field2": "uint16",      // V1: low 16 bits of SpellId
          "Field3": "uint16",      // V1: Cooldown
          "Field4": "uint16"       // V1: RemainingCooldown
        }
        // ... SpellsCount entries, 8 bytes each
      ],

      // ── Crafts Array (FUN_0050c716) ───────────────────────────
      "CraftsCount": "byte",
      "Crafts": [
        {
          "CraftKey": "byte",
          "CraftValue": "uint16"
        }
        // ... CraftsCount entries, 3 bytes each
      ],

      // ── Post-Craft Mystery Field (FUN_0050c803) ───────────────
      // V1 always sends byte(0). If nonzero, client reads uint16+uint16.
      "MysteryFlag": "byte",       // 0 = nothing, nonzero = 4 more bytes
      // if MysteryFlag != 0:
      "MysteryValue1": "uint16",
      "MysteryValue2": "uint16",

      // ── Talisman Slots (FUN_0050d97b) ─────────────────────────
      // CLIENT TRUTH: Client ALWAYS iterates exactly 3 times (hardcoded loop).
      // For i < TalismanSlots: reads uint32 entry (if !=0: reads full talisman data).
      // For i >= TalismanSlots: clears slot struct, NO stream reads.
      //
      // KEY FINDING: Populated talismans use the SAME sub-parsers as items!
      //   stats (FUN_0050c3df) + effects (FUN_0050c660) + spells (FUN_0050c525)
      //   + crafts (FUN_0050c716) + mystery (FUN_0050c803) + 2 trailing bytes.
      //
      // V1 BUG: V1 sends a completely different format for populated talismans:
      //   byte(0)+byte(0)+uint16(model)+byte(fused)+byte(0)+pascal(name)+stats+effects
      //   +Fill(0,3)+uint16(0x041C)
      // This "works" by coincidence because:
      //   - byte(0)+byte(0)+uint16(model) → client reads as uint32 entry = model value
      //   - byte(fused)+byte(0) → client reads as uint16 model = fused<<8
      //   - Fill(0,3) → byte(0)=spells count 0 + byte(0)=crafts count 0 + byte(0)=mystery 0
      //   - uint16(0x041C) → byte(0x04)+byte(0x1C) = trailing byte 1 (4) + trailing byte 2 (28)
      "TalismanSlots": "byte",     // 0–3 (server must send entries for this many slots)
      "Talismans": [
        // Per slot (only for i < TalismanSlots):
        //
        // ── Empty slot (4 bytes): ──
        // { "Entry": "uint32(0)" }
        //
        // ── Populated slot (variable): ──
        {
          "Entry": "uint32",          // talisman item entry (0 = empty)
          // if Entry != 0:
          "ModelId": "uint16",        // talisman display model
          "Name": "pascal",           // talisman name
          // Same sub-parsers as item:
          "StatsCount": "byte",
          "Stats": ["same as item stats: statId(byte) + value(uint16) + isExpiring(byte) + timer(uint32)"],
          "EffectsCount": "byte",
          "Effects": ["same as item effects: effectId(uint16) + timer(uint32)"],
          "SpellsCount": "byte",
          "Spells": ["same as item spells: 4×uint16 per entry"],
          "CraftsCount": "byte",
          "Crafts": ["same as item crafts: key(byte) + value(uint16)"],
          "MysteryFlag": "byte",
          // if MysteryFlag != 0: MysteryValue1(uint16) + MysteryValue2(uint16)
          "TrailingByte1": "byte",    // V1 sends 0x04 (from magic 0x041C)
          "TrailingByte2": "byte"     // V1 sends 0x1C (from magic 0x041C)
        }
      ],

      // ── Description ───────────────────────────────────────────
      "Description": "pascal",

      // ── Flags Block ───────────────────────────────────────────
      "Unk0": "byte",              // Unks[0]
      "Unk1": "byte",              // Unks[1] — "getUnk7"
      "Unk2": "byte",              // Unks[2] — "getUnk8"
      "Unk3": "byte",              // Unks[3] — "getNoChargeLeftDontDelete"

      // FUN_0050dafe: reads byte count, then count bytes (max 4 stored)
      "FlagArrayCount": "byte",    // Unks[4] — how many bytes follow
      "FlagArrayBytes": "byte[]",  // FlagArrayCount bytes

      "Flag5": "byte",             // next individual flag byte

      // ── Dyes ──────────────────────────────────────────────────
      "PrimaryDye": "uint16",
      "SecondaryDye": "uint16",

      // ── Tail Block ────────────────────────────────────────────
      "TailField1": "uint16",
      "TailField2": "byte",
      "TailField3": "uint32",
      "GuildFlag": "byte",         // FUN_0050dbac: 0=no heraldry, nonzero=read heraldry
      // if GuildFlag != 0 — FUN_0044a796 reads 9 bytes:
      "Heraldry": {
        "Emblem": "uint16",
        "Pattern": "uint16",
        "Color1": "byte",
        "Color2": "byte",
        "Discarded": "byte",       // read but not stored
        "Shape": "byte",
        "Extra": "byte"
      },
      "TailField4": "uint32",
      "TailField5": "byte",        // bool
      "TailField6": "byte"         // bool
    }
  ]
}
```

---

## V1 → Client Compatibility Analysis

The V1 server (`Item.BuildItem`) and the WAR client parser (`FUN_0050deb3`) are **byte-compatible**
despite having different interpretations of several fields. Here's how:

### Field-Level Discrepancies

| Field | V1 Server Sends | Client Reads | Compatible? | Notes |
|-------|-----------------|-------------|-------------|-------|
| Pre-flag | `byte(0)` "Repairable" | `byte` hasPreAppearance | **Yes** | V1 always sends 0 → no pre-appearance block |
| Race/Career | `byte(Bind)` + `byte(Race)` then `uint32(Career)` | FUN_0050d572 (uint16) + FUN_0050d615 (uint32) | **Yes** | V1's Bind+Race = 2 bytes = uint16 |
| Trophy padding | `uint32(0)` + `uint16(0)` (6 bytes) | `uint32` bitmask + `byte` + `byte` (6 bytes) | **Yes** | Same bytes, different interpretation |
| Enhancement padding | `uint32(0)` (4 bytes) | `uint32` bitmask (4 bytes) | **Yes** | Same bytes |
| Stat "padding" | `Fill(0,5)` or `Fill(1,5)` | `byte(isExpiring)` + `uint32(timer)` | **Yes**\* | AutoAttack sends 1s → timer=0x01010101 (garbage) |
| Effect "padding" | `uint32(0)` | `uint32(timer)` | **Yes** | V1 sends 0 → timer=0 |
| Spell block | `byte(0/1)` + `uint32(spell)` + `uint16(cd)` + `uint16(rem)` | `byte(count)` + count × `uint16[4]` | **Yes** | uint32 = 2×uint16 in big-endian alignment |
| Post-craft | `byte(0)` | `byte(flag)` + conditional | **Yes** | V1 always sends 0 |
| Talisman entry | `byte(0)+byte(0)+uint16(model)` | `uint32(entry)` | **Yes**\* | Entry VALUE is wrong (= modelId, not entry!) |
| Talisman model | `byte(fused)+byte(0)` | `uint16(model)` | **Yes**\* | Model VALUE is wrong (= fused<<8) |
| Talisman trailing | `Fill(0,3)+uint16(0x041C)` | spells(0)+crafts(0)+mystery(0)+byte+byte | **Yes** | Bytes align: 3 zero count bytes + 0x04 + 0x1C |

\* = byte-compatible but semantically incorrect

### V1 Bugs Discovered

1. **Stat AutoAttackSpeed**: Sends `Fill(1,5)` which client reads as `isExpiring=1, timer=0x01010101` — garbage timer value
2. **Talisman entry**: Sends modelId where client expects entry ID — talismans may not display correctly
3. **Talisman model**: Sends fused status where client expects display model — talisman icon is wrong
4. **Talisman spells/crafts**: Never populated (sent as zero counts via `Fill(0,3)`) — talisman on-use spells don't show

---

## Complete Field Read Order (FUN_0050deb3)

This is the authoritative byte-level read sequence from the client binary.

### Pre-Entry Block

| # | Function | Type | Bytes | Field | Condition |
|---|----------|------|-------|-------|-----------|
| 1 | readByte | byte | 1 | HasPreAppearance | always |
| 2 | readString | pascal | 1+N | PreAlt.Name | if #1 != 0 |
| 3 | readUInt32 | uint32 | 4 | PreAlt.Entry | if #1 != 0 |
| 4 | readUInt32 | uint32 | 4 | PreAlt.Unk1 | if #1 != 0 |
| 5 | readUInt32 | uint32 | 4 | PreAlt.Unk2 | if #1 != 0 |
| 6 | readUInt32 | uint32 | 4 | Entry | always |

> If Entry == 0: parsing stops here (empty item).

### Model & Appearance Block (FUN_0050d422)

| # | Function | Type | Bytes | Field |
|---|----------|------|-------|-------|
| 7 | readUInt16 | uint16 | 2 | ModelId |
| 8 | readUInt16 | uint16 | 2 | AltModelId |
| 9 | readUInt32 | uint32 | 4 | AltEntry |
| 10 | readString | pascal | 1+N | AltName |

### Attributes Block

| # | Function | Type | Bytes | Field |
|---|----------|------|-------|-------|
| 11 | readUInt16 | uint16 | 2 | EquipSlotId |
| 12 | readByte | byte | 1 | Type |
| 13 | readByte | byte | 1 | MinRank |
| 14 | readByte | byte | 1 | ObjectLevel |
| 15 | readByte | byte | 1 | MinRenown |
| 16 | readByte | byte | 1 | MinRenown2 |
| 17 | readByte | byte | 1 | UniqueEquipped |
| 18 | readByte | byte | 1 | Rarity |
| 19 | readByte | byte | 1 | Bind |

### Race, Career, & Type-Conditional

| # | Function | Type | Bytes | Field | Condition |
|---|----------|------|-------|-------|-----------|
| 20 | FUN_0050d572 | uint16 | 2 | RaceMask | always |
| 21 | FUN_0050d615 | uint32 | 4 | CareerMask | always |
| 22 | FUN_0050d6b4 | uint32 | 4 | TypeBitmask | Type == 23 or 24 |
| 23 | readByte | byte | 1 | TrophyByte1 (val-1) | Type == 24 |
| 24 | readByte | byte | 1 | TrophyByte2 | Type == 24 |

### Economy & Stack

| # | Function | Type | Bytes | Field |
|---|----------|------|-------|-------|
| 25 | readUInt16 | uint16 | 2 | BaseColor1 |
| 26 | readUInt16 | uint16 | 2 | BaseColor2 |
| 27 | readUInt32 | uint32 | 4 | SellPrice |
| 28 | readUInt16 | uint16 | 2 | MaxStack |
| 29 | readUInt16 | uint16 | 2 | Count |
| 30 | readUInt32 | uint32 | 4 | ItemSetId |

### Skills & Combat

| # | Function | Type | Bytes | Field |
|---|----------|------|-------|-------|
| 31 | FUN_0050d770 | uint32 | 4 | SkillsMask (28 bits) |
| 32+33 | FUN_0050d80f | uint16×2 | 4 | DPS/Armor + Speed |

### Name, Stats, Effects, Spells, Crafts, Mystery

| # | Function | Type | Bytes | Field |
|---|----------|------|-------|-------|
| 34 | readString | pascal | 1+N | Name |
| — | FUN_0050c3df | byte + N×8 | var | Stats |
| — | FUN_0050c660 | byte + N×6 | var | Effects |
| — | FUN_0050c525 | byte + N×8 | var | Spells |
| — | FUN_0050c716 | byte + N×3 | var | Crafts |
| — | FUN_0050c803 | byte [+4] | var | Mystery post-craft |

### Stats (FUN_0050c3df) — 8 bytes per entry

| Offset | Type | Field |
|--------|------|-------|
| 0 | byte | StatId |
| 1 | uint16 | Value |
| 3 | byte | IsExpiring (0=permanent, nonzero=has timer) |
| 4 | uint32 | Timer (seconds remaining) |

### Effects (FUN_0050c660) — 6 bytes per entry

| Offset | Type | Field |
|--------|------|-------|
| 0 | uint16 | EffectId |
| 2 | uint32 | Timer (seconds remaining, 0=permanent) |

### Spells (FUN_0050c525) — 8 bytes per entry

| Offset | Type | Field |
|--------|------|-------|
| 0 | uint16 | SpellField1 (V1: SpellId high word, usually 0) |
| 2 | uint16 | SpellField2 (V1: SpellId low word) |
| 4 | uint16 | SpellField3 (V1: Cooldown) |
| 6 | uint16 | SpellField4 (V1: RemainingCooldown) |

### Talisman Slots (FUN_0050d97b)

**Client loops exactly 3 times.** For `i < TalismanSlots`: reads from stream. For `i >= TalismanSlots`: clears internally, no reads.

Per slot (when `i < TalismanSlots`):

| # | Type | Bytes | Field | Condition |
|---|------|-------|-------|-----------|
| 1 | uint32 | 4 | Entry | always |
| 2 | uint16 | 2 | ModelId | Entry != 0 |
| 3 | pascal | 1+N | Name | Entry != 0 |
| — | stats block | var | Stats | Entry != 0 (same parser as item) |
| — | effects block | var | Effects | Entry != 0 (same parser) |
| — | spells block | var | Spells | Entry != 0 (same parser!) |
| — | crafts block | var | Crafts | Entry != 0 (same parser!) |
| — | mystery | var | MysteryFlag [+4] | Entry != 0 (same parser!) |
| 4 | byte | 1 | TrailingByte1 | Entry != 0 |
| 5 | byte | 1 | TrailingByte2 | Entry != 0 |

### Post-Talisman Block

| # | Function | Type | Bytes | Field |
|---|----------|------|-------|-------|
| 35 | readString | pascal | 1+N | Description |
| 36 | readByte | byte | 1 | Flag_212 (bool) |
| 37 | readByte | byte | 1 | Flag_85 (bool) |
| 38 | readByte | byte | 1 | Field_24D |
| 39 | readByte | byte | 1 | Field_93 |
| — | FUN_0050dafe | byte+N | var | FlagArray (count + bytes) |
| 40 | readByte | byte | 1 | Flag_213 (bool) |
| 41 | readUInt16 | uint16 | 2 | PrimaryDye |
| 42 | readUInt16 | uint16 | 2 | SecondaryDye |
| 43 | readUInt16 | uint16 | 2 | TailField1 |
| 44 | readByte | byte | 1 | TailField2 |
| 45 | readUInt32 | uint32 | 4 | TailField3 |
| — | FUN_0050dbac | byte[+9] | var | GuildHeraldry (flag + optional data) |
| 46 | readUInt32 | uint32 | 4 | TailField4 |
| 47 | readByte | byte | 1 | TailField5 (bool) |
| 48 | readByte | byte | 1 | TailField6 (bool) |

### FUN_0050dafe Detail

```
byte count          — number of following bytes (V1 sends Unks[4])
byte[count]         — individual bytes (max 4 stored internally)
(FUN_00460fd5)      — bit-check, no stream reads, stores processed byte
```

### FUN_0050dbac (Guild Heraldry) Detail

```
byte flag           — 0 = no heraldry
if flag != 0:       — FUN_0044a796 reads 9 bytes:
  uint16 emblem
  uint16 pattern
  byte color1
  byte color2
  byte (discarded)
  byte shape
  byte extra
```

---

## V1 Flags Block → Client Mapping

V1 writes 9 bytes for the flags block. The client reads them as:

| V1 Field | Client Read |
|----------|-------------|
| Unks[0] | Field #36: Flag_212 |
| Unks[1] | Field #37: Flag_85 |
| Unks[2] | Field #38: Field_24D |
| Unks[3] | Field #39: Field_93 |
| Unks[4] | FUN_0050dafe count byte |
| BindFlag (computed) | FUN_0050dafe array byte 0 |
| Unks[6] \| dyeable/salvage | FUN_0050dafe array byte 1 |
| Unks[7] | FUN_0050dafe array byte 2 (or overflow) |
| BoundFlag (computed) | Field #40: Flag_213 |

> **NOTE:** Exact mapping depends on the value of Unks[4] (flag count).

---

## V1 Tail Block → Client Mapping

V1 writes 14 bytes (Unks[13..26]) for non-guild, or 24 bytes for guild heraldry.

| V1 Index | Client Field | Type |
|----------|-------------|------|
| 13–14 | TailField1 | uint16 |
| 15 | TailField2 | byte |
| 16–19 | TailField3 | uint32 |
| 20 | GuildFlag (0=no heraldry) | byte |
| 21–24 | TailField4 | uint32 (seconds until decayed) |
| 25 | TailField5 | byte (bool) |
| 26 | TailField6 | byte (bool, bit0 = TwoHanded) |

> Unks[20] = GuildFlag: **crashes client if nonzero for non-guild items** because
> the client tries to read 9 bytes of heraldry data when this byte is nonzero!

---

## V1 Guild Heraldry → Client Mapping

V1 writes:
```
Fill(0, 7) + byte(1) + BuildHeraldry(...) + byte(1) + byte(1) + Fill(0, 6)
```

Client reads:
```
uint16[0] + byte[0] + uint32[0x00000001] + byte(flag=heraldryData[0]) + uint16+uint16+byte+byte+byte+byte+byte(heraldry) + uint32[last4bytes...] + byte(1) + byte(0??)
```

The 7 zero bytes + byte(1) are split as: uint16(0) + byte(0) + uint32(0x00000001) → GuildFlag becomes the `byte(1)` from V1's build. Then `BuildHeraldry` data is read as the 9 heraldry bytes. Then `byte(1) + byte(1) + Fill(0,6)` = uint32 + byte + byte.

---

## Conditional Summary (Client-Verified)

| # | Condition | Effect | Variable Width? |
|---|-----------|--------|-----------------|
| 1 | **HasPreAppearance != 0** | +pascal +3×uint32 before entry | Yes |
| 2 | **Entry == 0** | Parsing stops (empty item) | Terminal |
| 3 | **Type == 23 or 24** | +uint32 bitmask (4 bytes) | Yes |
| 4 | **Type == 24** | +byte +byte (2 bytes) | Yes |
| 5 | **MysteryFlag != 0** | +uint16 +uint16 (4 bytes) | Yes |
| 6 | **Talisman Entry != 0** | Full talisman sub-format vs 4-byte empty | Yes |
| 7 | **GuildFlag != 0** | +9 bytes heraldry data | Yes |

Variable-length arrays: Stats (8B/ea), Effects (6B/ea), Spells (8B/ea), Crafts (3B/ea), FlagArray (1B/ea), Talismans (sub-items), 3 PascalStrings.

---

## SEH-Blocked Addresses

These client functions could not be decompiled due to SEH protection:

| Address | Purpose | Needed For |
|---------|---------|------------|
| `FUN_004c30ce` (body at `0x004c30d8`) | Main opcode dispatch switch | Finding F_GET_ITEM case |
| `0x004cbf40` | Calls FUN_0050deb3 — likely F_GET_ITEM handler | **Batch loop structure, batch limit** |

> The batch limit question (can we send more than 8 items?) remains unanswered.
> These addresses need IDA Pro analysis.

---

## F_ITEM_SET_DATA Format

Opcode: `F_ITEM_SET_DATA` — sent once per unique item set after all items.

| Field       | Type       | Bytes  | Description                          |
|-------------|------------|--------|--------------------------------------|
| Entry       | uint32     | 4      | Item set entry ID                    |
| Name        | pascal     | 1+N    | Item set name                        |
| Unk         | byte       | 1      | Modifies the spell display           |
| ItemCount   | byte       | 1      | Number of items in the set           |
| *per item:* |            |        |                                      |
| → ItemEntry | uint32     | 4      | Item entry ID                        |
| → ItemName  | pascal     | 1+N    | Item name                            |
| BonusCount  | byte       | 1      | Number of bonuses                    |
| *per bonus:*|            |        |                                      |
| → BonusKey  | byte       | 1      | Bonus ID                             |
| → *if key < 80:* |       |        | **Stat bonus**                       |
| →→ StatId   | byte       | 1      | Stat identifier                      |
| →→ Value    | uint16     | 2      | Stat value                           |
| →→ IsPct    | byte       | 1      | `1` = percentage bonus               |
| → *if key >= 80:* |      |        | **Spell bonus**                      |
| →→ SpellId  | uint16     | 2      | Spell/ability entry                  |
