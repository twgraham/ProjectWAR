using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Core.Spatial.Math;

namespace Core.Spatial.KdTree;

/// <summary>
/// A KD-tree built over triangle mesh geometry for accelerating ray intersection queries.
/// Uses a flat array of <see cref="KdTreeNode"/> structs for cache-friendly traversal.
/// </summary>
/// <remarks>
/// The tree stores triangle indices, vertex positions, and per-triangle visibility flags.
/// Triangle visibility can be toggled at runtime (e.g. for opening/closing doors)
/// without rebuilding the tree.
/// </remarks>
public sealed class KdTreeAccel
{
    private readonly KdTreeNode[] _nodes;
    private readonly int[] _triangleIndices;
    private readonly Vector3[] _vertices;

    /// <summary>
    /// Per-triangle data stored as (vertexIndex0, vertexIndex1, vertexIndex2) packed into a Vector3.
    /// The integer vertex indices are stored as float components for direct lookup.
    /// </summary>
    private readonly Vector3[] _triangles;

    /// <summary>Packed fixture/surface IDs per triangle (surfaceType &lt;&lt; 24 | fixtureId).</summary>
    private readonly int[] _triangleIds;

    private readonly bool[] _visible;

    /// <summary>Index of the root node in <see cref="_nodes"/>.</summary>
    public int RootIndex { get; }

    /// <summary>Number of nodes in the tree.</summary>
    public int NodeCount => _nodes.Length;

    /// <summary>Number of triangles in the mesh.</summary>
    public int TriangleCount => _triangles.Length;

    internal KdTreeAccel(KdTreeNode[] nodes, int rootIndex, int[] triangleIndices,
                        Vector3[] triangles, Vector3[] vertices, int[] triangleIds, bool[] visible)
    {
        _nodes = nodes;
        RootIndex = rootIndex;
        _triangleIndices = triangleIndices;
        _triangles = triangles;
        _vertices = vertices;
        _triangleIds = triangleIds;
        _visible = visible;
    }

    /// <summary>
    /// Sets the visibility of a triangle by its mesh-local index.
    /// Invisible triangles are skipped during intersection tests.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTriangleVisible(int triangleIndex, bool visible)
    {
        _visible[triangleIndex] = visible;
    }

    /// <summary>
    /// Gets the visibility of a triangle by its mesh-local index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetTriangleVisible(int triangleIndex)
    {
        return _visible[triangleIndex];
    }

    /// <summary>
    /// Casts a ray against the tree and returns the packed triangle ID of the closest hit,
    /// or 0 if nothing was hit.
    /// </summary>
    /// <param name="rayOrigin">Ray origin.</param>
    /// <param name="rayDir">Normalized ray direction.</param>
    /// <param name="t">Distance along the ray to the hit point (output).</param>
    /// <param name="hitPoint">World-space hit point (output).</param>
    /// <param name="normal">Face normal at the hit point (output).</param>
    /// <returns>The packed triangle ID (surfaceType &lt;&lt; 24 | fixtureId), or 0 if no hit.</returns>
    public int Intersect(Vector3 rayOrigin, Vector3 rayDir, out float t, out Vector3 hitPoint, out Vector3 normal)
    {
        t = float.PositiveInfinity;
        hitPoint = default;
        normal = default;

        // Pre-compute once — avoids a division-per-node during traversal.
        var invDir = Vector3.One / rayDir;

        int hit = Intersect(RootIndex, rayOrigin, rayDir, invDir, ref t, ref normal);

        if (hit != 0)
            hitPoint = rayOrigin + t * rayDir;

        return hit;
    }

