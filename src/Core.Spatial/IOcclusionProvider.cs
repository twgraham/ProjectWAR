namespace Core.Spatial;

/// <summary>
/// Read-only query interface for spatial occlusion and terrain lookups against loaded
/// zone geometry. Loading and lifecycle are the responsibility of the implementing class
/// (e.g. <see cref="Zone.ZoneManager"/>), not this interface.
/// </summary>
public interface IOcclusionProvider
{
    /// <summary>
    /// Whether zone data has been initialized.
    /// </summary>
    bool Initialized { get; }

    /// <summary>
    /// Returns the terrain height (Z) at the given (x, y) position within a zone.
    /// Returns -1 if the position is in a hole area, 0 if the zone is not loaded.
    /// </summary>
    int GetTerrainZ(int zoneId, int x, int y);

    /// <summary>
    /// Casts a ray segment between two points and tests for occlusion by geometry,
    /// terrain, and water surfaces. Returns the <see cref="OcclusionResult"/>.
    /// </summary>
    OcclusionResult Raytest(
        int zoneId,
        float originX, float originY, float originZ,
        float targetX, float targetY, float targetZ,
        bool terrain, ref OcclusionInfo result);
}
