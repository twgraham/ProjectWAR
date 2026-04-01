using Core.Infrastructure.Network.Serialization.Attributes;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Entities;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_CREATE_MONSTER</c> (0x72) — Notifies a client that an NPC/creature has become
/// visible. Sent whenever a <see cref="CreatureEntity"/> enters a player's visibility set.
/// </summary>
/// <remarks>
/// <para><b>Fixed header</b> (44 bytes, byte-swapped by client):</para>
/// <code>
/// +0x00 Oid(u16) | +0x02 0(u16) | +0x04 Heading(u16) | +0x06 Z(u16)
/// +0x08 X(u32)   | +0x0C Y(u32) | +0x10 SpeedZ(u16)=0
/// +0x12 ModelId(u16) | +0x14 Scale(b) | +0x15 Level(b) | +0x16 Faction/CreatureFlags(b)
/// +0x17 0(b) | +0x18 IsDeployed(b) | +0x19 0,0(fill) | +0x1B Emote(b)
/// +0x1C 0(b) | +0x1D Unk1(u16) | +0x1F 0(b)
/// +0x20 Unk2(u16) | +0x22 Unk3(u16) | +0x24 Unk4(u16) | +0x26 Unk5(u16)
/// +0x28 Unk6(u16) | +0x2A Title(u16)
/// </code>
/// <para><b>Stream section</b> (variable length, read sequentially):</para>
/// <code>
/// [PacketLength(1)] States | ModelOverrideByte(b)=0 |
/// Name(cstring) | [RawBytes] PostNameBlob | InteractTrainerType(b) |
/// OwnerOid(u16)=0 | ObjStateLen(b) | InvLen(b)=5 | SubPacketLen3(b)=0 |
/// [stationary mvt state: Oid(u16) LocalX(u16) LocalY(u16) Z(u16) PctHealth(b)
///   Flags(b)=0 ZoneId(b) 0(b) 0(u32) Heading(u16 LE)] |
/// InvOid(u16) | 0(b) | 0(b) | 0(b)
/// </code>
/// <para><b>PostNameBlob</b> is a compound blob whose content depends on creature state
/// and the <see cref="CreatureFlags"/> byte. The client reads these conditional sub-fields
/// in order:</para>
/// <list type="number">
///   <item>State bit 29 (<c>PhysicalEffects</c>) → 15 bytes of visual appearance data.</item>
///   <item><c>Unk1</c> low-byte <c>StateFlags</c> bits 0–4 → various sub-packets.</item>
///   <item>State bit 24 (<c>Effects</c>) → u16.</item>
///   <item>State bit 25 (<c>UnkOmnipresent</c>) → count(u8) + N bytes (almost always present: <c>{1, 10}</c>).</item>
///   <item><see cref="CreatureFlags.HasExtendedStreamData"/> → u32.</item>
/// </list>
/// <para>For standard creatures the blob is <c>{1, 10}</c> (feeds state-25 read).
/// State 29 adds 15 extra bytes at the start; other states/flags add more.</para>
/// </remarks>
public sealed class CreateMonsterResponse
{
    // ── Position / Identity ──────────────────────────────────────────────

    public ushort Oid { get; set; }

    /// <summary>Horizontal velocity — always 0 for spawning creatures.</summary>
    public ushort Padding1 { get; set; }
    public ushort Heading { get; set; }
    public ushort Z { get; set; }
    public uint X { get; set; }
    public uint Y { get; set; }
    public ushort SpeedZ { get; set; }     // always 0

    // ── Appearance ───────────────────────────────────────────────────────

    public ushort ModelId { get; set; }
    public byte Scale { get; set; }

    // ── Combat / Class ───────────────────────────────────────────────────

    public byte Level { get; set; }

    /// <summary>
    /// Compound bitfield: realm (bits 7–6), sub-type (bits 2–1), and behavioral flags.
    /// See <see cref="CreatureFlags"/> for the full bit layout.
    /// </summary>
    public CreatureFlags Faction { get; set; }

    public byte Padding2 { get; set; }     // always 0
    public byte IsDeployed { get; set; }   // siege: 1 if deployed

    [FixedLength(2)]
    public byte[] Padding3 { get; set; } = new byte[2];

    public byte Emote { get; set; }

    /// <summary>Debug-only byte. Client uses only in debug mode. Always 0.</summary>
    public byte Padding4 { get; set; }

