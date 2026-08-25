using System.Numerics;
using Core.Spatial.Math;

namespace Core.Spatial.Tests;

public class RayIntersectionTests
{
    [Fact]
    public void RayTriangle_DirectHit_ReturnsTrue()
    {
        // Triangle in the XY plane at Z=0.
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 1, 0);

        // Ray pointing straight down from above the center of the triangle.
        var origin = new Vector3(0.2f, 0.2f, 5f);
        var dir = Vector3.Normalize(new Vector3(0, 0, -1));

        bool hit = RayIntersection.RayTriangle(origin, dir, v0, v1, v2, out float t, out var normal);

        Assert.True(hit);
        Assert.InRange(t, 4.99f, 5.01f);
        // Normal should point +Z (front face of the triangle).
        Assert.True(normal.Z > 0.9f);
    }

    [Fact]
    public void RayTriangle_Miss_ReturnsFalse()
    {
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 1, 0);

        // Ray goes past the triangle.
        var origin = new Vector3(5f, 5f, 5f);
        var dir = Vector3.Normalize(new Vector3(0, 0, -1));

        bool hit = RayIntersection.RayTriangle(origin, dir, v0, v1, v2, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void RayTriangle_BehindRay_ReturnsFalse()
    {
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 1, 0);

        // Ray pointing away from the triangle.
        var origin = new Vector3(0.2f, 0.2f, 5f);
        var dir = Vector3.Normalize(new Vector3(0, 0, 1));

        bool hit = RayIntersection.RayTriangle(origin, dir, v0, v1, v2, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void RayAabb_HitBox_ReturnsTrue()
    {
        var box = new Aabb(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        var origin = new Vector3(0, 0, 5);
        var invDir = Vector3.One / Vector3.Normalize(new Vector3(0, 0, -1));

        bool hit = RayIntersection.RayAabb(origin, invDir, in box, out float tNear, out float tFar);

        Assert.True(hit);
        Assert.InRange(tNear, 3.99f, 4.01f);
        Assert.InRange(tFar, 5.99f, 6.01f);
    }

    [Fact]
    public void RayAabb_MissBox_ReturnsFalse()
    {
        var box = new Aabb(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        var origin = new Vector3(5, 5, 5);
        var invDir = Vector3.One / Vector3.Normalize(new Vector3(0, 0, -1));

        bool hit = RayIntersection.RayAabb(origin, invDir, in box, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void RayAabb_BehindRay_ReturnsFalse()
    {
        var box = new Aabb(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        var origin = new Vector3(0, 0, 5);
        var invDir = Vector3.One / Vector3.Normalize(new Vector3(0, 0, 1)); // Away from box.

        bool hit = RayIntersection.RayAabb(origin, invDir, in box, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void ComputeTriangleNormal_HorizontalTriangle_PointsUp()
    {
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 1, 0);

        var normal = RayIntersection.ComputeTriangleNormal(v0, v1, v2);

        Assert.InRange(normal.X, -0.01f, 0.01f);
        Assert.InRange(normal.Y, -0.01f, 0.01f);
        Assert.InRange(MathF.Abs(normal.Z), 0.99f, 1.01f);
    }
}
