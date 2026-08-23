namespace Core.Spatial;

/// <summary>
/// Contains the result of an occlusion ray query, including the hit location,
/// surface type, fixture information, and water depth if applicable.
/// </summary>
public struct OcclusionInfo
{
    public OcclusionResult Result;
    public float HitX;
    public float HitY;
    public float HitZ;
    public float SafeX;
    public float SafeY;
    public float SafeZ;
    public int FixtureId;
    public SurfaceType SurfaceType;
    public float WaterDepth;

    public override readonly string ToString() =>
        $"Result:{Result} Hit:({HitX}, {HitY}, {HitZ}) Surface:{SurfaceType} WaterDepth:{WaterDepth} Fixture:{FixtureId}";
}
