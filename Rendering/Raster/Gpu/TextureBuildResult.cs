// File: Rendering/Raster/Gpu/TextureBuildResult.cs
// Purpose: Packed atlas dimensions, placements, and uploads from the atlas builder.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class TextureBuildResult
{
    public required IReadOnlyDictionary<TextureMap, RasterTexturePlacement> TexturePlacements { get; init; }
    public required IReadOnlyList<RasterTextureUpload> Uploads { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int TextureCount { get; init; }
    public required int PageCount { get; init; }
    public required bool UsesGpuTextureSampling { get; init; }
    public string? FallbackReason { get; init; }

    /// <summary>Creates an empty texture result that falls back to base-color sampling.</summary>
    internal static TextureBuildResult Baked(string? reason) => new()
    {
        TexturePlacements = new Dictionary<TextureMap, RasterTexturePlacement>(),
        Uploads = Array.Empty<RasterTextureUpload>(),
        Width = 1,
        Height = 1,
        TextureCount = 0,
        PageCount = 1,
        UsesGpuTextureSampling = false,
        FallbackReason = reason
    };
}
