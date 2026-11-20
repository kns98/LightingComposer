// File: Rendering/Compute/SharedComputeResources.cs
// Purpose: Shared compute layout, shader, and pipeline.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class SharedComputeResources : IDisposable
{
    public required ResourceLayout Layout { get; init; }
    public required Shader Shader { get; init; }
    public required Pipeline Pipeline { get; init; }

    /// <summary>Disposes the shared compute pipeline resources.</summary>
    public void Dispose()
    {
        GpuDisposal.Dispose(Pipeline, Shader, Layout);
    }
}
