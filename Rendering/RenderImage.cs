/*
 * The code here converts renderer-neutral scene/camera data into pixels or backend-ready state. Dimensions, cache
 * identity, data packing, and deterministic conversion are treated as part of the rendering contract so
 * interactive UI code does not need to know backend details.
 *
 * `RenderImage` owns a pixel buffer plus its dimensions/format semantics so renderers and writers agree on how
 * image memory is laid out.
 *
 * The `RenderImage` constructor captures `width`, `height`, `packedRgba32`. Those are the dependencies/initial
 * values the instance needs for its lifetime, so callbacks and later operations use the same
 * objects/configuration rather than looking them up globally.
 *
 * `SavePng` serializes png from current internal state, making persistence a snapshot operation rather than
 * allowing the serializer to walk concurrently mutating editor objects.
 */
namespace LightingShowcase.Rendering;

/// <summary>Cross-platform RGBA render result. Each uint stores R, G, B, A in low-to-high byte order.</summary>
public sealed class RenderImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] PackedRgba32 { get; }

    public RenderImage(int width, int height, uint[] packedRgba32)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (packedRgba32 == null) throw new ArgumentNullException(nameof(packedRgba32));
        if (packedRgba32.Length != checked(width * height))
            throw new ArgumentException("Pixel buffer length does not match image dimensions.", nameof(packedRgba32));

        Width = width;
        Height = height;
        PackedRgba32 = packedRgba32;
    }

    public void SavePng(string path) => PngWriter.Write(path, this);
}
