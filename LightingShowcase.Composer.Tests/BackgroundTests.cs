using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class BackgroundTests
{
    [Fact]
    public void ShadowRender_HonorsCustomBackground()
    {
        Scene scene = new();
        ShadowRasterRenderer.PreviewCache cache = ShadowRasterRenderer.BuildCache(scene, CancellationToken.None);
        CameraBasis basis = new CameraDefinition { Position = new Vec3(0, 0, -4), Target = new Vec3(0, 0, 0) }.ToBasis();
        RenderImage image = ShadowRasterRenderer.Render(
            cache, new Vec3(0, 0, -4), basis, 16, 16, CancellationToken.None, out _,
            backgroundTop: new Vec3(1, 0, 0), backgroundBottom: new Vec3(0, 0, 1));

        Assert.Equal(0xFF0000FFu, image.PackedRgba32[0]);
        Assert.Equal(0xFFFF0000u, image.PackedRgba32[16 * 16 - 1]);
    }

    [Fact]
    public void ShadowRender_RespectsExposure()
    {
        Scene scene = new();
        ShadowRasterRenderer.PreviewCache cache = ShadowRasterRenderer.BuildCache(scene, CancellationToken.None);
        CameraBasis basis = new CameraDefinition { Position = new Vec3(0, 0, -4), Target = new Vec3(0, 0, 0) }.ToBasis();
        RenderImage dim = ShadowRasterRenderer.Render(
            cache, new Vec3(0, 0, -4), basis, 16, 16, CancellationToken.None, out _,
            backgroundTop: new Vec3(0.25, 0, 0), backgroundBottom: new Vec3(0, 0, 0));
        RenderImage bright = ShadowRasterRenderer.Render(
            cache, new Vec3(0, 0, -4), basis, 16, 16, CancellationToken.None, out _,
            backgroundTop: new Vec3(0.25, 0, 0), backgroundBottom: new Vec3(0, 0, 0), exposure: 2.0);

        Assert.Equal(0xFF000088u, dim.PackedRgba32[0]);
        Assert.Equal(0xFF0000BAu, bright.PackedRgba32[0]);
    }

    [Fact]
    public void CpuTracer_HonorsCustomBackground()
    {
        RayTracer tracer = new(new Scene(), new LightingState())
        {
            BackgroundTop = new Vec3(1, 0, 0),
            BackgroundBottom = new Vec3(0, 0, 1)
        };

        Vec3 up = tracer.Trace(new Ray(new Vec3(0, 0, 0), new Vec3(0, 1, 0)));
        Vec3 down = tracer.Trace(new Ray(new Vec3(0, 0, 0), new Vec3(0, -1, 0)));

        Assert.True(up.X > 0.9 && up.Z < 0.1);
        Assert.True(down.Z > 0.9 && down.X < 0.1);
    }
}
