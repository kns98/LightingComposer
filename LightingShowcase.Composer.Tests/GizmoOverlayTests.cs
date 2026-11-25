using LightingShowcase.CameraSystem;
using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class GizmoOverlayTests
{
    private static int DrawnPixels(ComposerGizmoMode mode)
    {
        int width = 320;
        int height = 240;
        uint background = 0xff000000u;
        uint[] pixels = new uint[width * height];
        Array.Fill(pixels, background);
        RenderImage image = new(width, height, pixels);
        CameraDefinition camera = new() { Position = new Vec3(0, 1.5, -4), Target = new Vec3(0, 1, 0) };
        Aabb bounds = new(new Vec3(-0.5, 0.5, -0.5), new Vec3(0.5, 1.5, 0.5));

        ComposerOverlayRenderer.DrawSelection(image, camera, bounds, selectedTriangles: null, gizmoMode: mode, drawBounds: false);

        int drawn = 0;
        foreach (uint pixel in pixels)
        {
            if (pixel != background)
                drawn++;
        }
        return drawn;
    }

    [Fact]
    public void Gizmo_Translate_DrawsHandles()
    {
        Assert.True(DrawnPixels(ComposerGizmoMode.Translate) > 100);
    }

    [Fact]
    public void Gizmo_Rotate_DrawsRings()
    {
        Assert.True(DrawnPixels(ComposerGizmoMode.Rotate) > 100);
    }

    [Fact]
    public void Gizmo_Scale_DrawsHandles()
    {
        Assert.True(DrawnPixels(ComposerGizmoMode.Scale) > 100);
    }
}
