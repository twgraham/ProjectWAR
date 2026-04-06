using Core.Domain.Entities;
using Core.GameWorld.Entities;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_OBJECT_STATE</c> (0x09) — stationary variant.
/// Broadcasts a non-moving entity's current state (position, health, heading)
/// to players within visibility range. Sent periodically as a keepalive and
/// immediately when observable state changes.
/// </summary>
/// <remarks>
/// <para><b>Wire format</b> (18 bytes):</para>
/// <code>
/// Oid(u16) X(u16) Y(u16) Z(u16) PctHealth(u8) Flags(u8) ZoneId(u8) Unk1(u8) Unk2(u32) Heading(u16 LE)
/// </code>
/// <para>
/// This packet shares the same wire format as the movement-state block embedded in
/// <c>F_CREATE_MONSTER</c> — the only difference is the opcode header.
/// </para>
/// </remarks>
public sealed class StationaryObjectStateResponse
{
    // ── Wire fields (18 bytes) ──────────────────────────────────────────

    /// <summary>Entity object identifier.</summary>
    public ushort Oid { get; init; }

    /// <summary>Zone-local X coordinate.</summary>
    public ushort X { get; init; }

    /// <summary>Zone-local Y coordinate.</summary>
    public ushort Y { get; init; }

    /// <summary>Height (game units).</summary>
    public ushort Z { get; init; }

    /// <summary>Health percentage (0–100). Always 100 for game objects.</summary>
    public byte PctHealth { get; init; }

    /// <summary>
    /// Bitfield controlling the tail layout. Always <see cref="ObjectStateFlags.None"/>
    /// for stationary entities (heading-only tail).
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

    /// <summary>Facing direction (little-endian on wire).</summary>
    [LittleEndian]
    public ushort Heading { get; init; }

    // ── Factory Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a stationary state packet for a <see cref="UnitEntity"/> (creature, pet).
    /// </summary>
    public static StationaryObjectStateResponse From(UnitEntity entity, ZoneInfo zone)
    {
        var (localX, localY) = entity.Position.ToZoneLocal(zone.OffX, zone.OffY);

        return new StationaryObjectStateResponse
        {
            Oid = entity.ObjectId,
            X = (ushort)localX,
            Y = (ushort)localY,
            Z = (ushort)entity.Position.Z,
            PctHealth = entity.Health.Percent,
            Flags = (byte)ObjectStateFlags.None,
            ZoneId = (byte)zone.ZoneId,
            Heading = entity.Position.Heading,
        };
    }

    /// <summary>
    /// Builds a stationary state packet for a <see cref="GameObjectEntity"/>.
    /// Game objects have no health component (PctHealth fixed at 100) and never move.
    /// </summary>
    public static StationaryObjectStateResponse From(GameObjectEntity entity, ZoneInfo zone)
    {
        var (localX, localY) = entity.Position.ToZoneLocal(zone.OffX, zone.OffY);

        return new StationaryObjectStateResponse
        {
            Oid = entity.ObjectId,
            X = (ushort)localX,
            Y = (ushort)localY,
            Z = (ushort)entity.Position.Z,
            PctHealth = 100,
            Flags = (byte)ObjectStateFlags.None,
            ZoneId = (byte)zone.ZoneId,
            Heading = entity.Position.Heading,
        };
    }
}
