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
        float boundsPad = BvhBounds.Padding(scene);
        Vector3 pad = new(boundsPad);

        // Store only the final triangle order. Triangle bounds and centroids are
        // already present in the scene, so duplicating them in another large
        // per-triangle structure only increases peak memory.
        int[] triangleIndices = new int[triangleCount];
        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            triangleIndices[triangleIndex] = triangleIndex;

        log.Stage($"GPU BVH conservative triangle bounds pad={boundsPad:G6}");

        CpuBvhNode[] nodes = new CpuBvhNode[CountNodes(triangleCount)];
        TriangleCentroidComparer comparer = new(scene.Triangles);
        BvhSplitter splitter = new(scene.Triangles, triangleIndices, pad, comparer, nodes);
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
            int leftCount = count / 2;
            int result = checked(1 + Count(leftCount) + Count(count - leftCount));
            cache[count] = result;
            return result;
        }
    }
}
