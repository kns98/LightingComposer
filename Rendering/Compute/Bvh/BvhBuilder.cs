// File: Rendering/Compute/Bvh/BvhBuilder.cs
// Purpose: Builds the traversal BVH triangle order and CPU nodes.

using System.Numerics;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class BvhBuilder
{
    private readonly ComputeStageLog log;

    public BvhBuilder(ComputeStageLog log)
    {
        this.log = log;
    }

    /// <summary>Builds the bounding volume hierarchy for the scene.</summary>
    public BvhBuildResult Build(Scene scene)
    {
        int triangleCount = scene.Triangles.Count;
        Vector3 pad = new(BvhBounds.Padding(scene));

        // The splitter reorders this index array in place; the uploader later
        // writes triangles to the GPU in this same traversal order.
        int[] triangleIndices = new int[triangleCount];
        for (int i = 0; i < triangleCount; i++)
            triangleIndices[i] = i;

        log.Stage($"GPU BVH conservative triangle bounds pad={pad.X:G6}");

        CpuBvhNode[] nodes = new CpuBvhNode[CountNodes(triangleCount)];
        BvhSplitter splitter = new(scene.Triangles, triangleIndices, pad, new TriangleCentroidComparer(scene.Triangles), nodes);
        splitter.Build(0, triangleIndices.Length);

        if (splitter.NodeCount != nodes.Length)
            throw new InvalidOperationException($"Internal BVH packing mismatch: nodes={splitter.NodeCount}/{nodes.Length}.");

        return new BvhBuildResult { TriangleIndices = triangleIndices, Nodes = nodes };
    }

    /// <summary>Counts the hierarchy nodes needed for a triangle count.</summary>
    private static int CountNodes(int triangleCount)
    {
        Dictionary<int, int> cache = new();
        return Count(triangleCount);

        int Count(int count)
        {
            if (count <= 4)
                return 1;
            if (cache.TryGetValue(count, out int cached))
                return cached;
            int result = checked(1 + Count(count / 2) + Count(count - count / 2));
            cache[count] = result;
            return result;
        }
    }
}