    // ── Proto unknowns ────────────────────────────────────────────────────

    /// <summary>
    /// High byte: debug telemetry. Low byte: <c>StateFlags</c> —
    /// a bitfield (bits 0–4) controlling which optional sub-packets the client reads from
    /// the stream after <see cref="Name"/>. Always 0 for standard creatures.
    /// </summary>
    public ushort Unk1     { get; set; }    // V1: _Unks[1]
    public byte   Padding5 { get; set; }   // always 0

    /// <summary>Reserved u16 field. Purpose unknown; V1 always sends 0.</summary>
    public ushort Unk2    { get; set; }    // V1: _Unks[2]

    /// <summary>Dead field — client ignores these 2 bytes.</summary>
    public ushort Unk3    { get; set; }    // V1: _Unks[3]

    /// <summary>
    /// Combined with <see cref="Unk5"/> as a single u32 on the client.
    /// Purpose unknown; V1 always sends 0.
    /// </summary>
    public ushort Unk4    { get; set; }    // V1: _Unks[4] — high u16 of combined u32

    /// <summary>Low u16 of the combined u32 with <see cref="Unk4"/>.</summary>
    public ushort Unk5    { get; set; }    // V1: _Unks[5] — low u16 of combined u32

    /// <summary>Part of the level/rank block. Purpose unknown; V1 always sends 0.</summary>
    public ushort Unk6    { get; set; }    // V1: _Unks[6]
    public ushort Title   { get; set; }

    // ── States ────────────────────────────────────────────────────────────

