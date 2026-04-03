# F_PLAYER_STATE2 Packet Format (Client → Server)

Opcode: `F_PLAYER_STATE2` (`0x62`) — the client sends this packet to the server to report
the local player's movement state, position, and combat target information.

> **Direction**: Client → Server (inbound at the server).
>
> **Client RE Source**: WAR client bitstream writer at `sub_4C69A3`, called from the
> movement-update dispatcher `sub_4B3417`. Verified via IDA Pro and Ghidra MCP
> decompilation of all helper functions. V1 server code (`MovementHandlers`) and
> WorldServerV2 `PlayerStateRequest` DTO cross-referenced.

---

## Overview

The payload is a **variable-length bitstream** packed LSB-first within each byte. The fields
are not byte-aligned — they flow continuously across byte boundaries. Three boolean flags
embedded in the stream control which conditional sections are present, producing packets
of different lengths.

The server deserializes the raw bitstream into fixed-width fields:

| Server DTO Field | Attribute | Description |
|---|---|---|
| `Data` | `[RawBytes] byte[]` | The entire packet payload as raw bytes (8–18 bytes) |

The DTO exposes typed decode methods that read the bitstream directly using a
`BitReader` ref struct (LSB-first per byte). Extension methods on the DTO classify
the packet by length (`PlayerStateType`) and decode the appropriate fields:

| Method | Returns | Usage |
|---|---|---|
| `Type` | `PlayerStateType` | Heartbeat (≤9 bytes), Standard (10–17), Combat (≥18) |
| `DecodeCommon()` | `PlayerStateCommon` | Header fields present in all variants (speed, flags, heartbeat counter) |
| `DecodePosition()` | `PlayerStatePosition?` | Zone-local position (null if HasPosition=0 or HasMoveDest=1) |
| `DecodeMoveDestination()` | `PlayerStateMoveDestination?` | Click-to-move target coords (null if HasMoveDest=0) |

---

## Bitstream Encoding Functions

The client uses five encoding primitives to pack values into the stream:

| Function | Signature | Description |
|---|---|---|
| **WriteBits** | `WriteBits(value, n)` | Writes `n` raw bits of `value` (unsigned), LSB-first |
| **WriteBit** | `WriteBit(flag)` | Writes a single bit (0 or 1) |
| **WriteRanged** | `WriteRanged(value, min, max)` | Normalizes `value` into `[min, max]`, writes `value − min` in `⌈log₂(max − min + 1)⌉` bits |
| **WriteSigned** | `WriteSigned(value, n)` | Writes 1 sign bit followed by `n − 1` magnitude bits |
| **WriteFloat** | `WriteFloat(value, n)` | Clamps `value` to `[0, maxConst]`, scales to `2^(n−1) − 1`, then calls `WriteSigned(scaled, n)` |

---

## Control Flags

Three flags within the bitstream control which conditional sections are present. A fourth
flag (`AltMode`) guards an alternate-coordinate code path that is **never active** in
observed client behavior — it is always 0.

| Flag | Struct Offset | Written At | Meaning |
|---|---|---|---|
| **AltMode** | `0x04` | Field 2 | Alternate coordinate system (always 0, legacy/dead code) |
| **HasCombatTarget** | `0x10` | Field 5 | Player has an active hostile target selected |
| **HasPosition** | `0x28` | Field 11 | Packet includes position data (see note below) |
| **HasMoveDest** | `0x30` | Field 13 | Click-to-move destination active (replaces zone-local coords with target coords) |

### When is HasPosition = 0?

`HasPosition` is 0 only when **all** of the following hold:

1. Player X, Y, and Z are exactly equal to the previously-transmitted values
2. A specific caller parameter is non-zero
3. The entity has no active movement target (`entity+0x360 == 0`)

This is an extremely narrow edge case. When it occurs, the packet contains movement-state
flags (speed, direction, mode, animation) but **no coordinate data**, yielding a 58-bit
(8-byte) payload. The current server DTO requires a minimum of 17 bytes and **cannot
parse this short form**.

---

## Bitstream Field Table

Fields are listed in **write order** within the bitstream. The "Condition" column shows
which flags must be set for the field to be present. An em-dash (—) means always present.

