using System.Numerics;
using Core.Spatial.KdTree;

namespace Core.Spatial.Tests;

public class KdTreeTests
{
    /// <summary>
    /// Builds a small tree with two triangles and verifies intersection works.
    /// </summary>
    [Fact]
    public void Intersect_HitsClosestTriangle()
    {
        // Two horizontal triangles at different heights.
        var vertices = new Vector3[]
        {
            // Triangle 0 vertices at Z=5
            new(0, 0, 5), new(10, 0, 5), new(0, 10, 5),
            // Triangle 1 vertices at Z=10
            new(0, 0, 10), new(10, 0, 10), new(0, 10, 10),
        };

        // Triangle 0 uses vertices 0,1,2. Triangle 1 uses 3,4,5.
        var triangles = new Vector3[]
        {
            new(0, 1, 2), // indices into vertices
            new(3, 4, 5),
        };

        var triangleIds = new int[] { 100, 200 };

        var tree = KdTreeAccel.Build(triangles, vertices, triangleIds, maxTrisPerLeaf: 10);

        // Ray from above, pointing down.
        var origin = new Vector3(2, 2, 20);
        var dir = Vector3.Normalize(new Vector3(0, 0, -1));

        int hit = tree.Intersect(origin, dir, out float t, out var hitPoint, out var normal);

        // Should hit the higher triangle (Z=10) first.
        Assert.Equal(200, hit);
        Assert.InRange(hitPoint.Z, 9.99f, 10.01f);
    }

    [Fact]
    public void Intersect_MissesWhenNoGeometry()
    {
        var vertices = new Vector3[]
        {
            new(0, 0, 0), new(1, 0, 0), new(0, 1, 0),
        };

        var triangles = new Vector3[]
        {
            new(0, 1, 2),
        };

        var triangleIds = new int[] { 42 };
        var tree = KdTreeAccel.Build(triangles, vertices, triangleIds, maxTrisPerLeaf: 10);

        // Ray that misses the triangle entirely.
        var origin = new Vector3(50, 50, 10);
        var dir = Vector3.Normalize(new Vector3(0, 0, -1));

        int hit = tree.Intersect(origin, dir, out _, out _, out _);

        Assert.Equal(0, hit);
    }

    [Fact]
    public void SetTriangleVisible_HidesTriangleFromIntersect()
    {
        var vertices = new Vector3[]
        {
            new(0, 0, 0), new(10, 0, 0), new(0, 10, 0),
        };

        var triangles = new Vector3[]
        {
            new(0, 1, 2),
        };

        var triangleIds = new int[] { 99 };
        var tree = KdTreeAccel.Build(triangles, vertices, triangleIds, maxTrisPerLeaf: 10);

        // Verify hit first.
        var origin = new Vector3(2, 2, 5);
        var dir = Vector3.Normalize(new Vector3(0, 0, -1));

        int hit = tree.Intersect(origin, dir, out _, out _, out _);
        Assert.Equal(99, hit);

        // Hide the triangle.
        tree.SetTriangleVisible(0, false);

        hit = tree.Intersect(origin, dir, out _, out _, out _);
        Assert.Equal(0, hit);

        // Show it again.
        tree.SetTriangleVisible(0, true);
        hit = tree.Intersect(origin, dir, out _, out _, out _);
        Assert.Equal(99, hit);
    }

    [Fact]
    public void Build_LargeTriangleCount_ProducesValidTree()
    {
        // Generate a grid of triangles.
        const int gridSize = 10;
        var vertices = new List<Vector3>();
        var triangles = new List<Vector3>();
        var triangleIds = new List<int>();

        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                int baseIdx = vertices.Count;
                vertices.Add(new Vector3(x, y, 0));
                vertices.Add(new Vector3(x + 1, y, 0));
                vertices.Add(new Vector3(x, y + 1, 0));

                triangles.Add(new Vector3(baseIdx, baseIdx + 1, baseIdx + 2));
                triangleIds.Add(x * gridSize + y + 1);
            }
        }

        var tree = KdTreeAccel.Build(
            triangles.ToArray(),
            vertices.ToArray(),
            triangleIds.ToArray(),
            maxTrisPerLeaf: 4);

        Assert.True(tree.NodeCount > 1, "Tree should have multiple nodes with maxTrisPerLeaf=4");

        // Cast ray at known grid cell.
        var origin = new Vector3(5.2f, 5.2f, 10);
        var dir = Vector3.Normalize(new Vector3(0, 0, -1));

        int hit = tree.Intersect(origin, dir, out _, out var hitPoint, out _);

        Assert.NotEqual(0, hit);
        Assert.InRange(hitPoint.Z, -0.01f, 0.01f);
    }
}
