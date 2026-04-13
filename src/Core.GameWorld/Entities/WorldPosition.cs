namespace Core.GameWorld.Entities;

/// <summary>
/// Immutable value type representing an entity's position in the game world.
/// Coordinates are <b>region-wide</b>: <c>X = zoneInfo.OffX * 4096 + zoneLocalX</c>.
/// Zone-local values are derived at the network boundary when serializing packets.
/// <para>
/// Region-wide storage eliminates per-check <c>ZoneInfo</c> lookups on the hottest
/// path (distance calculations for spatial queries, combat, packet dispatch).
/// </para>
/// </summary>
/// <param name="RegionId">The region this position belongs to.</param>
/// <param name="X">Region-wide X coordinate (game units).</param>
/// <param name="Y">Region-wide Y coordinate (game units).</param>
/// <param name="Z">Height (game units, zone-independent).</param>
/// <param name="Heading">Facing direction (0–4095).</param>
/// <param name="ZoneId">The zone this position falls within.</param>
public readonly record struct WorldPosition(ushort RegionId, int X, int Y, int Z, ushort Heading, ushort ZoneId)
{
    /// <summary>A position at the origin with no region or zone.</summary>
    public static readonly WorldPosition Zero = default;

    /// <summary>
    /// Creates a <see cref="WorldPosition"/> from zone-local coordinates and zone metadata.
    /// This is the primary conversion from inbound packet data or database records.
    /// </summary>
    /// <param name="regionId">The region the zone belongs to.</param>
    /// <param name="zoneId">The zone identifier.</param>
    /// <param name="offX">Zone X offset in cell units (from <c>ZoneInfo.OffX</c>).</param>
    /// <param name="offY">Zone Y offset in cell units (from <c>ZoneInfo.OffY</c>).</param>
    /// <param name="localX">Zone-local X coordinate.</param>
    /// <param name="localY">Zone-local Y coordinate.</param>
    /// <param name="z">Height.</param>
    /// <param name="heading">Facing direction.</param>
    public static WorldPosition FromZoneLocal(
        ushort regionId, ushort zoneId, int offX, int offY,
        int localX, int localY, int z, ushort heading)
    {
        return new WorldPosition(
            regionId,
            offX * 4096 + localX,
            offY * 4096 + localY,
            z,
            heading,
            zoneId);
    }

    /// <summary>
    /// Creates a <see cref="WorldPosition"/> from region-absolute (world) coordinates.
    /// No zone offset conversion is applied — the caller supplies coordinates that are
    /// already region-wide.
    /// </summary>
    /// <param name="regionId">The region the position belongs to.</param>
    /// <param name="zoneId">The zone identifier.</param>
    /// <param name="worldX">Region-absolute X coordinate.</param>
    /// <param name="worldY">Region-absolute Y coordinate.</param>
    /// <param name="z">Height.</param>
    /// <param name="heading">Facing direction.</param>
    public static WorldPosition FromRegionAbsolute(
        ushort regionId, ushort zoneId, int worldX, int worldY, int z, ushort heading)
    {
        return new WorldPosition(regionId, worldX, worldY, z, heading, zoneId);
    }

    /// <summary>
    /// Converts region-wide coordinates back to zone-local by subtracting the zone offset.
    /// Used when serializing position data for outbound network packets.
    /// </summary>
    /// <param name="offX">Zone X offset in cell units (from <c>ZoneInfo.OffX</c>).</param>
    /// <param name="offY">Zone Y offset in cell units (from <c>ZoneInfo.OffY</c>).</param>
    /// <returns>Zone-local X and Y coordinates.</returns>
    public (int LocalX, int LocalY) ToZoneLocal(int offX, int offY)
    {
        return (X - offX * 4096, Y - offY * 4096);
    }

    /// <summary>
    /// Computes the cell grid index for this position.
    /// Cell coordinates are <c>regionX / 4096</c> (integer division).
    /// </summary>
    public (int CellX, int CellY) CellIndex => (X / 4096, Y / 4096);

    /// <summary>
    /// Returns the squared 2D distance (ignoring Z) to another position.
    /// Avoids the <c>sqrt</c> call — use for range comparisons against squared thresholds.
    /// Both positions must be in the same region for the result to be meaningful.
    /// </summary>
    public long DistanceSquared2D(WorldPosition other)
    {
        long dx = X - other.X;
        long dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// Returns the squared 3D distance to another position.
    /// Both positions must be in the same region.
    /// </summary>
    public long DistanceSquared3D(WorldPosition other)
    {
        long dx = X - other.X;
        long dy = Y - other.Y;
        long dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
