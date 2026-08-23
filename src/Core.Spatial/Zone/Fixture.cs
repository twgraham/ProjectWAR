using System.Numerics;

namespace Core.Spatial.Zone;

/// <summary>
/// Represents a collision fixture (building, door, wall, etc.) within a zone.
/// Each fixture owns a contiguous range of triangles in the zone's collision mesh.
/// </summary>
public sealed class Fixture
{
    /// <summary>Surface type encoding (door, water, fixture, etc.).</summary>
    public int SurfaceType { get; set; }

    /// <summary>Whether this fixture's triangles participate in intersection tests.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Start index into the zone's triangle array.</summary>
    public int TriangleStartIndex { get; set; }

    /// <summary>Number of triangles belonging to this fixture.</summary>
    public int TriangleCount { get; set; }

    /// <summary>Fixture unique ID (lower 24 bits of the packed triangle ID).</summary>
    public int Id { get; set; }

    /// <summary>Bounding box minimum corner.</summary>
    public Vector3 BoundsMin { get; set; }

    /// <summary>Bounding box maximum corner.</summary>
    public Vector3 BoundsMax { get; set; }
}

/// <summary>
/// Public information about a fixture, returned by <see cref="ZoneManager.GetFixtureInfo"/>.
/// </summary>
public struct FixtureInfo
{
    public float X1, Y1, Z1;
    public float X2, Y2, Z2;
    public int SurfaceType;
    public int UniqueId;
}
