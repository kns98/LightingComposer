using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class HeadlessLightingTests
{
    private static CameraDefinition TestCamera() => new()
    {
        Position = new Vec3(2, 1, -3),
        Target = new Vec3(0, 0, 3.55),
        Up = new Vec3(0, 1, 0)
    };

    private static RenderRequest EmptyRequest() => new() { Input = "scene.obj" };

    [Fact]
    public void Resolve_RepositionsDefaultLightsNearCamera()
    {
        Scene scene = new();
        scene.Lights.Add(new SceneLight("gltf_default_key", new Vec3(100, 100, 100), new Vec3(1, 1, 1), 5.0, isDefault: true));
        scene.Lights.Add(new SceneLight("gltf_default_fill", new Vec3(-100, -100, -100), new Vec3(1, 1, 1), 2.0, isDefault: true));
        CameraDefinition camera = TestCamera();

        HeadlessLighting.Resolve(scene, camera, EmptyRequest());

        CameraBasis basis = camera.ToBasis();
        double dist = (camera.Position - camera.Target).Length();
        Vec3 expectedKey = camera.Position + basis.Up * (dist * 0.55) + basis.Right * (dist * 0.40);
        Vec3 expectedFill = camera.Position - basis.Right * (dist * 0.65) + basis.Up * (dist * 0.18);
        Assert.Equal(expectedKey.X, scene.Lights[0].Position.X, precision: 9);
        Assert.Equal(expectedKey.Y, scene.Lights[0].Position.Y, precision: 9);
        Assert.Equal(expectedKey.Z, scene.Lights[0].Position.Z, precision: 9);
        Assert.Equal(expectedFill.X, scene.Lights[1].Position.X, precision: 9);
        Assert.Equal(expectedFill.Y, scene.Lights[1].Position.Y, precision: 9);
        Assert.Equal(expectedFill.Z, scene.Lights[1].Position.Z, precision: 9);
        Assert.Equal(5.0, scene.Lights[0].Intensity);
    }

    [Fact]
    public void Resolve_KeepsAuthoredLightsInPlace()
    {
        Scene scene = new();
        Vec3 filePosition = new(9, 9, 9);
        scene.Lights.Add(new SceneLight("file_spot", filePosition, new Vec3(1, 1, 1), 4.0, kind: SceneLightKind.Spot, isImported: true));

        HeadlessLighting.Resolve(scene, TestCamera(), EmptyRequest());

        Assert.Single(scene.Lights);
        Assert.Equal(filePosition, scene.Lights[0].Position);
    }

    [Fact]
    public void Resolve_AddsDefaultsWhenNoEnabledLights()
    {
        Scene scene = new();
        scene.Lights.Add(new SceneLight("off", new Vec3(0, 0, 0), new Vec3(1, 1, 1), 1.0, enabled: false));

        HeadlessLighting.Resolve(scene, TestCamera(), EmptyRequest());

        Assert.Equal(3, scene.Lights.Count);
        Assert.All(scene.Lights.Skip(1), light => Assert.True(light.Enabled));
        Assert.All(scene.Lights.Skip(1), light => Assert.True(light.IsDefault));
    }

    [Fact]
    public void Resolve_ExplicitLightsReplaceAll()
    {
        Scene scene = new();
        scene.Lights.Add(new SceneLight("gltf_default_key", new Vec3(100, 100, 100), new Vec3(1, 1, 1), 5.0, isDefault: true));
        RenderRequest request = EmptyRequest();
        request.Lights = new[]
        {
            LightRequest.Parse("position=1,0,0", 0),
            LightRequest.Parse("position=0,1,0", 1)
        };

        HeadlessLighting.Resolve(scene, TestCamera(), request);

        Assert.Equal(2, scene.Lights.Count);
        Assert.Equal(new Vec3(1, 0, 0), scene.Lights[0].Position);
        Assert.Equal(new Vec3(0, 1, 0), scene.Lights[1].Position);
    }

    [Fact]
    public void RenderRequest_ParsesZoomAndRepeatedLights()
    {
        CommandLineArguments values = CommandLineArguments.Parse(new[]
        {
            "scene.obj", "--zoom", "1.6",
            "--light", "position=1,0,0",
            "--light", "position=0,1,0;intensity=2"
        });

        RenderRequest request = RenderRequest.FromCommandLine(values);

        Assert.Equal(1.6, request.Zoom);
        Assert.Equal(2, request.Lights.Count);
        Assert.Equal(new Vec3(1, 0, 0), request.Lights[0].Position);
        Assert.Equal(2.0, request.Lights[1].Intensity);
    }
}
