using System.Numerics;
using System.Reflection;
using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Veldrid;
using Veldrid.SPIRV;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class RasterShadowTests
{
    private static SceneLight DirectionalLight(Vec3 direction, bool castsShadow = true) =>
        new("dir", Vec3.Zero, new Vec3(1, 1, 1), 3.0, kind: SceneLightKind.Directional, direction: direction, castsShadow: castsShadow);

    [Fact]
    public void ShadowCamera_Orthographic_CentersOnBounds()
    {
        Vector4[] rows = ShadowCameraBuilder.BuildRows(DirectionalLight(new Vec3(0, -1, 0)), Vec3.Zero, 2.0);

        Assert.Equal(5, rows.Length);
        Assert.Equal(new Vector4(0, 5, 0, 1), rows[0]);
        Assert.Equal(new Vector4(0, -1, 0, 0), rows[1]);
        Assert.Equal(new Vector4(0, 0, -1, 0), rows[2]);
        Assert.Equal(new Vector4(1, 0, 0, 0), rows[3]);
        Assert.Equal(1.0f, rows[4].X);
        Assert.Equal(-2.9, rows[4].Y, precision: 5);
        Assert.Equal(0.0f, rows[4].Z);
        Assert.Equal(10.0f, rows[4].W);
    }

    [Fact]
    public void ShadowCamera_Spot_UsesConeAndRange()
    {
        SceneLight light = new("spot", new Vec3(0, 5, 0), new Vec3(1, 1, 1), 3.0,
            kind: SceneLightKind.Spot, direction: new Vec3(0, -1, 0), range: 7.0, outerConeAngle: Math.PI / 6.0);

        Vector4[] rows = ShadowCameraBuilder.BuildRows(light, Vec3.Zero, 2.0);

        Assert.Equal(new Vector4(0, 5, 0, 1), rows[0]);
        Assert.Equal(new Vector4(0, -1, 0, 0), rows[1]);
        Assert.Equal(1.0f, rows[4].X);
        Assert.Equal(Math.Tan(Math.PI / 6.0 * 2.2 / 2.0), rows[4].Y, precision: 5);
        Assert.Equal(0.035, rows[4].Z, precision: 6);
        Assert.Equal(7.0f, rows[4].W);
    }

    [Fact]
    public void ShadowCamera_Point_AimsAtCenter()
    {
        SceneLight light = new("point", new Vec3(3, 4, 0), new Vec3(1, 1, 1), 3.0);

        Vector4[] rows = ShadowCameraBuilder.BuildRows(light, new Vec3(1, 0, 0), 2.0);

        Assert.Equal(new Vector4(3, 4, 0, 1), rows[0]);
        Assert.Equal(-2.0 / Math.Sqrt(20.0), rows[1].X, precision: 6);
        Assert.Equal(-4.0 / Math.Sqrt(20.0), rows[1].Y, precision: 6);
        Assert.Equal(0.0, rows[1].Z, precision: 6);
        Assert.Equal(Math.Tan(Math.PI * 0.36), rows[4].Y, precision: 6);
        Assert.Equal(10.0f, rows[4].W);
    }

    [Fact]
    public void ShadowCamera_DegeneratePoint_FallsBackDown()
    {
        SceneLight light = new("point", new Vec3(1, 0, 0), new Vec3(1, 1, 1), 3.0);

        Vector4[] rows = ShadowCameraBuilder.BuildRows(light, new Vec3(1, 0, 0), 2.0);

        Assert.Equal(new Vector4(0, -1, 0, 0), rows[1]);
    }

    [Fact]
    public void ShadowCamera_SmallRadius_ClampsUp()
    {
        Vector4[] rows = ShadowCameraBuilder.BuildRows(DirectionalLight(new Vec3(0, -1, 0)), Vec3.Zero, 0.0);

        Assert.Equal(-0.35 * 1.45, rows[4].Y, precision: 6);
        Assert.Equal(0.35 * 5.0, rows[4].W, precision: 6);
    }

    [Fact]
    public void ShadowCamera_FrameRows_PackSelectedLayers()
    {
        Scene scene = new();
        scene.Lights.Add(DirectionalLight(new Vec3(0, -1, 0)));
        scene.Lights.Add(new SceneLight("plain", Vec3.Zero, new Vec3(1, 1, 1), 1.0, castsShadow: false));
        IReadOnlyList<PreparedShadowLight> selected = new LightBufferBuilder().SelectShadowLights(scene);

        Vector4[] rows = ShadowCameraBuilder.BuildFrameRows(selected, Vec3.Zero, 2.0);

        Assert.Equal(20, rows.Length);
        Assert.Equal(new Vector4(0, 5, 0, 1), rows[0]);
        Assert.Equal(Vector4.Zero, rows[5]);
        Assert.Equal(Vector4.Zero, rows[19]);
    }

    [Fact]
    public void LightBufferBuilder_AssignsFirstFourShadowLayers()
    {
        Scene scene = new();
        for (int i = 0; i < 5; i++)
            scene.Lights.Add(DirectionalLight(new Vec3(0, -1, 0)));
        scene.Lights.Add(new SceneLight("plain", Vec3.Zero, new Vec3(1, 1, 1), 1.0, castsShadow: false));
        scene.Lights.Add(new SceneLight("off", Vec3.Zero, new Vec3(1, 1, 1), 1.0, enabled: false));

        RasterLight[] packed = new LightBufferBuilder().Build(scene);
        IReadOnlyList<PreparedShadowLight> selected = new LightBufferBuilder().SelectShadowLights(scene);

        Assert.Equal(6, packed.Length);
        Assert.Equal(0.0f, packed[0].ConeEnabled.Z);
        Assert.Equal(1.0f, packed[1].ConeEnabled.Z);
        Assert.Equal(2.0f, packed[2].ConeEnabled.Z);
        Assert.Equal(3.0f, packed[3].ConeEnabled.Z);
        Assert.Equal(-1.0f, packed[4].ConeEnabled.Z);
        Assert.Equal(-1.0f, packed[5].ConeEnabled.Z);
        Assert.Equal(4, selected.Count);
        Assert.Equal(0, selected[0].Layer);
        Assert.Equal(3, selected[3].Layer);
        Assert.Same(scene.Lights[0], selected[0].Light);
    }

    private static string LoadEmbeddedShader(string resourceName)
    {
        Assembly assembly = typeof(GpuTriangle).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        Assert.True(stream != null, resourceName);
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    [Fact]
    public void RasterShaders_CompileToSpirv()
    {
        (string Resource, ShaderStages Stage)[] shaders =
        {
            ("Raster.raster.vert.glsl", ShaderStages.Vertex),
            ("Raster.raster.frag.glsl", ShaderStages.Fragment),
            ("Raster.shadow.vert.glsl", ShaderStages.Vertex),
            ("Raster.shadow.frag.glsl", ShaderStages.Fragment)
        };
        foreach ((string resource, ShaderStages stage) in shaders)
        {
            SpirvCompilationResult result = SpirvCompilation.CompileGlslToSpirv(
                LoadEmbeddedShader(resource), "main", stage, new GlslCompileOptions());
            Assert.True(result.SpirvBytes.Length > 0, resource);
        }
    }

    private static bool TryCreateVulkanDevice()
    {
        try
        {
            using GraphicsDevice gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions { Debug = false });
            return gd.BackendType == GraphicsBackend.Vulkan;
        }
        catch
        {
            return false;
        }
    }

    private static void AddTri(Scene scene, Vec3 a, Vec3 b, Vec3 c, Vec3 normal, Material material) =>
        scene.AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1),
            normal, normal, normal, material);

    private static void AddBox(Scene scene, Vec3 min, Vec3 max, Material material)
    {
        Vec3[] corners =
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
        };
        (int A, int B, int C, int D, Vec3 Normal)[] faces =
        {
            (0, 1, 2, 3, new Vec3(0, -1, 0)),
            (4, 7, 6, 5, new Vec3(0, 1, 0)),
            (0, 4, 5, 1, new Vec3(0, 0, -1)),
            (2, 6, 7, 3, new Vec3(0, 0, 1)),
            (1, 5, 6, 2, new Vec3(1, 0, 0)),
            (3, 7, 4, 0, new Vec3(-1, 0, 0))
        };
        foreach ((int a, int b, int c, int d, Vec3 normal) in faces)
        {
            AddTri(scene, corners[a], corners[b], corners[c], normal, material);
            AddTri(scene, corners[a], corners[c], corners[d], normal, material);
        }
    }

    private static double PixelLuminance(uint pixel) =>
        (pixel & 0xFF) + ((pixel >> 8) & 0xFF) + ((pixel >> 16) & 0xFF);

    private static double MeanLuminance(RenderImage image)
    {
        double sum = 0.0;
        foreach (uint pixel in image.PackedRgba32)
            sum += PixelLuminance(pixel);
        return sum / image.PackedRgba32.Length;
    }

    private static RenderImage RenderShadowScene(bool castsShadow)
    {
        Scene scene = new();
        scene.BeginGroup("shadow-test");
        Material floor = new(new Vec3(0.85, 0.85, 0.85), roughness: 0.9);
        AddTri(scene, new Vec3(-12, 0, -12), new Vec3(12, 0, -12), new Vec3(12, 0, 12), new Vec3(0, 1, 0), floor);
        AddTri(scene, new Vec3(-12, 0, -12), new Vec3(12, 0, 12), new Vec3(-12, 0, 12), new Vec3(0, 1, 0), floor);
        AddBox(scene, new Vec3(-0.8, 0.4, -0.8), new Vec3(0.8, 2.0, 0.8), new Material(new Vec3(0.6, 0.6, 0.6), roughness: 0.6));
        scene.EndGroup();
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);
        scene.Lights.Add(new SceneLight("sun", new Vec3(0, 0, 0), new Vec3(1, 1, 1), 3.0,
            kind: SceneLightKind.Directional, direction: new Vec3(0.45, -1, 0.3), castsShadow: castsShadow));

        Vec3 position = new(0, 6.5, -8.5);
        Vec3 target = new(0, 0, 0);
        Vec3 forward = (target - position).Normalize();
        Vec3 right = new Vec3(0, 1, 0).Cross(forward).Normalize();
        Vec3 up = forward.Cross(right).Normalize();
        return VulkanRasterRenderer.Render(scene, position, new CameraBasis(forward, right, up), 160, 120,
            CancellationToken.None, out _);
    }

    [Fact]
    public void Raster_Shadows_DarkenOccludedPixels()
    {
        if (!TryCreateVulkanDevice())
            return;

        try
        {
            RenderImage lit = RenderShadowScene(castsShadow: false);
            RenderImage shadowed = RenderShadowScene(castsShadow: true);

            Assert.False(lit.PackedRgba32.SequenceEqual(shadowed.PackedRgba32));
            Assert.True(MeanLuminance(shadowed) < MeanLuminance(lit));
            int darkened = 0;
            for (int i = 0; i < lit.PackedRgba32.Length; i++)
            {
                if (PixelLuminance(shadowed.PackedRgba32[i]) < PixelLuminance(lit.PackedRgba32[i]) * 0.9)
                    darkened++;
            }
            Assert.True((double)darkened / lit.PackedRgba32.Length > 0.001);
        }
        finally
        {
            VulkanRasterRenderer.DisposeSharedDevice();
        }
    }
}