| # | Field | Bits | Encoding | Condition | Description |
|---|---|---|---|---|---|
| 1 | **Status Word** | 16 | WriteBits | — | Entity status flags from `entity+0x08`. Contains packed movement status bits. The server's `Unk1` byte is the low 8 bits of this field. |
| 2 | **Alt Mode** | 1 | WriteBit | — | Alternate coordinate mode. Always 0 in practice (dead code path). |
| 3 | **Speed** | 9 | WriteRanged(−127, 325) | — | Horizontal movement speed. Source: `entity+0x80` (float → int). Zeroed when stunned/rooted (`entity+0x98` or `entity+0x99`). Clamped to `[−127, 325]`. |
| 4 | **Vertical Velocity** | 12 | WriteRanged(−2000, 500) | — | Vertical velocity (fall/jump speed). Source: `entity+0x94` (float → int). Clamped to `[−2000, 500]`. |
| 5 | **Has Combat Target** | 1 | WriteBit | — | Whether the player has an active hostile target selected. |
| 6 | **Movement Mode** | 2 | WriteBits | — | 0 = idle, 1 = forward, 2 = walking, 3 = backwards. Mapped from caller parameter: `{2→1, 3→2, 4→3, else 0}`. |
| 7 | **Movement Direction** | 3 | WriteBits | — | Cardinal direction 0–7 derived from the player's heading angle. |
| 8 | **Movement Flags** | 3 | WriteBits | — | Bitmask from `entity+0xBC`. Bit 0 = grounded, bit 1 = airborne, bit 2 = swimming. Computed via bitwise NOT + masking. |
| 9 | **Target Visibility** | 1 | WriteBit | Combat ∧ ¬Alt | Line-of-sight / in-range result for the combat target. |
| 10 | **Heartbeat** | 3 | WriteBits | ¬Alt | 3-bit counter (0–7) that cycles on every state update. Name confirmed by old server `State2.cs`. The function's second argument is passed through directly. |
| 11 | **Has Position** | 1 | WriteBit | — | Whether coordinate data follows. Always 1 in normal operation (see note above). |
| 12 | **Heading** | 12 | WriteFloat | Position | Facing direction as a 12-bit signed float (1 sign + 11 magnitude). Max value = `6.2831855` (≈ 2π radians). Name confirmed by old server `State2.cs`. |
| 13 | **Has Move Destination** | 1 | WriteBit | Position | Click-to-move is active. When set, fields 20–23 replace fields 15–16 and 18–19. |
| 14 | **Ground Type** | 1 | WriteBit | Position | 0 = solid ground, 1 = water/swimming. Source: `entity+0x7C` (or hardcoded to 1 when dead). |
| 15 | **Zone-Local X** | 16 | WriteBits | Position ∧ ¬Dest | X coordinate in zone-local space. Written first in the bitstream. Converted from world coords via a per-zone offset lookup table. Confirmed by V1 `MovementHandlers` extraction and `State2.cs` `Write()` method. |
| 16 | **Zone-Local Y** | 16 | WriteBits | Position ∧ ¬Dest | Y coordinate in zone-local space. Written second. Same conversion as X. |
| 17 | **Combat Engagement** | 1 | WriteBit | Combat ∧ ¬Alt | Result of the engagement check function (`sub_919896`). Indicates active combat state with target. |
| 18 | **Zone ID** | 9 | WriteBits | Position ∧ ¬Dest | Zone identifier. Looked up from world coordinates via a zone boundary table. |
| 19 | **Z (Height)** | 16 | WriteBits | Position ∧ ¬Dest | Z-axis / height coordinate. 16-bit unsigned integer. Name confirmed by old server `State2.cs`. |
| 20 | **Target X** | 16 | WriteSigned | Position ∧ Dest | Click-to-move destination X. Source: `entity+0x2FC` (float → int truncation). |
| 21 | **Target Y** | 16 | WriteSigned | Position ∧ Dest | Click-to-move destination Y. Source: `entity+0x300` (float → int truncation). |
| 22 | **Target Z** | 16 | WriteSigned | Position ∧ Dest | Click-to-move destination Z. Source: `entity+0x304` (float → int truncation). |
| 23 | **Target OID** | 9 | WriteBits | Position ∧ Dest | Entity OID of the click-to-move target. XOR-obfuscated with `0x7EDD` before transmission (from `target+0x10A`). |
| 24 | **Combat Data 1** | 1 | WriteBit | Combat ∧ ¬Alt | From the combat state struct: `[entity+0x240]+0x18`. Ability/combat state flag. |
| 25 | **Combat Data 2** | 1 | WriteBit | Combat ∧ ¬Alt | From the combat state struct: `[entity+0x240]+0x50`. |
| 26 | **Combat Data 3** | 1 | WriteBit | Combat ∧ ¬Alt | From the combat state struct: `[entity+0x240]+0x34`. |
| 27 | **Combat Data 4** | 1 | WriteBit | Combat ∧ ¬Alt | From `entity+0xAA0`. |
| 28 | **Combat Data 5** | 1 | WriteBit | Combat ∧ ¬Alt | Result of the first `sub_919896` call (pre-engagement check). |
| 29 | **Animation Stance** | 3 | WriteBits | ¬Alt | Animation or stance state. Source: `entity+0x3A8`. |
| 30–33 | **Alt Mode Data** | 4 × 1 | WriteBit | Alt | Four flags for the alternate-mode path. Never written in practice (AltMode is always 0). |
| 34 | **Not Moving** | 1 | WriteBit | — | Player is stationary (caller param₁ == 1). |
| 35 | **Walking** | 1 | WriteBit | ¬Alt | Walk toggle is active (`entity+0x3A4 == 2`). |
| 36 | **Has Active Effect** | 1 | WriteBit | — | Entity has a currently active buff or effect (`entity+0x174 ≠ 0`). |
| 37 | **Has Move Target** | 1 | WriteBit | — | Entity has a pending movement destination (`entity+0x364 ≠ 0`). |

