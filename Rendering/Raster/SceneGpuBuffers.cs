// File: Rendering/Raster/SceneGpuBuffers.cs
// Purpose: Prepared scene vertex, uniform, and storage buffers with joint disposal.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class SceneGpuBuffers : IDisposable
{
    public required DeviceBuffer Opaque { get; init; }
    public required DeviceBuffer Transparent { get; init; }
    public required DeviceBuffer Camera { get; init; }
    public required DeviceBuffer IdentityTransform { get; init; }
    public required DeviceBuffer PreviewTransform { get; init; }
    public required DeviceBuffer Light { get; init; }
    public required DeviceBuffer Material { get; init; }

    public void Dispose()
    {
        GpuDisposal.Dispose(Material, Light, PreviewTransform, IdentityTransform, Camera, Transparent, Opaque);
    }
}
