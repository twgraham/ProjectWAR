using System.Numerics;
using System.Runtime.CompilerServices;
using Core.Spatial.Math;

namespace Core.Spatial.KdTree;

/// <summary>
/// A single node in a flat-array KD-tree.
/// Internal nodes split space along an axis; leaf nodes reference a range of triangle indices.
/// </summary>
public struct KdTreeNode
{
    /// <summary>Axis-aligned bounding box of this node.</summary>
    public Aabb Bounds;

    /// <summary>Split axis (only meaningful for internal nodes).</summary>
    public SplitAxis SplitAxis;

    /// <summary>Split plane position along <see cref="SplitAxis"/> (only meaningful for internal nodes).</summary>
    public float SplitValue;

    /// <summary>Index of the left child in the node array, or -1 if this is a leaf.</summary>
    public int LeftChild;

    /// <summary>Index of the right child in the node array, or -1 if this is a leaf.</summary>
    public int RightChild;

    /// <summary>True if this node is a leaf that contains triangles.</summary>
    public bool IsLeaf;

    /// <summary>Start index into the tree's sorted triangle index array (leaf only).</summary>
    public int TriangleStart;

    /// <summary>Number of triangles in this leaf node (leaf only).</summary>
    public int TriangleCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsPointToLeft(Vector3 point)
    {
        return SplitAxis switch
        {
            Math.SplitAxis.X => point.X < SplitValue,
            Math.SplitAxis.Y => point.Y < SplitValue,
            _ => point.Z < SplitValue,
        };
    }
}
