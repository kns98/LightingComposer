using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class LibraryTextureRenderTests
{
    private static string FindRepoFile(params string[] parts)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        return Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());
    }

    private static RenderImage RenderTextured(Material material)
    {
        Scene scene = new();
        scene.AddTriangle(
            new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4),
            new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), material);
        scene.AddTriangle(
            new Vec3(-1, -1, 4), new Vec3(1, 1, 4), new Vec3(-1, 1, 4),
            new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), material);
        scene.Lights.Add(new SceneLight("key", new Vec3(3, 4, -2), new Vec3(1, 1, 1), 5.0));
        CameraDefinition camera = new() { Position = new Vec3(0, 0, -4), Target = new Vec3(0, 0, 4) };
        return CpuCommandLineRenderer.Render(
            scene, camera,
            new RenderRequest { Width = 32, Height = 24, Samples = 1, Bounces = 0, FieldOfViewDegrees = 72.0, Exposure = 1.0 },
            CancellationToken.None, out _);
    }

    private static double MeanAbsLuminanceDiff(RenderImage a, RenderImage b)
    {
        Assert.Equal(a.PackedRgba32.Length, b.PackedRgba32.Length);
        double total = 0.0;
        for (int i = 0; i < a.PackedRgba32.Length; i++)
        {
            uint pa = a.PackedRgba32[i];
            uint pb = b.PackedRgba32[i];
            double la = (pa & 0xFF) + ((pa >> 8) & 0xFF) + ((pa >> 16) & 0xFF);
            double lb = (pb & 0xFF) + ((pb >> 8) & 0xFF) + ((pb >> 16) & 0xFF);
            total += Math.Abs(la - lb);
        }
        return total / a.PackedRgba32.Length;
    }

    [Theory]
    [InlineData("brick_wall_001_albedo_1k.jpg")]
    [InlineData("concrete_wall_001_albedo_1k.jpg")]
    [InlineData("wood_planks_albedo_1k.jpg")]
    [InlineData("marble_01_albedo_1k.jpg")]
    [InlineData("denim_fabric_albedo_1k.jpg")]
    [InlineData("metal_plate_albedo_1k.jpg")]
    public void LibraryAlbedo_AffectsCpuRender(string fileName)
    {
        Material flat = new(new Vec3(1, 1, 1));
        Material textured = flat.WithTexture(TextureMap.FromFile(FindRepoFile("textures", fileName)));

        double diff = MeanAbsLuminanceDiff(RenderTextured(flat), RenderTextured(textured));

        Assert.True(diff > 1.0, $"Textured render is identical to flat render for {fileName} (diff {diff}).");
    }
}