    private int Intersect(int nodeIndex, Vector3 rayOrigin, Vector3 rayDir, Vector3 invDir,
                          ref float t, ref Vector3 normal)
    {
        ref readonly var node = ref _nodes[nodeIndex];

        if (!RayIntersection.RayAabb(rayOrigin, invDir, in node.Bounds, out _, out _))
            return 0;

        int hit = 0;

        if (node.IsLeaf)
        {
            // Test all triangles in this leaf.
            int end = node.TriangleStart + node.TriangleCount;
            for (int i = node.TriangleStart; i < end; i++)
            {
                int triIdx = _triangleIndices[i];

                if (!_visible[triIdx])
                    continue;

                var tri = _triangles[triIdx];
                var v0 = _vertices[(int)tri.X];
                var v1 = _vertices[(int)tri.Y];
                var v2 = _vertices[(int)tri.Z];

                if (RayIntersection.RayTriangle(rayOrigin, rayDir, v0, v1, v2, out float tmpT, out var tmpNormal))
                {
                    if (tmpT < t)
                    {
                        hit = _triangleIds[triIdx];
                        t = tmpT;
                        normal = tmpNormal;
                    }
                }
            }
        }
        else
        {
            int hitLeft = 0, hitRight = 0;

            if (node.LeftChild >= 0)
                hitLeft = Intersect(node.LeftChild, rayOrigin, rayDir, invDir, ref t, ref normal);

            if (node.RightChild >= 0)
                hitRight = Intersect(node.RightChild, rayOrigin, rayDir, invDir, ref t, ref normal);

            // Return whichever child produced a hit (closest t wins via the shared ref).
            if (hitLeft != 0)
                hit = hitLeft;
            if (hitRight != 0)
                hit = hitRight;
        }

        return hit;
    }

    /// <summary>
    /// Builds a KD-tree from the given mesh data.
    /// </summary>
    /// <param name="triangles">
    /// Array of Vector3 where each component is a vertex index into <paramref name="vertices"/>.
    /// </param>
    /// <param name="vertices">Position array.</param>
    /// <param name="triangleIds">Per-triangle packed ID (surfaceType &lt;&lt; 24 | fixtureId).</param>
    /// <param name="maxTrisPerLeaf">Maximum number of triangles in a leaf node before splitting stops.</param>
    public static KdTreeAccel Build(Vector3[] triangles, Vector3[] vertices, int[] triangleIds, int maxTrisPerLeaf)
    {
        var builder = new KdTreeBuilder(triangles, vertices, triangleIds, maxTrisPerLeaf);
        return builder.Build();
    }
}

