// File: Rendering/Compute/Bvh/BvhSplitter.cs
// Purpose: Recursive median-split BVH construction over triangle indices.

using System.Numerics;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class BvhSplitter
{
    private const int LeafSize = 4;

    private readonly IReadOnlyList<Triangle> triangles;
    private readonly int[] triangleIndices;
    private readonly Vector3 pad;
    private readonly TriangleCentroidComparer comparer;
    private readonly CpuBvhNode[] nodes;
    private int nodeCount;

    public BvhSplitter(
        IReadOnlyList<Triangle> triangles, int[] triangleIndices, Vector3 pad,
        TriangleCentroidComparer comparer, CpuBvhNode[] nodes)
    {
        this.triangles = triangles;
        this.triangleIndices = triangleIndices;
        this.pad = pad;
        this.comparer = comparer;
        this.nodes = nodes;
    }

    public int NodeCount => nodeCount;

    /// <summary>Recursively builds one bounding volume hierarchy node.</summary>
    public int Build(int start, int count)
    {
        int nodeIndex = nodeCount++;

        if (count <= LeafSize)
        {
            BvhBounds.LeafBounds(triangles, triangleIndices, start, count, pad, out Vector3 leafMin, out Vector3 leafMax);
            nodes[nodeIndex] = new CpuBvhNode(leafMin, leafMax, start, count, 0);
            return nodeIndex;
        }

        BvhBounds.CentroidBounds(triangles, triangleIndices, start, count, out Vector3 centroidMin, out Vector3 centroidMax);
        Vector3 extent = centroidMax - centroidMin;
        comparer.Axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(triangleIndices, start, count, comparer);

        int leftCount = count / 2;
        int left = Build(start, leftCount);
        int right = Build(start + leftCount, count - leftCount);
        nodes[nodeIndex] = new CpuBvhNode(
            Vector3.Min(nodes[left].BoundsMin, nodes[right].BoundsMin),
            Vector3.Max(nodes[left].BoundsMax, nodes[right].BoundsMax),
            left, 0, right);
        return nodeIndex;
    }
}
