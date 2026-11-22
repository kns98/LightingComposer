// File: Rendering/Raster/RasterShadowResources.cs
// Purpose: Device-global shadow maps, cameras, and depth pipeline.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class RasterShadowResources : IDisposable
{
    public required Texture ShadowMaps { get; init; }
    public required Texture ShadowDepth { get; init; }
    public required Sampler ShadowSampler { get; init; }
    public required DeviceBuffer ShadowMatrices { get; init; }
    public required Pipeline DepthPipeline { get; init; }
    public required ResourceLayout DepthLayout { get; init; }
    public required Shader DepthVertex { get; init; }
    public required Shader DepthFragment { get; init; }
    public required Framebuffer[] LayerFramebuffers { get; init; }
    public required DeviceBuffer[] LayerUniforms { get; init; }
    public required ResourceSet[] LayerSets { get; init; }

    /// <summary>Disposes the shadow maps, pipeline, shaders, and per-layer resources.</summary>
    public void Dispose()
    {
        GpuDisposal.Dispose(DepthPipeline, DepthVertex, DepthFragment);
        foreach (ResourceSet set in LayerSets)
            GpuDisposal.Dispose(set);
        foreach (DeviceBuffer buffer in LayerUniforms)
            GpuDisposal.Dispose(buffer);
        foreach (Framebuffer framebuffer in LayerFramebuffers)
            GpuDisposal.Dispose(framebuffer);
        GpuDisposal.Dispose(ShadowMatrices, ShadowSampler, ShadowDepth, ShadowMaps);
        GpuDisposal.Dispose(DepthLayout);
    }
}
