using LightingShowcase.Rendering;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class GifWriterTests
{
    private static RenderImage SolidFrame(int width, int height, uint packed)
    {
        uint[] pixels = new uint[width * height];
        Array.Fill(pixels, packed);
        return new RenderImage(width, height, pixels);
    }

    [Fact]
    public void Encode_SingleFrame_HasGifBoundsAndLoopExtension()
    {
        byte[] gif = GifWriter.Encode(new[] { SolidFrame(4, 3, 0xFF0000FFu) }, delayCentiseconds: 8);

        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
        Assert.Equal(0x3B, gif[^1]);
        Assert.True(FindBytes(gif, "NETSCAPE2.0"u8.ToArray()) >= 0);
    }

    [Fact]
    public void Encode_TwoFrames_HasTwoImageDescriptorsWithDelays()
    {
        byte[] gif = GifWriter.Encode(
            new[] { SolidFrame(4, 4, 0xFF0000FFu), SolidFrame(4, 4, 0xFF00FF00u) },
            delayCentiseconds: 12);

        int gceCount = 0;
        int searchFrom = 0;
        while (true)
        {
            int found = FindBytes(gif, new byte[] { 0x21, 0xF9, 0x04 }, searchFrom);
            if (found < 0)
                break;
            gceCount++;
            if (gceCount == 1)
                Assert.Equal(12, BitConverter.ToUInt16(gif, found + 4));
            searchFrom = found + 1;
        }
        Assert.Equal(2, gceCount);
    }

    [Fact]
    public void Encode_RoundTripsFirstFrameThroughDecoder()
    {
        byte[] gif = GifWriter.Encode(new[] { SolidFrame(4, 4, 0xFF0000FFu) }, delayCentiseconds: 8);

        using MemoryStream stream = new(gif);
        StbImageSharp.ImageResult decoded = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.Equal(255, decoded.Data[0]);
        Assert.Equal(0, decoded.Data[1]);
        Assert.Equal(0, decoded.Data[2]);
    }

    [Fact]
    public void Encode_NoisyImage_RoundTripsThroughDecoder()
    {
        Random random = new(12345);
        uint[] pixels = new uint[64 * 64];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = 0xFF000000u | (uint)random.Next(0, 1 << 24);
        RenderImage noisy = new(64, 64, pixels);

        byte[] gif = GifWriter.Encode(new[] { noisy }, delayCentiseconds: 8);

        using MemoryStream stream = new(gif);
        StbImageSharp.ImageResult decoded = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(64, decoded.Width);
        Assert.Equal(64, decoded.Height);
    }

    [Fact]
    public void Encode_MismatchedSizes_Throws()
    {
        Assert.Throws<ArgumentException>(() => GifWriter.Encode(
            new[] { SolidFrame(4, 4, 0xFF0000FFu), SolidFrame(8, 4, 0xFF0000FFu) }, delayCentiseconds: 8));
    }

    private static int FindBytes(byte[] haystack, byte[] needle, int start = 0)
    {
        for (int i = start; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }
}
