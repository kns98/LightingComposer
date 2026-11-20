using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Math3D;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class TurntableCameraTests
{
    [Fact]
    public void Orbit_ReturnsOnePositionPerFrameAroundFullCircle()
    {
        CameraDefinition camera = new()
        {
            Position = new Vec3(0, 1, -6),
            Target = new Vec3(0, 0, 0),
            Up = new Vec3(0, 1, 0)
        };

        IReadOnlyList<Vec3> positions = TurntableCamera.Orbit(camera, frameCount: 4);

        Assert.Equal(4, positions.Count);
        Assert.Equal(new Vec3(0, 1, -6), positions[0]);
        foreach (Vec3 position in positions)
        {
            double radius = Math.Sqrt(position.X * position.X + position.Z * position.Z);
            Assert.Equal(6.0, radius, precision: 9);
            Assert.Equal(1.0, position.Y, precision: 9);
        }
        Assert.Equal(-positions[0].Z, positions[2].Z, precision: 9);
    }

    [Fact]
    public void Orbit_SingleFrame_KeepsBasePosition()
    {
        CameraDefinition camera = new()
        {
            Position = new Vec3(1, 2, 3),
            Target = new Vec3(0, 0, 0),
            Up = new Vec3(0, 1, 0)
        };

        IReadOnlyList<Vec3> positions = TurntableCamera.Orbit(camera, frameCount: 1);

        Assert.Single(positions);
        Assert.Equal(new Vec3(1, 2, 3), positions[0]);
    }

    [Fact]
    public void RenderRequest_AnimationRequiresGifAndFastBackend()
    {
        RenderRequest gif = new()
        {
            Input = "scene.obj",
            Frames = 12,
            Output = "out.gif",
            Backend = LightingShowcase.SceneGraph.RenderBackend.VulkanRasterPreview
        };
        gif.Validate();

        RenderRequest noGif = new() { Input = "scene.obj", Frames = 12, Output = "out.png" };
        Assert.Throws<ArgumentException>(() => noGif.Validate());

        RenderRequest slow = new()
        {
            Input = "scene.obj",
            Frames = 12,
            Output = "out.gif",
            Backend = LightingShowcase.SceneGraph.RenderBackend.Cpu
        };
        Assert.Throws<ArgumentException>(() => slow.Validate());
    }
}
