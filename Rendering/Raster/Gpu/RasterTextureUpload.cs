// File: Rendering/Raster/Gpu/RasterTextureUpload.cs
// Purpose: One texture's destination rect inside the GPU atlas.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class RasterTextureUpload
{
    public required TextureMap Texture { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Page { get; init; }
}
