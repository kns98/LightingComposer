using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Composer;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class CpuMappingTests
{
    [Fact]
    public void CliAndUiCpuRendersMatch()
    {
        Scene scene = new();
        scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4), new Material(new Vec3(0.8, 0.2, 0.2)));
        scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, 1, 4), new Vec3(-1, 1, 4), new Material(new Vec3(0.8, 0.2, 0.2)));
        scene.Lights.Add(new SceneLight("key", new Vec3(3, 4, -2), new Vec3(1, 1, 1), 5.0));
        CameraDefinition camera = new() { Position = new Vec3(0, 0, -4), Target = new Vec3(0, 0, 4) };

        RenderImage ui = CpuComposerRenderer.Render(
            scene, camera, 32, 24,
            ComposerRenderOptions.DefaultsFor(ComposerRendererKind.Cpu) with { Width = 32, Height = 24, Samples = 1, Bounces = 0 },
            CancellationToken.None, out _);
        RenderImage cli = CpuCommandLineRenderer.Render(
            scene, camera,
            new RenderRequest { Width = 32, Height = 24, Samples = 1, Bounces = 0, FieldOfViewDegrees = 72.0, Exposure = 1.0 },
            CancellationToken.None, out _);

        Assert.Equal(ui.PackedRgba32, cli.PackedRgba32);
    }
}
