// File: Rendering/Raster/RasterTargets.cs
// Purpose: Sized color, depth, staging, and sync resources for one resolution.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class RasterTargets : IDisposable
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Texture ColorTexture { get; init; }
    public required Texture DepthTexture { get; init; }
    public required Texture StagingTexture { get; init; }
    public required Framebuffer Framebuffer { get; init; }
    public required CommandList CommandList { get; init; }
    public required Fence Fence { get; init; }
    public long LastUseSerial { get; set; }

    /// <summary>Disposes the cached render targets, command list, and fence.</summary>
    public void Dispose()
    {
        GpuDisposal.Dispose(Fence, CommandList, Framebuffer, StagingTexture, DepthTexture, ColorTexture);
    }
}