    /// <summary>
    /// Combined: <c>Proto.States + optional quest-state byte</c>.
    /// Written with a 1-byte length prefix.
    /// </summary>
    [PacketLength(1)]
    public byte[] States { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Model override trigger — if non-zero the client performs a model lookup.
    /// V1 always writes 0; must remain 0 for normal creatures.
    /// </summary>
    public byte ModelOverrideByte { get; set; }

    // ── Name / PostNameBlob ───────────────────────────────────────────────

    /// <summary>Creature name (gendered form — may contain <c>^</c>) as null-terminated string.</summary>
    [CString]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Compound stream blob written between <see cref="Name"/> and <see cref="InteractTrainerType"/>.
    /// No length prefix — the client determines how many bytes to consume based on creature
    /// state bits and the <see cref="CreatureFlags"/> / <see cref="Unk1"/> header fields.
    /// </summary>
    /// <remarks>
    /// <para>V1 calls this <c>FigLeafData</c>, but it feeds <em>multiple</em> conditional reads:</para>
    /// <list type="bullet">
    ///   <item>State bit 29 (<c>PhysicalEffects</c>): 15 bytes of visual appearance.</item>
    ///   <item>State bit 24 (<c>Effects</c>): u16 visual effect ID.</item>
    ///   <item>State bit 25 (<c>UnkOmnipresent</c>): count(u8) + N data bytes.</item>
    ///   <item><see cref="CreatureFlags.HasExtendedStreamData"/>: u32 extended data.</item>
    /// </list>
    /// <para>For standard creatures this is <c>{ 1, 10 }</c> (feeds the state-25 read).
    /// State 29 prepends 15 bytes; other states/flags can widen the blob further.</para>
    /// </remarks>
    [RawBytes]
    public byte[] PostNameBlob { get; set; } = Array.Empty<byte>();

    public byte InteractTrainerType { get; set; }

    // ── Object State ─────────────────────────────────────────────────────

    public ushort OwnerOid { get; set; }   // always 0

    /// <summary>Byte count of the object-state block that follows (= 12 for stationary creatures).</summary>
    public byte ObjStateLen { get; set; }

    /// <summary>F_PLAYER_INVENTORY sub-block length (always 5).</summary>
    public byte InvLen { get; set; } = 5;

    /// <summary>
    /// Third sub-packet length — dispatched with opcodes 0x4E/0x4F.
    /// V1 always writes 0 (no third sub-packet).
    /// </summary>
    public byte SubPacketLen3 { get; set; }

    // ── Stationary Movement State ─────────────────────────────────────────
    // Oid(u16) LocalX(u16) LocalY(u16) Z(u16) PctHealth(b) Flags(b) ZoneId(b) Unk8(b) Unk9(u32) Heading(u16 LE)

    public ushort MvtOid     { get; set; }
    public ushort MvtLocalX  { get; set; }
    public ushort MvtLocalY  { get; set; }
    public ushort MvtZ       { get; set; }
    public byte   PctHealth  { get; set; }
    public byte   MvtFlags   { get; set; }    // always 0 (stationary)
    public byte   ZoneId     { get; set; }
    public byte   MvtPad     { get; set; }    // always 0
    public uint   MvtUnk     { get; set; }    // always 0

    /// <summary>Heading in little-endian (V1: <c>WriteUInt16R</c> = LE).</summary>
    [LittleEndian]
    public ushort MvtHeading { get; set; }

    // ── F_PLAYER_INVENTORY (minimal NPC version) ─────────────────────────

    public ushort InvOid { get; set; }
    public byte InvPad1 { get; set; }
    public byte InvPad2 { get; set; }
    public byte InvPad3 { get; set; }

    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a <see cref="CreateMonsterResponse"/> from a live <see cref="CreatureEntity"/>,
    /// its template, the zone it resides in, and an optional per-player quest state byte.
    /// </summary>
    /// <param name="entity">The creature entity (Oid must be assigned).</param>
    /// <param name="proto">The creature's prototype data.</param>
    /// <param name="zone">Zone info used to convert region-absolute → zone-local coordinates.</param>
    /// <param name="questState">
    /// Optional client-visible quest indicator (0 = none/merchant, see <c>CreatureState</c> enum).
    /// </param>
    public static CreateMonsterResponse From(
        CreatureEntity entity,
        CreatureProto proto,
        ZoneInfo zone,
        byte questState = 0)
    {
        // Header X/Y are region-absolute (V1: WorldPosition.X/Y).
        // Movement-state X/Y are zone-local (V1: CalculPin subtracts zone offset).
        var (localX, localY) = entity.Position.ToZoneLocal(zone.OffX, zone.OffY);

        // Build the combined states blob: proto states + optional quest-state byte
        var protoStates   = proto.States ?? Array.Empty<byte>();
        var statesArray   = questState != 0
            ? [..protoStates, questState]
            : protoStates;

        // ObjStateLen = bytes from the sub-block start (after InvLen+Padding7) to end of
        // stationary movement state. That is: (2+2+2+2+1+1+1+1+4+2) = 18 bytes for stationary.
        // V1 computes this dynamically; we hard-code stationary = 18 bytes
        // (ObjStateLen byte itself + InvLen byte + Padding7 byte + mvt block).
        // The actual value measured by V1 is latestPos - (objStateLenPos + 3) = 15 bytes mvt
        // block + overhead. Stationary mvt block (WriteUInt16R heading only = 2 bytes) gives:
        //   Oid(2)+LocalX(2)+LocalY(2)+Z(2)+PctHealth(1)+Flags(1)+ZoneId(1)+Unk8(1)+Unk9(4)+Heading(2) = 18
        const byte ObjStateLenStationary = 18;

        return new CreateMonsterResponse
        {
            Oid         = entity.ObjectId,
            Heading     = entity.Position.Heading,
            Z           = (ushort)entity.Position.Z,
            X           = (uint)entity.Position.X,
            Y           = (uint)entity.Position.Y,
            ModelId     = entity.ModelId,
            Scale       = (byte)entity.Scale,
            Level       = entity.Level,
            Faction     = (CreatureFlags)entity.Faction,
            Emote       = entity.Emote,
            Unk1        = proto.Unk1,
            Unk2        = proto.Unk2,
            Unk3        = proto.Unk3,
            Unk4        = proto.Unk4,
            Unk5        = proto.Unk5,
            Unk6        = proto.Unk6,
            Title       = proto.Title,
            States      = statesArray,
            Name        = proto.Name,
            PostNameBlob = proto.FigLeafData ?? Array.Empty<byte>(),
            OwnerOid    = 0,
            ObjStateLen = ObjStateLenStationary,
            // Movement state (stationary)
            MvtOid      = entity.ObjectId,
            MvtLocalX   = (ushort)localX,
            MvtLocalY   = (ushort)localY,
            MvtZ        = (ushort)entity.Position.Z,
            PctHealth   = entity.Health.Percent,
            ZoneId      = (byte)zone.ZoneId,
            MvtHeading  = entity.Position.Heading,
            // F_PLAYER_INVENTORY
            InvOid      = entity.ObjectId,
        };
    }
}
