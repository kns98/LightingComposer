// File: Rendering/Compute/ComputeSceneBuffers.cs
// Purpose: Prepared compute scene GPU buffers with joint disposal.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class ComputeSceneBuffers : IDisposable
{
    public required DeviceBuffer Triangles { get; init; }
    public required DeviceBuffer BvhNodes { get; init; }
    public required DeviceBuffer Lights { get; init; }
    public required DeviceBuffer TexturePixels { get; init; }
    public required DeviceBuffer TextureInfos { get; init; }

    public void Dispose()
    {
        GpuDisposal.Dispose(TextureInfos, TexturePixels, Lights, BvhNodes, Triangles);
    }
}
