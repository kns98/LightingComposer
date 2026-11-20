// File: Rendering/Compute/TexturePixelUploader.cs
// Purpose: Uploads packed texture pixels into the GPU pixel buffer.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class TexturePixelUploader
{
    private readonly GraphicsDevice gd;
    private readonly ComputeStageLog log;

    public TexturePixelUploader(GraphicsDevice gd, ComputeStageLog log)
    {
        this.gd = gd;
        this.log = log;
    }

    /// <summary>Uploads texture pixels to the GPU buffer.</summary>
    public void Upload(DeviceBuffer destination, ComputeTextureBuild textures, CancellationToken token)
    {
        if (textures.Uploads.Count == 0)
        {
            gd.UpdateBuffer(destination, 0, new[] { 0xffffffffu });
            return;
        }

        foreach (ComputeTextureUpload upload in textures.Uploads)
        {
            log.ThrowIfCancellationRequested(token, "upload cached Vulkan texture pixels");
            uint[] pixels = upload.Texture.CopyPackedRgba32Pixels();
            int expectedPixels = checked(Math.Max(1, upload.Texture.Width) * Math.Max(1, upload.Texture.Height));
            if (pixels.Length != expectedPixels)
                throw new InvalidOperationException($"Texture '{upload.Texture.Name}' returned {pixels.Length} pixels; expected {expectedPixels}.");
            gd.UpdateBuffer(destination, checked((uint)((long)upload.PixelOffset * sizeof(uint))), pixels);
            pixels = Array.Empty<uint>();
        }
    }
}
