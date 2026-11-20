// File: Rendering/Compute/Gpu/BvhBuildResult.cs
// Purpose: Reordered triangle indices and CPU nodes from the BVH builder.

namespace LightingShowcase.Rendering;

internal sealed class BvhBuildResult
{
    public required int[] TriangleIndices { get; set; }
    public required CpuBvhNode[] Nodes { get; set; }
}
