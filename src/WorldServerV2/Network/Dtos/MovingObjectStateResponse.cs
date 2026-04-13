using Core.Domain.Entities;
using Core.GameWorld.Entities;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_OBJECT_STATE</c> (0x09) — moving variant.
/// Broadcasts a moving entity's current state (position, health, speed, destination)
/// to players within visibility range.
/// </summary>
/// <remarks>
/// <para><b>Wire format</b> (26 bytes):</para>
/// <code>
/// Oid(u16) X(u16) Y(u16) Z(u16) PctHealth(u8) Flags(u8) ZoneId(u8) Unk1(u8) Unk2(u32)
/// Speed(u16 LE) DestUnk(u8) DestX(u16 LE) DestY(u16 LE) DestZ(u16 LE) DestZoneId(u8)
/// </code>
/// <para>
/// When the entity is also looking at a target (<see cref="ObjectStateFlags.LookingAt"/>),
/// a <c>TargetOid(u16 LE)</c> is appended after <c>DestZoneId</c>. That extension is not
/// yet modelled — a dedicated <c>MovingWithTargetObjectStateResponse</c> will be added
/// when the movement system is wired.
/// </para>
/// </remarks>
public sealed class MovingObjectStateResponse
{
    // ── Base fields (16 bytes) ──────────────────────────────────────────

    /// <summary>Entity object identifier.</summary>
    public ushort Oid { get; init; }

    /// <summary>Zone-local X coordinate (current position).</summary>
    public ushort X { get; init; }

    /// <summary>Zone-local Y coordinate (current position).</summary>
    public ushort Y { get; init; }

    /// <summary>Height (game units).</summary>
    public ushort Z { get; init; }

    /// <summary>Health percentage (0–100).</summary>
    public byte PctHealth { get; init; }

    /// <summary>
    /// Bitfield — always includes <see cref="ObjectStateFlags.Moving"/> for this variant.
    /// </summary>
    public byte Flags { get; init; }

    /// <summary>Zone identifier (truncated to byte for the wire format).</summary>
    public byte ZoneId { get; init; }

    /// <summary>
    /// Unknown byte. V1 writes 0 normally; 6 when forcing a position update;
    /// 4 for <c>BuffHostObject</c>.
    /// </summary>
    public byte Unk1 { get; init; }

    /// <summary>Unknown u32. V1 writes 0 normally; 3 for <c>BuffHostObject</c>.</summary>
    public uint Unk2 { get; init; }

    // ── Movement tail (10 bytes) ────────────────────────────────────────

    /// <summary>Movement speed (game units, pre-scaled by 2.35).</summary>
    [LittleEndian]
    public ushort Speed { get; init; }

    /// <summary>Destination unknown byte — V1 always writes 0.</summary>
    public byte DestUnk { get; init; }

    /// <summary>Zone-local destination X.</summary>
    [LittleEndian]
    public ushort DestX { get; init; }

    /// <summary>Zone-local destination Y.</summary>
    [LittleEndian]
    public ushort DestY { get; init; }

    /// <summary>Destination height.</summary>
    [LittleEndian]
    public ushort DestZ { get; init; }

    /// <summary>Destination zone identifier.</summary>
    public byte DestZoneId { get; init; }

    // ── Factory Method ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a moving state packet for a <see cref="UnitEntity"/>.
    /// </summary>
    public static MovingObjectStateResponse From(
        UnitEntity entity,
        ZoneInfo zone,
        ushort speed,
        ushort destX,
        ushort destY,
        ushort destZ,
        byte destZoneId)
    {
        var (localX, localY) = entity.Position.ToZoneLocal(zone.OffX, zone.OffY);

        return new MovingObjectStateResponse
        {
            Oid = entity.ObjectId,
            X = (ushort)localX,
            Y = (ushort)localY,
            Z = (ushort)entity.Position.Z,
            PctHealth = entity.Health.Percent,
            Flags = (byte)ObjectStateFlags.Moving,
            ZoneId = (byte)zone.ZoneId,
            Speed = speed,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            DestZoneId = destZoneId,
        };
    }
}
