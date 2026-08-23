using Core.Spatial.KdTree;

namespace Core.Spatial.Zone;

/// <summary>
/// Holds all spatial data for a single game zone: terrain heightmap,
/// collision/water KD-trees, and fixture metadata.
/// </summary>
public sealed class ZoneData
{
    public int RegionId { get; set; }
    public int ZoneId { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    /// <summary>Terrain heightmap for ground-level Z queries.</summary>
    public TerrainGrid? Terrain { get; set; }

    /// <summary>KD-tree over solid collision geometry (fixtures, buildings, doors).</summary>
    public KdTreeAccel? CollisionTree { get; set; }

    /// <summary>KD-tree over water surface geometry.</summary>
    public KdTreeAccel? WaterTree { get; set; }

    /// <summary>Fixtures keyed by their packed ID (instanceId &lt;&lt; 24 | uniqueId).</summary>
    public Dictionary<int, Fixture> Fixtures { get; } = [];

    /// <summary>Ordered list of fixtures for index-based access.</summary>
    public List<Fixture> FixtureList { get; } = [];
}