/// <summary>
/// Constructs a KD-tree from mesh data using median-space splitting.
/// </summary>
file sealed class KdTreeBuilder
{
    private readonly Vector3[] _triangles;
    private readonly Vector3[] _vertices;
    private readonly int[] _triangleIds;
    private readonly int _maxTrisPerLeaf;

    private readonly List<KdTreeNode> _nodes = [];
    private readonly List<int> _sortedTriIndices = [];

    public KdTreeBuilder(Vector3[] triangles, Vector3[] vertices, int[] triangleIds, int maxTrisPerLeaf)
    {
        _triangles = triangles;
        _vertices = vertices;
        _triangleIds = triangleIds;
        _maxTrisPerLeaf = maxTrisPerLeaf;
    }

    public KdTreeAccel Build()
    {
        // Build initial index list.
        int numTris = _triangles.Length;
        var triIndices = new int[numTris];
        for (int i = 0; i < numTris; i++)
            triIndices[i] = i;

        // Compute root bounding box from all vertices.
        var bounds = Aabb.FromVertices(_vertices.AsSpan());

        int rootIndex = BuildRecursive(numTris, triIndices, bounds);

        var visible = new bool[numTris];
        Array.Fill(visible, true);

        return new KdTreeAccel(
            _nodes.ToArray(),
            rootIndex,
            _sortedTriIndices.ToArray(),
            _triangles,
            _vertices,
            _triangleIds,
            visible);
    }

    private int BuildRecursive(int numTris, int[] triIndices, Aabb bounds)
    {
        int nodeIndex = _nodes.Count;
        _nodes.Add(default); // Reserve slot.

        // Leaf node.
        if (numTris <= _maxTrisPerLeaf)
        {
            int triStart = _sortedTriIndices.Count;
            for (int i = 0; i < numTris; i++)
                _sortedTriIndices.Add(triIndices[i]);

            _nodes[nodeIndex] = new KdTreeNode
            {
                Bounds = bounds,
                IsLeaf = true,
                TriangleStart = triStart,
                TriangleCount = numTris,
                LeftChild = -1,
                RightChild = -1,
            };

            return nodeIndex;
        }

        // Choose split axis = longest side of bounding box.
        var axis = bounds.GetLongestAxis();

        // Median value along that axis.
        float medianVal = axis switch
        {
            SplitAxis.X => bounds.Min.X + (bounds.Max.X - bounds.Min.X) * 0.5f,
            SplitAxis.Y => bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * 0.5f,
            _ => bounds.Min.Z + (bounds.Max.Z - bounds.Min.Z) * 0.5f,
        };

        // Compute child bounding boxes.
        var leftBounds = bounds;
        var rightBounds = bounds;

        switch (axis)
        {
            case SplitAxis.X:
                leftBounds.Max = new Vector3(medianVal, bounds.Max.Y, bounds.Max.Z);
                rightBounds.Min = new Vector3(medianVal, bounds.Min.Y, bounds.Min.Z);
                break;
            case SplitAxis.Y:
                leftBounds.Max = new Vector3(bounds.Max.X, medianVal, bounds.Max.Z);
                rightBounds.Min = new Vector3(bounds.Min.X, medianVal, bounds.Min.Z);
                break;
            default:
                leftBounds.Max = new Vector3(bounds.Max.X, bounds.Max.Y, medianVal);
                rightBounds.Min = new Vector3(bounds.Min.X, bounds.Min.Y, medianVal);
                break;
        }

        // Partition triangles into left/right using pooled arrays to reduce GC pressure.
        var pool = ArrayPool<int>.Shared;
        var leftBuf = pool.Rent(numTris);
        var rightBuf = pool.Rent(numTris);
        int leftCount = 0;
        int rightCount = 0;

        for (int i = 0; i < numTris; i++)
        {
            int idx = triIndices[i];
            GetTriBounds(idx, axis, out float minVal, out float maxVal);

            if (minVal < medianVal)
                leftBuf[leftCount++] = idx;
            if (maxVal >= medianVal)
                rightBuf[rightCount++] = idx;
        }

        // Keep pool buffers alive through each recursive call — no ToArray() copy needed.
        int leftChild = BuildRecursive(leftCount, leftBuf, leftBounds);
        pool.Return(leftBuf);
        int rightChild = BuildRecursive(rightCount, rightBuf, rightBounds);
        pool.Return(rightBuf);

        _nodes[nodeIndex] = new KdTreeNode
        {
            Bounds = bounds,
            SplitAxis = axis,
            SplitValue = medianVal,
            IsLeaf = false,
            LeftChild = leftChild,
            RightChild = rightChild,
            TriangleStart = 0,
            TriangleCount = 0,
        };

        return nodeIndex;
    }

    /// <summary>
    /// Returns both the min and max of triangle <paramref name="triIndex"/> along
    /// <paramref name="axis"/> in a single method, halving the vertex lookups
    /// compared to calling separate min/max helpers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GetTriBounds(int triIndex, SplitAxis axis, out float min, out float max)
    {
        var tri = _triangles[triIndex];
        float a, b, c;
        switch (axis)
        {
            case SplitAxis.X:
                a = _vertices[(int)tri.X].X;
                b = _vertices[(int)tri.Y].X;
                c = _vertices[(int)tri.Z].X;
                break;
            case SplitAxis.Y:
                a = _vertices[(int)tri.X].Y;
                b = _vertices[(int)tri.Y].Y;
                c = _vertices[(int)tri.Z].Y;
                break;
            default:
                a = _vertices[(int)tri.X].Z;
                b = _vertices[(int)tri.Y].Z;
                c = _vertices[(int)tri.Z].Z;
                break;
        }
        min = MathF.Min(a, MathF.Min(b, c));
        max = MathF.Max(a, MathF.Max(b, c));
    }
}
