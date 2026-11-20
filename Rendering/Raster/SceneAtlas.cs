// File: Rendering/Raster/SceneAtlas.cs
// Purpose: Prepared scene texture atlas and sampler with joint disposal.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class SceneAtlas : IDisposable
{
    public required Texture Texture { get; init; }
    public required Sampler Sampler { get; init; }

    public void Dispose()
    {
        GpuDisposal.Dispose(Sampler, Texture);
    }
}