---

## Packet Variants

The flags produce four practical packet forms. Bit totals include all unconditionally-written
fields plus those enabled by the active flag combination.

| Variant | HasPosition | HasCombatTarget | HasMoveDest | Total Bits | Payload Bytes |
|---|---|---|---|---|---|
| **State-only** (no coords) | 0 | 0 | — | 58 | 8 |
| **Normal Movement** | 1 | 0 | 0 | 129 | 17 |
| **Click-to-Move** | 1 | 0 | 1 | 129 | 17 |
| **Combat Movement** | 1 | 1 | 0 | 136 | 18 |
| **Combat + Click-to-Move** | 1 | 1 | 1 | 136 | 18 |

> **State-only (heartbeat) variant**: This 8-byte short form contains movement flags
> (speed, direction, mode, animation, heartbeat counter) but no position data. It is sent
> when the player's position is unchanged from the last update (HasPosition = 0). The
> server classifies these as `PlayerStateType.Heartbeat` and relays them to nearby players
> without updating the authoritative position.

Packet classification by length:
- **≤ 9 bytes** → Heartbeat (state-only, no position)
- **10–17 bytes** → Standard movement (position update)
- **≥ 18 bytes** → Combat movement (position + combat target data)

---

## Bit Layout — Direct Bitstream Reading

The server reads the raw payload directly as an LSB-first bitstream using `BitReader`,
a ref struct that tracks the current bit offset and extracts fields sequentially. This
eliminates the previous approach of reading two big-endian `long` values (`State`/`State2`)
and reconstructing bitstream-contiguous fields from byte-reversed positions via
shift-and-mask operations.

The `BitReader` supports the same encoding primitives the client uses:

| Method | Client Equivalent | Description |
|---|---|---|
| `ReadBits(n)` | `WriteBits(v, n)` | Read `n` raw unsigned bits |
| `ReadBit()` | `WriteBit(f)` | Read a single bit as bool |
| `ReadRanged(min, max)` | `WriteRanged(v, min, max)` | Read `BitsForRange(max-min+1)` bits, add `min` |
| `ReadSigned(n)` | `WriteSigned(v, n)` | Read 1 sign bit + `n-1` magnitude bits |
| `ReadFloat(n, max)` | `WriteFloat(v, n)` | Read signed value, scale back to `[0, max]` float |

---

## Client-Side Anti-Cheat Obfuscation

The client applies XOR obfuscation to certain entity values in memory:

| Pattern | Usage |
|---|---|
| `XOR 0xF8B57EDD` | Entity position floats at `entity+0x08`, `+0x0C`, `+0x10` are stored obfuscated in memory. De-obfuscated before zone-local conversion. |
| `XOR 0x7EDD` | Entity OID at `entity+0x10A` (and combat target OID at `target+0x10A`) is stored obfuscated. Same mask used as a null sentinel for the combat target check at `entity+0xBE`. |

These obfuscations happen in the client's memory; the values written to the bitstream are
the **de-obfuscated** real values. The server does not need to apply any XOR decoding.

---

## Movement Direction Mapping

Field 7 (Movement Direction) is derived from the player's heading angle via a lookup
function that maps to 8 cardinal/intercardinal directions:

| Input | Output | Direction |
|---|---|---|
| 0 | 0 | Forward |
| 1 | 1 | Forward-Right |
| 2 | 3 | Right |
| 3 | 2 | Forward-Left |
| 4 | 5 | Backward-Left (default) |
| 5 | 7 | Left |
| else | 4 or 5 | Backward / Backward-Left |

The 3-bit output (0–7) is written directly to the bitstream.

---

## Movement Flags Bitmask

Field 8 (Movement Flags) is a 3-bit value derived from `entity+0xBC` via bitwise operations:

```
raw = entity[0xBC]         // uint16
inv = NOT(raw)
result = 0
if (inv & 0x01) result += 1    // bit 0 of NOT = original bit 0 was clear
if (inv & 0x10) result += 2    // bit 4 of NOT = original bit 4 was clear
else if (raw & 0x20) result += 4  // bit 5 of original was set
```

---

## Speed Clamping Logic

Field 3 (Speed) undergoes additional processing before encoding:

1. Read `entity+0x80` as float, convert to int via truncation
2. If value + 31 ≤ 62 (i.e., value is in `[−31, 31]`), set to 0 (dead zone)
3. If value == 0 but the original float was non-zero, set to 1 (minimum speed)
4. Clamp to max 325
5. If entity is stunned (`entity+0x98 ≠ 0`) or rooted (`entity+0x99 ≠ 0`), force to 0
6. If a caller flag is set (param₃ ≠ 0), force to 0
7. Final clamp to `[−127, 325]`

Then WriteRanged encodes as: `value − (−127)` in 9 bits (range 0–452).
