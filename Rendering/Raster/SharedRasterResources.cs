// File: Rendering/Raster/SharedRasterResources.cs
// Purpose: Shared raster layout, shaders, and graphics pipelines.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class SharedRasterResources : IDisposable
{
    public required ResourceLayout Layout { get; init; }
    public required Shader[] Shaders { get; init; }
    public required Pipeline OpaquePipeline { get; init; }
    public required Pipeline TransparentPipeline { get; init; }
    public required Pipeline DepthPrepassPipeline { get; init; }
    public required Pipeline WireframePipeline { get; init; }
    public required Pipeline WireframeDashedPipeline { get; init; }

    /// <summary>Disposes the shared raster pipelines, shaders, and resource layout.</summary>
    public void Dispose()
    {
        GpuDisposal.Dispose(OpaquePipeline, TransparentPipeline, DepthPrepassPipeline, WireframePipeline, WireframeDashedPipeline);
        if (Shaders != null)
        {
            foreach (Shader shader in Shaders)
                GpuDisposal.Dispose(shader);
        }
        GpuDisposal.Dispose(Layout);
    }
}
