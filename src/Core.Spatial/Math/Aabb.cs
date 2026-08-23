using System.Numerics;
using System.Runtime.CompilerServices;

namespace Core.Spatial.Math;

/// <summary>
/// Axis-aligned bounding box represented by min/max corners.
/// </summary>
public struct Aabb
{
    public Vector3 Min;
    public Vector3 Max;

    public Aabb(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Computes a tight-fitting AABB around the given vertices.
    /// </summary>
    public static Aabb FromVertices(ReadOnlySpan<Vector3> vertices)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        for (int i = 0; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i]);
            max = Vector3.Max(max, vertices[i]);
        }

        return new Aabb(min, max);
    }

    /// <summary>
    /// Returns the longest axis of this bounding box.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly SplitAxis GetLongestAxis()
    {
        var extent = Max - Min;
        if (extent.X > extent.Y && extent.X > extent.Z) return SplitAxis.X;
        return extent.Y > extent.Z ? SplitAxis.Y : SplitAxis.Z;
    }
}

/// <summary>
/// Axis identifier used for KD-tree splitting.
/// </summary>
public enum SplitAxis : byte
{
    X = 0,
    Y = 1,
    Z = 2,
}
