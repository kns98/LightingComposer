// File: Rendering/Raster/SceneDescriptorSets.cs
// Purpose: Identity and preview descriptor sets with joint disposal.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class SceneDescriptorSets : IDisposable
{
    public required ResourceSet Identity { get; init; }
    public required ResourceSet Preview { get; init; }

    public void Dispose()
    {
        GpuDisposal.Dispose(Preview, Identity);
    }
}
