using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class ShadowShaderTests
{
    private static Triangle MakeTriangle(Material material) =>
        new(new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4),
            new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), material);

    private static PixelSample MakeSample(Triangle tri, IReadOnlyList<PreparedLight>? lights = null) =>
        new(tri, 1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0, new Vec3(0, 0, 4), new Vec3(0, 0, -1),
            Vec3.Zero, lights ?? Array.Empty<PreparedLight>());

    private static void PrepareFull(FullPixelShader shader, Triangle tri) =>
        Assert.True(shader.Prepare(tri, new ProjectedVertex(0, 0, 4), new ProjectedVertex(0, 0, 4), new ProjectedVertex(0, 0, 4)));

    private static TextureMap SolidTexture(string name, byte r, byte g, byte b, byte a = 255) =>
        TextureMap.FromRgbaBytes(name, 1, 1, new byte[] { r, g, b, a });

    private static PreparedLight FrontDirectional()
    {
        SceneLight light = new("key", Vec3.Zero, new Vec3(1, 1, 1), 1.0,
            kind: SceneLightKind.Directional, castsShadow: false);
        return new PreparedLight(light, null, new Vec3(0, 0, -1), Vec3.Zero, 0.0, 0.0);
    }

    [Fact]
    public void Full_MaskBelowCutoff_Discards()
    {
        Triangle tri = MakeTriangle(new Material(new Vec3(1, 1, 1), alpha: 0.3, alphaMode: MaterialAlphaMode.Mask, alphaCutoff: 0.5));
        FullPixelShader shader = new();
        PrepareFull(shader, tri);

        Assert.False(shader.TryShade(MakeSample(tri), out _, out _));
    }

    [Fact]
    public void Full_MaskAboveCutoff_ShadesOpaque()
    {
        Triangle tri = MakeTriangle(new Material(new Vec3(1, 1, 1), alpha: 0.7, alphaMode: MaterialAlphaMode.Mask, alphaCutoff: 0.5));
        FullPixelShader shader = new();
        PrepareFull(shader, tri);

        Assert.True(shader.TryShade(MakeSample(tri), out _, out double alpha));
        Assert.Equal(1.0, alpha);
    }

    [Fact]
    public void Full_OpaqueIgnoresTextureAlpha()
    {
        Material material = new(new Vec3(1, 1, 1), texture: SolidTexture("zero-alpha", 255, 0, 0, 0));
        Assert.Equal(MaterialAlphaMode.Opaque, material.AlphaMode);
        Triangle tri = MakeTriangle(material);
        FullPixelShader shader = new();
        PrepareFull(shader, tri);

        Assert.True(shader.TryShade(MakeSample(tri), out Vec3 shaded, out double alpha));
        Assert.Equal(1.0, alpha);
        Assert.Equal(0.11, shaded.X, precision: 9);
    }

    [Fact]
    public void Full_BlendReturnsSampledAlpha()
    {
        Triangle tri = MakeTriangle(new Material(new Vec3(1, 1, 1), alpha: 0.5, alphaMode: MaterialAlphaMode.Blend));
        FullPixelShader shader = new();
        PrepareFull(shader, tri);

        Assert.True(shader.TryShade(MakeSample(tri), out _, out double alpha));
        Assert.Equal(0.5, alpha, precision: 9);
    }

    [Fact]
    public void Full_NormalMap_PerturbsShading()
    {
        PreparedLight[] lights = { FrontDirectional() };
        Triangle plain = MakeTriangle(new Material(new Vec3(1, 1, 1), roughness: 0.9));
        Triangle flat = MakeTriangle(new Material(new Vec3(1, 1, 1), roughness: 0.9, normalTexture: SolidTexture("flat", 128, 128, 255)));
        Triangle tilted = MakeTriangle(new Material(new Vec3(1, 1, 1), roughness: 0.9, normalTexture: SolidTexture("tilted", 255, 128, 255)));

        FullPixelShader plainShader = new();
        PrepareFull(plainShader, plain);
        Assert.True(plainShader.TryShade(MakeSample(plain, lights), out Vec3 plainShaded, out _));

        FullPixelShader flatShader = new();
        PrepareFull(flatShader, flat);
        Assert.True(flatShader.TryShade(MakeSample(flat, lights), out Vec3 flatShaded, out _));

        FullPixelShader tiltedShader = new();
        PrepareFull(tiltedShader, tilted);
        Assert.True(tiltedShader.TryShade(MakeSample(tilted, lights), out Vec3 tiltedShaded, out _));

        Assert.True(Math.Abs(flatShaded.X - plainShaded.X) < 0.01);
        Assert.True(tiltedShaded.X < flatShaded.X - 0.05);
    }

    [Fact]
    public void Full_Occlusion_AttenuatesAmbientOnly()
    {
        Triangle plain = MakeTriangle(new Material(new Vec3(0.8, 0.7, 0.6)));
        Triangle occluded = MakeTriangle(new Material(new Vec3(0.8, 0.7, 0.6), occlusionTexture: SolidTexture("black", 0, 0, 0)));
        Triangle half = MakeTriangle(new Material(new Vec3(0.8, 0.7, 0.6), occlusionTexture: SolidTexture("black", 0, 0, 0), occlusionStrength: 0.5));

        FullPixelShader plainShader = new();
        PrepareFull(plainShader, plain);
        Assert.True(plainShader.TryShade(MakeSample(plain), out Vec3 plainShaded, out _));

        FullPixelShader occludedShader = new();
        PrepareFull(occludedShader, occluded);
        Assert.True(occludedShader.TryShade(MakeSample(occluded), out Vec3 occludedShaded, out _));

        FullPixelShader halfShader = new();
        PrepareFull(halfShader, half);
        Assert.True(halfShader.TryShade(MakeSample(half), out Vec3 halfShaded, out _));

        Assert.Equal(0.088, plainShaded.X, precision: 9);
        Assert.Equal(Vec3.Zero, occludedShaded);
        Assert.Equal(0.5, halfShaded.X / plainShaded.X, precision: 9);
    }

    [Fact]
    public void Fast_Prepare_AppliesMaskCutoffAtCentroid()
    {
        Triangle culled = MakeTriangle(new Material(new Vec3(1, 1, 1), alpha: 0.3, alphaMode: MaterialAlphaMode.Mask, alphaCutoff: 0.5));
        Triangle kept = MakeTriangle(new Material(new Vec3(1, 1, 1), alpha: 0.7, alphaMode: MaterialAlphaMode.Mask, alphaCutoff: 0.5));
        Triangle opaque = MakeTriangle(new Material(new Vec3(1, 1, 1), texture: SolidTexture("zero-alpha", 255, 255, 255, 0)));
        ProjectedVertex vertex = new(0, 0, 1);

        Assert.False(new FastPixelShader().Prepare(culled, vertex, vertex, vertex));
        Assert.True(new FastPixelShader().Prepare(kept, vertex, vertex, vertex));
        Assert.True(new FastPixelShader().Prepare(opaque, vertex, vertex, vertex));
    }

    [Fact]
    public void Fast_Blend_ReturnsCentroidAlpha()
    {
        Triangle tri = MakeTriangle(new Material(new Vec3(1, 1, 1), alpha: 0.5, alphaMode: MaterialAlphaMode.Blend));
        FastPixelShader shader = new();
        ProjectedVertex vertex = new(0, 0, 1);
        Assert.True(shader.Prepare(tri, vertex, vertex, vertex));

        Assert.True(shader.TryShade(MakeSample(tri), out _, out double alpha));
        Assert.Equal(0.5, alpha, precision: 9);
    }

    [Fact]
    public void ColorMath_DisplayPackRoundTrips()
    {
        Vec3 display = new(0.2, 0.4, 0.8);
        Vec3 unpacked = ColorMath.UnpackDisplay(ColorMath.PackDisplay(display));

        Assert.True(Math.Abs(unpacked.X - display.X) < 0.002);
        Assert.True(Math.Abs(unpacked.Y - display.Y) < 0.002);
        Assert.True(Math.Abs(unpacked.Z - display.Z) < 0.002);
    }

    [Fact]
    public void Rasterize_Blend_CompositesWithoutDepthWrite()
    {
        Material material = new(new Vec3(0, 0, 0), alpha: 0.5, alphaMode: MaterialAlphaMode.Blend);
        Triangle tri = MakeTriangle(material);
        int width = 64;
        int height = 48;
        uint[] pixels = new uint[width * height];
        double[] zBuffer = new double[pixels.Length];
        Array.Fill(zBuffer, double.PositiveInfinity);
        uint background = ColorMath.PackDisplay(new Vec3(0.4, 0.6, 0.8));
        Array.Fill(pixels, background);
        CameraBasis basis = new(new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

        new TriangleRasterizer().Rasterize(pixels, zBuffer, width, height, tri, Vec3.Zero, basis,
            Array.Empty<PreparedLight>(), interactiveFast: false, exposure: 1.0);

        int changed = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            Assert.Equal(double.PositiveInfinity, zBuffer[i]);
            if (pixels[i] == background)
                continue;
            changed++;
            Vec3 unpacked = ColorMath.UnpackDisplay(pixels[i]);
            Assert.True(Math.Abs(unpacked.X - 0.2) < 0.005);
            Assert.True(Math.Abs(unpacked.Y - 0.3) < 0.005);
            Assert.True(Math.Abs(unpacked.Z - 0.4) < 0.005);
        }
        Assert.True(changed > 0);
    }

    [Fact]
    public void Rasterize_Opaque_WritesDepth()
    {
        Material material = new(new Vec3(0, 0, 0));
        Triangle tri = MakeTriangle(material);
        int width = 64;
        int height = 48;
        uint[] pixels = new uint[width * height];
        double[] zBuffer = new double[pixels.Length];
        Array.Fill(zBuffer, double.PositiveInfinity);
        uint background = ColorMath.PackDisplay(new Vec3(0.4, 0.6, 0.8));
        Array.Fill(pixels, background);
        CameraBasis basis = new(new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

        new TriangleRasterizer().Rasterize(pixels, zBuffer, width, height, tri, Vec3.Zero, basis,
            Array.Empty<PreparedLight>(), interactiveFast: false, exposure: 1.0);

        int changed = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] == background)
            {
                Assert.Equal(double.PositiveInfinity, zBuffer[i]);
                continue;
            }
            changed++;
            Assert.Equal(4.0, zBuffer[i], precision: 9);
        }
        Assert.True(changed > 0);
    }

    private static ShadowMap MakeShadowMap()
    {
        SceneLight light = new("sun", Vec3.Zero, new Vec3(1, 1, 1), 1.0,
            kind: SceneLightKind.Directional, direction: new Vec3(0, 0, 1), castsShadow: true);
        ShadowProjection projection = ShadowProjectionSelector.Create(light, new Vec3(0, 0, 4), 3.0);
        return new ShadowMap(light.Id, 64, projection);
    }

    [Fact]
    public void ShadowMap_MaskCullsTransparentTexels()
    {
        Triangle opaque = MakeTriangle(new Material(new Vec3(1, 1, 1), texture: SolidTexture("zero-alpha", 255, 255, 255, 0)));
        Triangle maskedOut = MakeTriangle(new Material(new Vec3(1, 1, 1), texture: SolidTexture("zero-alpha", 255, 255, 255, 0), alphaMode: MaterialAlphaMode.Mask));
        Triangle maskedIn = MakeTriangle(new Material(new Vec3(1, 1, 1), texture: SolidTexture("solid", 255, 255, 255, 255), alphaMode: MaterialAlphaMode.Mask));

        ShadowMap opaqueMap = MakeShadowMap();
        ShadowTriangleRasterizer.Rasterize(opaqueMap, opaque);
        ShadowMap culledMap = MakeShadowMap();
        ShadowTriangleRasterizer.Rasterize(culledMap, maskedOut);
        ShadowMap keptMap = MakeShadowMap();
        ShadowTriangleRasterizer.Rasterize(keptMap, maskedIn);

        Assert.True(opaqueMap.HasDepth);
        Assert.False(culledMap.HasDepth);
        Assert.True(keptMap.HasDepth);
    }
}
