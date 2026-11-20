// File: Rendering/Raster/TextureAtlasBuilder.cs
// Purpose: Packs scene textures into a GPU texture array and computes placements.

using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class TextureAtlasBuilder
{
    private const Veldrid.PixelFormat AtlasFormat = Veldrid.PixelFormat.R8_G8_B8_A8_UNorm;

    private readonly RasterStageLog log;

    public TextureAtlasBuilder(RasterStageLog log)
    {
        this.log = log;
    }

    /// <summary>Packs scene textures into a GPU texture array and computes their placements.</summary>
    public TextureBuildResult Build(GraphicsDevice gd, Scene scene, bool gpuTextureSamplingRequested)
    {
        if (!gpuTextureSamplingRequested)
            return TextureBuildResult.Baked(null);

        List<TextureMap> sorted = new TextureCollector().Collect(scene);
        if (sorted.Count == 0)
        {
            return new TextureBuildResult
            {
                TexturePlacements = new Dictionary<TextureMap, RasterTexturePlacement>(),
                Uploads = Array.Empty<RasterTextureUpload>(),
                Width = 1,
                Height = 1,
                TextureCount = 0,
                PageCount = 1,
                UsesGpuTextureSampling = true
            };
        }

        if (!gd.GetPixelFormatSupport(AtlasFormat, TextureType.Texture2D, TextureUsage.Sampled, out PixelFormatProperties support))
            throw new InvalidOperationException("The Vulkan device does not support sampled RGBA8 texture arrays.");

        int maxPageWidth = checked((int)Math.Min((uint)int.MaxValue, support.MaxWidth));
        int maxPageHeight = checked((int)Math.Min((uint)int.MaxValue, support.MaxHeight));
        int maxPageCount = checked((int)Math.Min((uint)int.MaxValue, support.MaxArrayLayers));
        if (maxPageWidth <= 0 || maxPageHeight <= 0 || maxPageCount <= 0)
            throw new InvalidOperationException("The Vulkan device reported invalid sampled-texture array limits.");

        AtlasSizeSelection selection = new AtlasSizeSelector(sorted, maxPageWidth, maxPageHeight, maxPageCount).Select();

        Dictionary<TextureMap, RasterTexturePlacement> texturePlacements = new(selection.Uploads.Count);
        foreach (RasterTextureUpload upload in selection.Uploads)
        {
            TextureMap texture = upload.Texture;
            float offsetX = (upload.X + 0.5f) / selection.Width;
            float offsetY = (upload.Y + 0.5f) / selection.Height;
            float scaleX = texture.Width <= 1 ? 0.0f : (texture.Width - 1.0f) / selection.Width;
            float scaleY = texture.Height <= 1 ? 0.0f : (texture.Height - 1.0f) / selection.Height;
            texturePlacements[texture] = new RasterTexturePlacement(offsetX, offsetY, scaleX, scaleY, upload.Page, texture);
        }

        log.Stage(
            $"Vulkan raster texture pages: {sorted.Count} textures -> " +
            $"{selection.PageCount} layer(s) of {selection.Width}x{selection.Height}; " +
            $"allocated={selection.AllocatedPixels * 4.0 / (1024.0 * 1024.0):F1} MB");

        return new TextureBuildResult
        {
            TexturePlacements = texturePlacements,
            Uploads = selection.Uploads,
            Width = selection.Width,
            Height = selection.Height,
            TextureCount = texturePlacements.Count,
            PageCount = selection.PageCount,
            UsesGpuTextureSampling = true
        };
    }
}
