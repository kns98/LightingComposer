using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using SkiaSharp;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class TextureMapTests
{
    private static byte[] EncodeWebP()
    {
        using SKBitmap bitmap = new(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                SKColor color = (x < 2, y < 2) switch
                {
                    (true, true) => new SKColor(255, 0, 0, 255),
                    (false, true) => new SKColor(0, 255, 0, 255),
                    (true, false) => new SKColor(0, 0, 255, 128),
                    (false, false) => new SKColor(255, 255, 255, 255)
                };
                bitmap.SetPixel(x, y, color);
            }
        }
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Webp, 100);
        return data.ToArray();
    }

    private static void AssertQuadrants(TextureMap texture)
    {
        Assert.Equal(4, texture.Width);
        Assert.Equal(4, texture.Height);

        Vec3 red = texture.Sample(0.0, 0.0);
        Assert.Equal(1.0, red.X, precision: 1);
        Assert.Equal(0.0, red.Y, precision: 1);
        Assert.Equal(0.0, red.Z, precision: 1);
        Assert.Equal(1.0, texture.SampleAlpha(0.0, 0.0), precision: 1);

        Vec3 green = texture.Sample(1.0, 0.0);
        Assert.Equal(0.0, green.X, precision: 1);
        Assert.Equal(1.0, green.Y, precision: 1);
        Assert.Equal(1.0, texture.SampleAlpha(1.0, 0.0), precision: 1);

        Vec3 blue = texture.Sample(0.0, 1.0);
        Assert.Equal(0.0, blue.X, precision: 1);
        Assert.Equal(0.0, blue.Y, precision: 1);
        Assert.Equal(1.0, blue.Z, precision: 1);
        Assert.Equal(0.5, texture.SampleAlpha(0.0, 1.0), precision: 1);

        Vec3 white = texture.Sample(1.0, 1.0);
        Assert.Equal(1.0, white.X, precision: 1);
        Assert.Equal(1.0, white.Y, precision: 1);
        Assert.Equal(1.0, white.Z, precision: 1);
    }

    [Fact]
    public void FromBytes_DecodesWebP()
    {
        byte[] webp = EncodeWebP();

        TextureMap texture = TextureMap.FromBytes("quad.webp", webp);

        AssertQuadrants(texture);
    }

    [Fact]
    public void FromFile_DecodesWebP()
    {
        string directory = Directory.CreateTempSubdirectory("webp-file-test").FullName;
        try
        {
            string path = Path.Combine(directory, "quad.webp");
            File.WriteAllBytes(path, EncodeWebP());

            TextureMap texture = TextureMap.FromFile(path);

            AssertQuadrants(texture);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FromBytes_CorruptWebP_Throws()
    {
        byte[] corrupt = new byte[128];
        corrupt[0] = (byte)'R';
        corrupt[1] = (byte)'I';
        corrupt[2] = (byte)'F';
        corrupt[3] = (byte)'F';
        corrupt[8] = (byte)'W';
        corrupt[9] = (byte)'E';
        corrupt[10] = (byte)'B';
        corrupt[11] = (byte)'P';

        Assert.Throws<InvalidDataException>(() => TextureMap.FromBytes("bad.webp", corrupt));
    }
}
