using Core.Infrastructure.Network.Serialization.Attributes;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Entities;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_CREATE_STATIC</c> (0x71) — Notifies a client that a static game object has become
/// visible. Sent whenever a <see cref="GameObjectEntity"/> enters a player's visibility set.
/// </summary>
/// <remarks>
/// <para><b>Wire format (40-byte fixed header + variable tail):</b></para>
/// <code>
/// +0x00 Oid          u16     Unique object identifier
/// +0x02 VfxState     u16     Visual-effect state
/// +0x04 Heading      u16     Facing direction (4096-unit circle)
/// +0x06 Z            u16     Vertical position
/// +0x08 X            u32     Horizontal position
/// +0x0C Y            u32     Horizontal position
/// +0x10 DisplayId    u16     Model / visual prototype
/// +0x12 UnkHi        byte    Dead — client ignores this byte
/// +0x13 StaticFlags  byte    Compound bitfield (see StaticFlags enum)
/// +0x14 Unks1        u16  ─┐ Reserved pair — typically 0 in DB
/// +0x16 Unks2        u16  ─┘
/// +0x18 SpawnUnk1    byte ─┐ Combined u16 with Flags HI byte
/// +0x19 Flags        u16  ─┘ Low byte = ObjectFlags bitfield
/// +0x1B SpawnUnk2    byte    Dead — client ignores this byte
/// +0x1C SpawnUnk3    u32     Byte layout: [dead][dead][Scale][Variant]
/// +0x20 Unks4        u16  ─┐ Spawn timestamp components — combined with
/// +0x22 Unks5        u16  ─┘ SpawnUnk4 to produce a time-in-seconds float.
/// +0x24 SpawnUnk4    u32     V1 always sends 0 for all three timestamp fields.
/// +0x28 Name         PascalString (length byte + chars)
///  ...  DoorIdSection  0x00 or {0x04 + DoorId u32}
///  ...  [Optional: if StaticFlags.HasNameSuffix — career-line name suffix data]
/// </code>
/// </remarks>
public sealed class CreateStaticResponse
{
    // ── Identity / Position ───────────────────────────────────────────────

    public ushort Oid      { get; set; }
    public ushort VfxState { get; set; }
    public ushort Heading  { get; set; }
    public ushort Z        { get; set; }
    public uint   X        { get; set; }
    public uint   Y        { get; set; }

    // ── Appearance ────────────────────────────────────────────────────────

    public ushort DisplayId { get; set; }

    /// <summary>Dead — client ignores this byte. Kept for wire compatibility.</summary>
    public byte UnkHi { get; set; }

    /// <summary>
    /// Compound bitfield: realm identity (bits 0-1), selectability (bit 2),
    /// extension flags (bit 5), and entity bit-flags (bits 4/6/7).
    /// See <see cref="StaticFlags"/> for per-bit documentation.
    /// </summary>
    public StaticFlags StaticFlags { get; set; }

    /// <summary>Reserved u16 pair — client combines with <see cref="Unks2"/> as a u32. Typically 0 in DB.</summary>
    public ushort Unks1 { get; set; }

    /// <summary>Low half of the reserved u32 with <see cref="Unks1"/>. Typically 0 in DB.</summary>
    public ushort Unks2 { get; set; }

    // ── State / Properties ────────────────────────────────────────────────

    /// <summary>
    /// Part of a level/rank block — client combines this byte with the high byte of
    /// <see cref="Flags"/> into a u16. Game objects hardcode a default level of 18 and rank of 0
    /// for nameplate color calculation; this spawn-specific byte is usually 0.
    /// </summary>
    public byte SpawnUnk1 { get; set; }

    /// <summary>
    /// Object-flags u16. Only the low byte is used by the client as a behavioral bitfield:
    /// bit 0 = target-selection mode, bit 2 = interactable, bit 3 = attackable,
    /// bit 4 = PvP-related, bit 5 = destructible/specialization.
    /// The high byte is combined with <see cref="SpawnUnk1"/> for the level/rank block.
    /// </summary>
    public ushort Flags { get; set; }

    /// <summary>Dead — client ignores this byte. Kept for wire compatibility.</summary>
    public byte SpawnUnk2 { get; set; }

    /// <summary>
    /// Compound u32 with byte-level layout: [dead][dead][Scale][Variant].
    /// <b>Scale</b> (byte 1): model scale factor — <c>value / 50.0</c> (50 = 1.0×). 0 also means 1.0×.
    /// <b>Variant</b> (byte 0): static configuration/variant byte — write-once, may control
    /// object interaction mode or mesh variant.
    /// The top two bytes are dead (client ignores them).
    /// </summary>
    public uint SpawnUnk3 { get; set; }

