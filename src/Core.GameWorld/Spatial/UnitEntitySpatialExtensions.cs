using Core.GameWorld.Entities;
using Core.Spatial;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Extension methods for spatial queries on <see cref="UnitEntity"/>.
/// <para>
/// Entity-level methods account for <see cref="UnitEntity.BaseRadius"/> (edge-to-edge
/// distance) and resolve <see cref="IOcclusionProvider"/> from the entity's
/// <see cref="WorldEntity.RegionServices"/> — no static singletons needed.
/// </para>
/// <para>
/// All distance methods return values in <b>feet</b> (12 game units = 1 foot).
/// </para>
/// </summary>
public static class UnitEntitySpatialExtensions
{
    // ═════════════════════════════════════════════════════════════════════
    //  DISTANCE
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the 2D edge-to-edge distance between two entities, in feet.
    /// Subtracts both entities' <see cref="UnitEntity.BaseRadius"/> (collision radii)
    /// and clamps to zero. Matches V1's <c>GetDistanceToObject(factorRadius: true)</c>.
    /// </summary>
    public static float DistanceTo(this UnitEntity self, UnitEntity other)
    {
        float raw = self.Position.RawDistanceTo2D(other.Position);
        float adjusted = raw - self.BaseRadius - other.BaseRadius;
        return MathF.Max(0f, adjusted);
    }

    /// <summary>
    /// Returns the 3D edge-to-edge distance between two entities, in feet.
    /// </summary>
    public static float DistanceTo3D(this UnitEntity self, UnitEntity other)
    {
        float raw = self.Position.RawDistanceTo3D(other.Position);
        float adjusted = raw - self.BaseRadius - other.BaseRadius;
        return MathF.Max(0f, adjusted);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  LINE OF SIGHT
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks line-of-sight from <paramref name="self"/> to <paramref name="target"/>
    /// using the <see cref="IOcclusionProvider"/> from <see cref="WorldEntity.RegionServices"/>.
    /// <para>
    /// Returns <c>true</c> (clear LOS) when:
    /// <list type="bullet">
    ///   <item>No <see cref="RegionServices"/> is set (entity not in a region).</item>
    ///   <item>The occlusion provider is <c>null</c> or not initialized.</item>
    ///   <item>The entities are in different zones (cross-zone LOS not supported — V1 parity).</item>
    ///   <item>The ray is not occluded by geometry, terrain, or water.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static bool HasLineOfSight(this UnitEntity self, UnitEntity target)
    {
        var provider = self.RegionServices?.Occlusion;
        if (provider is null || !provider.Initialized)
            return true;

        // Cross-zone LOS is not supported (matches V1 behaviour).
        if (self.Position.ZoneId != target.Position.ZoneId)
            return true;

        int zoneId = self.Position.ZoneId;

        var info = new OcclusionInfo();
        var result = provider.Raytest(
            zoneId,
            self.Position.X, self.Position.Y, self.Position.Z + RegionConstants.CharacterHeight,
            target.Position.X, target.Position.Y, target.Position.Z + RegionConstants.CharacterHeight,
            terrain: true,
            ref info);

        return result == OcclusionResult.NotOccluded;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  FACING / ARC CHECK
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if <paramref name="target"/> is within the frontal arc of
    /// <paramref name="self"/>. Default arc is 140° (±70° from heading — V1 parity).
    /// </summary>
    public static bool IsInFrontArc(
        this UnitEntity self,
        UnitEntity target,
        float arcDegrees = RegionConstants.DefaultFrontArcDegrees)
    {
        return self.Position.IsInFrontArc(target.Position, arcDegrees);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  RANGE GATING
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if the edge-to-edge 2D distance between <paramref name="self"/>
    /// and <paramref name="other"/> is ≤ <paramref name="rangeFeet"/>.
    /// Matches V1's <c>IsInCastRange</c> semantics.
    /// </summary>
    public static bool IsInRange(this UnitEntity self, UnitEntity other, float rangeFeet)
    {
        return self.DistanceTo(other) <= rangeFeet;
    }
}
