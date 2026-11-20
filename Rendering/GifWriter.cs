// -----------------------------------------------------------------------------
// File: Rendering/GifWriter.cs
// Purpose: Minimal animated GIF writer.
//
// Encodes render frames with one shared median-cut palette, GIF LZW image
// data, per-frame delays, and a loop-forever extension.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Rendering;

/// <summary>Minimal animated GIF writer for render frames.</summary>
public static class GifWriter
{
    /// <summary>Encodes render frames into animated GIF bytes with the given frame delay.</summary>
    public static byte[] Encode(IReadOnlyList<RenderImage> frames, int delayCentiseconds)
    {
        if (frames == null || frames.Count == 0)
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        int width = frames[0].Width;
        int height = frames[0].Height;
        if (width <= 0 || height <= 0 || width > 65535 || height > 65535)
            throw new ArgumentOutOfRangeException(nameof(frames), "GIF dimensions must be between 1 and 65535.");
        if (frames.Any(frame => frame.Width != width || frame.Height != height))
            throw new ArgumentException("All GIF frames must share the same dimensions.", nameof(frames));

        int pixelsPerFrame = checked(width * height);
        byte[] red = new byte[checked(pixelsPerFrame * frames.Count)];
        byte[] green = new byte[red.Length];
        byte[] blue = new byte[red.Length];
        for (int i = 0; i < frames.Count; i++)
        {
            uint[] packed = frames[i].PackedRgba32;
            for (int p = 0; p < pixelsPerFrame; p++)
            {
                red[i * pixelsPerFrame + p] = (byte)(packed[p] & 0xFF);
                green[i * pixelsPerFrame + p] = (byte)((packed[p] >> 8) & 0xFF);
                blue[i * pixelsPerFrame + p] = (byte)((packed[p] >> 16) & 0xFF);
            }
        }

        (byte[] palette, byte[] indices) = MedianCutQuantizer.Quantize(red, green, blue);

        using MemoryStream output = new();
        output.Write("GIF89a"u8);
        WriteU16(output, (ushort)width);
        WriteU16(output, (ushort)height);
        output.WriteByte(0xF7);
        output.WriteByte(0x00);
        output.WriteByte(0x00);
        output.Write(palette);

        output.Write([0x21, 0xFF, 0x0B]);
        output.Write("NETSCAPE2.0"u8);
        output.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        int delay = Math.Clamp(delayCentiseconds, 0, 65535);
        for (int i = 0; i < frames.Count; i++)
        {
            output.Write([0x21, 0xF9, 0x04, 0x04]);
            WriteU16(output, (ushort)delay);
            output.Write([0x00, 0x00]);
            output.WriteByte(0x2C);
            WriteU16(output, 0);
            WriteU16(output, 0);
            WriteU16(output, (ushort)width);
            WriteU16(output, (ushort)height);
            output.WriteByte(0x00);

            byte[] frameIndices = new byte[pixelsPerFrame];
            Array.Copy(indices, i * pixelsPerFrame, frameIndices, 0, pixelsPerFrame);
            byte[] compressed = GifLzwCompressor.Compress(frameIndices, minimumCodeSize: 8);
            output.WriteByte(8);
            for (int offset = 0; offset < compressed.Length; offset += 255)
            {
                int block = Math.Min(255, compressed.Length - offset);
                output.WriteByte((byte)block);
                output.Write(compressed, offset, block);
            }
            output.WriteByte(0x00);
        }

        output.WriteByte(0x3B);
        return output.ToArray();
    }

    /// <summary>Writes render frames to an animated GIF file at the given path.</summary>
    public static void Write(string path, IReadOnlyList<RenderImage> frames, int delayCentiseconds)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An output path is required.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        File.WriteAllBytes(fullPath, Encode(frames, delayCentiseconds));
    }

    private static void WriteU16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value & 0xFF));
        output.WriteByte((byte)(value >> 8));
    }
}
