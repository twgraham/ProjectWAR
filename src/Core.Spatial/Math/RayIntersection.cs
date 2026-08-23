using System.Numerics;
using System.Runtime.CompilerServices;

namespace Core.Spatial.Math;

/// <summary>
/// Low-level ray intersection primitives.
/// All methods are static and use <see cref="Vector3"/> for SIMD acceleration.
/// </summary>
public static class RayIntersection
{
    /// <summary>
    /// Ray vs AABB slab test. Returns true if the ray intersects the box,
    /// with <paramref name="tNear"/> and <paramref name="tFar"/> set to the entry/exit distances.
    /// </summary>
    /// <param name="rayOrigin">Ray origin point.</param>
    /// <param name="invDir">Pre-computed inverse ray direction (<c>Vector3.One / rayDir</c>).
    /// Pre-computing avoids redundant division when testing the same ray against many boxes.</param>
    /// <param name="box">The axis-aligned bounding box to test.</param>
    /// <param name="tNear">Entry distance along the ray.</param>
    /// <param name="tFar">Exit distance along the ray.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool RayAabb(Vector3 rayOrigin, Vector3 invDir, in Aabb box, out float tNear, out float tFar)
    {
        float t1 = (box.Min.X - rayOrigin.X) * invDir.X;
        float t2 = (box.Max.X - rayOrigin.X) * invDir.X;
        float t3 = (box.Min.Y - rayOrigin.Y) * invDir.Y;
        float t4 = (box.Max.Y - rayOrigin.Y) * invDir.Y;
        float t5 = (box.Min.Z - rayOrigin.Z) * invDir.Z;
        float t6 = (box.Max.Z - rayOrigin.Z) * invDir.Z;

        float tmin = MathF.Max(MathF.Max(MathF.Min(t1, t2), MathF.Min(t3, t4)), MathF.Min(t5, t6));
        float tmax = MathF.Min(MathF.Min(MathF.Max(t1, t2), MathF.Max(t3, t4)), MathF.Max(t5, t6));

        // If tmax < 0, ray intersects AABB but entirely behind the origin.
        if (tmax < 0.0f)
        {
            tNear = tmin;
            tFar = tmax;
            return false;
        }

        // If tmin > tmax, ray does not intersect AABB.
        if (tmin > tmax)
        {
            tNear = tmin;
            tFar = tmax;
            return false;
        }

        tNear = tmin;
        tFar = tmax;
        return true;
    }

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection test.
    /// Returns true if the ray hits the triangle, with <paramref name="t"/> set to the
    /// hit distance and <paramref name="normal"/> set to the triangle face normal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool RayTriangle(
        Vector3 rayOrigin, Vector3 rayDir,
        Vector3 v0, Vector3 v1, Vector3 v2,
        out float t, out Vector3 normal)
    {
        t = 0;
        normal = default;

        var e1 = v1 - v0;
        var e2 = v2 - v0;

        var h = Vector3.Cross(rayDir, e2);
        float a = Vector3.Dot(e1, h);

        if (a > -1e-5f && a < 1e-5f)
            return false;

        float f = 1.0f / a;
        var s = rayOrigin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        var q = Vector3.Cross(s, e1);
        float v = f * Vector3.Dot(rayDir, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        t = f * Vector3.Dot(e2, q);

        if (t > 1e-5f)
        {
            normal = ComputeTriangleNormal(v0, v1, v2);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes the unit normal of the triangle defined by three vertices.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ComputeTriangleNormal(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        var u = p2 - p1;
        var v = p3 - p1;
        return Vector3.Normalize(Vector3.Cross(u, v));
    }
}