    /// <summary>
    /// Spawn timestamp (low u16 of a u32 pair). Combined with <see cref="Unks5"/> and
    /// <see cref="SpawnUnk4"/> to produce a creation-time float in seconds
    /// (from a 64-bit millisecond value). V1 sends 0 for all three fields.
    /// </summary>
    public ushort Unks4 { get; set; }

    /// <summary>Low half of the spawn-timestamp u32 with <see cref="Unks4"/>.</summary>
    public ushort Unks5 { get; set; }

    /// <summary>
    /// Spawn timestamp high word. Combined with <see cref="Unks4"/>/<see cref="Unks5"/>
    /// to form the 64-bit millisecond creation time. V1 sends 0.
    /// </summary>
    public uint SpawnUnk4 { get; set; }

    // ── Name ──────────────────────────────────────────────────────────────

    /// <summary>Object name as a length-prefixed Pascal string.</summary>
    [PascalString]
    public string Name { get; set; } = string.Empty;

    // ── DoorId section ────────────────────────────────────────────────────

    /// <summary>
    /// Variable-length DoorId block. Pre-computed by <see cref="From"/>:
    /// <list type="bullet">
    ///   <item><c>DoorId == 0</c>: single byte <c>0x00</c></item>
    ///   <item><c>DoorId != 0</c>: <c>0x04</c> followed by four bytes of DoorId (big-endian)</item>
    /// </list>
    /// </summary>
    [RawBytes]
    public byte[] DoorIdSection { get; set; } = [0x00];

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a <see cref="CreateStaticResponse"/> from a live <see cref="GameObjectEntity"/>
    /// and its spawn descriptor, using <paramref name="zone"/> to derive zone-local coordinates
    /// from the region-absolute <see cref="WorldEntity.Position"/>.
    /// </summary>
    /// <param name="entity">The game object entity (OID must be assigned).</param>
    /// <param name="descriptor">
    /// The spawn descriptor that sourced this entity — carries the raw DB fields
    /// needed for the wire protocol (Unks, DisplayId, SpawnUnk1-4, DoorId).
    /// </param>
    /// <param name="zone">Zone used to convert region-absolute → zone-local coordinates.</param>
    /// <param name="proto">
    /// Optional proto record. When provided, the proto name is used;
    /// falls back to <see cref="GameObjectEntity.Name"/>.
    /// </param>
    public static CreateStaticResponse From(
        GameObjectEntity          entity,
        GameObjectSpawnDescriptor descriptor,
        ZoneInfo                  zone,
        GameObjectProto?          proto = null)
    {
        // Unks[0] encodes two wire bytes — safe to access even if array is null/short
        var unks = descriptor.Unks;
        ushort unks0 = unks is { Length: > 0 } ? unks[0] : (ushort)0;
        ushort unks1 = unks is { Length: > 1 } ? unks[1] : (ushort)0;
        ushort unks2 = unks is { Length: > 2 } ? unks[2] : (ushort)0;
        ushort unks3 = unks is { Length: > 3 } ? unks[3] : (ushort)0;
        ushort unks4 = unks is { Length: > 4 } ? unks[4] : (ushort)0;
        ushort unks5 = unks is { Length: > 5 } ? unks[5] : (ushort)0;

        // flags = Unks[3]; set interactable bit 2 if applicable
        int flags = unks3;
        if (descriptor.Interactable || descriptor.DoorId != 0)
            flags |= 4;  // bit 2 = interactable (stops invalid target errors in V1)

        // DoorId section: 0x00 or 0x04 + uint (big-endian per PacketOut.WriteUInt32)
        byte[] doorSection;
        if (descriptor.DoorId != 0)
        {
            uint d = descriptor.DoorId;
            doorSection = [0x04, (byte)(d >> 24), (byte)(d >> 16), (byte)(d >> 8), (byte)d];
        }
        else
        {
            doorSection = [0x00];
        }

        return new CreateStaticResponse
        {
            Oid            = entity.ObjectId,
            VfxState       = entity.VfxState,
            Heading        = entity.Position.Heading,
            Z              = (ushort)entity.Position.Z,
            X              = (uint)entity.Position.X,
            Y              = (uint)entity.Position.Y,
            DisplayId      = descriptor.DisplayId,
            UnkHi          = (byte)(unks0 >> 8),
            StaticFlags    = (StaticFlags)(unks0 & 0xFF),
            Unks1          = unks1,
            Unks2          = unks2,
            SpawnUnk1      = descriptor.SpawnUnk1,
            Flags          = (ushort)flags,
            SpawnUnk2      = descriptor.SpawnUnk2,
            SpawnUnk3      = descriptor.SpawnUnk3,
            Unks4          = unks4,
            Unks5          = unks5,
            SpawnUnk4      = descriptor.SpawnUnk4,
            Name           = proto?.Name ?? entity.Name,
            DoorIdSection  = doorSection,
        };
    }
}
