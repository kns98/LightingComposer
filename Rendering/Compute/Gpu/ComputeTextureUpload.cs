// File: Rendering/Compute/Gpu/ComputeTextureUpload.cs
// Purpose: One texture's destination offset inside the packed pixel buffer.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class ComputeTextureUpload
{
    public required TextureMap Texture { get; init; }
    public required int PixelOffset { get; init; }
}
