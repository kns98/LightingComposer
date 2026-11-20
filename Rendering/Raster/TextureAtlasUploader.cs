// File: Rendering/Raster/TextureAtlasUploader.cs
// Purpose: Uploads atlas textures plus linear-filtering border texels to the GPU.

using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class TextureAtlasUploader
{
    private readonly GraphicsDevice gd;
    private readonly RasterStageLog log;

    public TextureAtlasUploader(GraphicsDevice gd, RasterStageLog log)
    {
        this.gd = gd;
        this.log = log;
    }

    /// <summary>Uploads each texture and its filtering border pixels into the GPU atlas.</summary>
    public void Upload(SceneAtlas atlas, TextureBuildResult textures, CancellationToken token)
    {
        if (textures.Uploads.Count == 0)
        {
            gd.UpdateTexture(atlas.Texture, new[] { 0xffffffffu }, 0, 0, 0, 1, 1, 1, 0, 0);
            return;
        }

        foreach (RasterTextureUpload upload in textures.Uploads)
        {
            log.ThrowIfCancellationRequested(token, "upload Vulkan raster texture atlas");
            TextureMap texture = upload.Texture;
            int width = Math.Max(1, texture.Width);
            int height = Math.Max(1, texture.Height);
            uint[] source = texture.CopyPackedRgba32Pixels();
            int expectedPixels = checked(width * height);
            if (source.Length != expectedPixels)
                throw new InvalidOperationException($"Texture '{texture.Name}' returned {source.Length} pixels; expected {expectedPixels}.");

            gd.UpdateTexture(atlas.Texture, source, (uint)upload.X, (uint)upload.Y, 0, (uint)width, (uint)height, 1, 0, (uint)upload.Page);

            // Upload only the one-pixel borders needed by linear filtering.
            // This avoids allocating a second padded copy of the whole texture.
            uint[] top = new uint[width];
            uint[] bottom = new uint[width];
            Array.Copy(source, 0, top, 0, width);
            Array.Copy(source, (height - 1) * width, bottom, 0, width);
            gd.UpdateTexture(atlas.Texture, top, (uint)upload.X, (uint)(upload.Y - 1), 0, (uint)width, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas.Texture, bottom, (uint)upload.X, (uint)(upload.Y + height), 0, (uint)width, 1, 1, 0, (uint)upload.Page);

            uint[] left = new uint[height];
            uint[] right = new uint[height];
            for (int y = 0; y < height; y++)
            {
                left[y] = source[y * width];
                right[y] = source[y * width + width - 1];
            }
            gd.UpdateTexture(atlas.Texture, left, (uint)(upload.X - 1), (uint)upload.Y, 0, 1, (uint)height, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas.Texture, right, (uint)(upload.X + width), (uint)upload.Y, 0, 1, (uint)height, 1, 0, (uint)upload.Page);

            gd.UpdateTexture(atlas.Texture, new[] { source[0] }, (uint)(upload.X - 1), (uint)(upload.Y - 1), 0, 1, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas.Texture, new[] { source[width - 1] }, (uint)(upload.X + width), (uint)(upload.Y - 1), 0, 1, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas.Texture, new[] { source[(height - 1) * width] }, (uint)(upload.X - 1), (uint)(upload.Y + height), 0, 1, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas.Texture, new[] { source[source.Length - 1] }, (uint)(upload.X + width), (uint)(upload.Y + height), 0, 1, 1, 1, 0, (uint)upload.Page);
            source = Array.Empty<uint>();
        }
    }
}
