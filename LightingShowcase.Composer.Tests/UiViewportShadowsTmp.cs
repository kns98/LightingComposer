using LightingShowcase.CameraSystem;
using LightingShowcase.Composer;
using LightingShowcase.ImportExport.Gltf;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class UiViewportShadowsTests
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

    private static double MeanLuminance(RenderImage image)
    {
        double sum = 0.0;
        foreach (uint pixel in image.PackedRgba32)
            sum += (pixel & 0xFF) + ((pixel >> 8) & 0xFF) + ((pixel >> 16) & 0xFF);
        return sum / image.PackedRgba32.Length;
    }

    [Fact]
    public void UiViewport_AllRenderers_SaveForInspection()
    {
        string path = FindRepoFile("models", "CornellBox", "CornellBox.gltf");
        Assert.True(File.Exists(path), "Cornell box missing: " + path);
        Scene scene = new();
        GltfSceneIO.LoadIntoScene(scene, path, new Material(new Vec3(0.82, 0.82, 0.78)));
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);

        Aabb bounds = scene.GetSceneBounds() ?? new Aabb(new Vec3(-2, -2, -2), new Vec3(2, 2, 2));
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        double radius = Math.Max(0.25, (bounds.Max - bounds.Min).Length() * 0.5);
        CameraDefinition camera = new() { Position = center + new Vec3(radius * 0.75, radius * 0.45, -radius * 2.25), Target = center };
        string scratch = FindRepoFile(".build-tmp");
        scratch = Directory.Exists(scratch) ? scratch : AppContext.BaseDirectory;

        ShadowRasterRenderer.PreviewCache cache = ShadowRasterRenderer.BuildCache(scene, CancellationToken.None);
        RenderImage raster = ShadowRasterRenderer.Render(cache, camera.Position, camera.ToBasis(), 320, 240,
            CancellationToken.None, out _, interactiveFast: true, wireframe: false,
            backgroundTop: RenderSettings.DefaultBackgroundTop, backgroundBottom: RenderSettings.DefaultBackgroundBottom, exposure: 1.0);
        PngWriter.Write(Path.Combine(scratch, "ui-raster.png"), raster);

        RenderImage vraster = VulkanRasterRenderer.Render(scene, camera.Position, camera.ToBasis(), 320, 240,
            null, null, CancellationToken.None, out _,
            wireframe: false, backgroundTop: RenderSettings.DefaultBackgroundTop, backgroundBottom: RenderSettings.DefaultBackgroundBottom, exposure: 1.0);
        PngWriter.Write(Path.Combine(scratch, "ui-vraster.png"), vraster);

        RenderSettings settings = new()
        {
            Width = 320,
            Height = 240,
            Backend = RenderBackend.VulkanGpu,
            PathBounceCount = 0,
            Exposure = 1.0,
            AmbientStrength = 1.0,
            BackgroundTop = RenderSettings.DefaultBackgroundTop,
            BackgroundBottom = RenderSettings.DefaultBackgroundBottom,
            Mode = RenderMode.Lit,
            UseShadows = true
        };
        RenderImage compute = VulkanSceneComputeRenderer.Render(scene, camera.Position, camera.ToBasis(), 320, 240,
            bounceCount: 0, sampleIndex: 0, sampleCount: 1, cancellationToken: CancellationToken.None, details: out _,
            progressCallback: null, settings: settings, fieldOfViewDegrees: 72.0);
        PngWriter.Write(Path.Combine(scratch, "ui-compute.png"), compute);

        RenderImage cpu = CpuComposerRenderer.Render(scene, camera, 320, 240,
            ComposerRenderOptions.DefaultsFor(ComposerRendererKind.Cpu) with { Width = 320, Height = 240 },
            CancellationToken.None, out _);
        PngWriter.Write(Path.Combine(scratch, "ui-cpu.png"), cpu);

        Console.WriteLine($"means raster={MeanLuminance(raster):F1} vraster={MeanLuminance(vraster):F1} compute={MeanLuminance(compute):F1} cpu={MeanLuminance(cpu):F1}");
    }
}
