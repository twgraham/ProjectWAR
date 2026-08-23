namespace Core.Spatial;

/// <summary>
/// Result of a ray/segment intersection query against zone geometry.
/// </summary>
public enum OcclusionResult
{
    NotLoaded = -1,
    NotOccluded = 0,
    OccludedByGeometry = 1,
    OccludedByTerrain = 2,
    OccludedByWater = 3,
    OccludedByLava = 4,
    OccludedByDynamicObject = 5,
    OccludedByClosedDoor = 6,
}
