// File: Rendering/Compute/Gpu/ComputeTextureBuild.cs
// Purpose: Packed texture ids, uploads, and metadata from the texture builder.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class ComputeTextureBuild
{
    public required Dictionary<TextureMap, int> TextureIds { get; init; }
    public required IReadOnlyList<ComputeTextureUpload> Uploads { get; init; }
    public required GpuTextureInfo[] Infos { get; set; }
    public required long PixelCount { get; init; }
    public required int TextureCount { get; init; }
}
