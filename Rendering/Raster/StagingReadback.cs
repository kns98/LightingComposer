// File: Rendering/Raster/StagingReadback.cs
// Purpose: Copies staging texture pixels into an opaque render image.

using Veldrid;

namespace LightingShowcase.Rendering;

internal static class StagingReadback
{
    /// <summary>Reads staging pixels into a render image, forcing alpha to opaque.</summary>
    public static unsafe RenderImage Read(GraphicsDevice gd, Texture stagingTexture, int width, int height)
    {
        uint[] pixels = new uint[checked(width * height)];
        MappedResource mapped = gd.Map(stagingTexture, MapMode.Read);
        try
        {
            fixed (uint* destinationBase = pixels)
            {
                nuint rowBytes = checked((nuint)(width * sizeof(uint)));
                for (int y = 0; y < height; y++)
                {
                    byte* source = (byte*)mapped.Data + checked((nint)(y * mapped.RowPitch));
                    uint* destination = destinationBase + y * width;
                    Buffer.MemoryCopy(source, destination, rowBytes, rowBytes);
                    for (int x = 0; x < width; x++)
                        destination[x] |= 0xff000000u;
                }
            }
        }
        finally
        {
            gd.Unmap(stagingTexture);
        }
        return new RenderImage(width, height, pixels);
    }
}
