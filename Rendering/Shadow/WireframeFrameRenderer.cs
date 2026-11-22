// File: Rendering/Shadow/WireframeFrameRenderer.cs
// Purpose: Wireframe frame with depth prepass and hidden-line dashes.

using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class WireframeFrameRenderer
{
    /// <summary>Renders a wireframe view with solid visible lines and dashed hidden lines.</summary>
    public RenderImage Render(Scene scene, Vec3 cameraPosition, CameraBasis basis, int width, int height, CancellationToken cancellationToken, out string details, Vec3 backgroundTop, Vec3 backgroundBottom)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        uint[] pixels = new uint[checked(width * height)];
        double[] zBuffer = new double[pixels.Length];
        Array.Fill(zBuffer, double.PositiveInfinity);
        BackgroundPainter.Clear(pixels, width, height, backgroundTop, backgroundBottom);
        foreach (Triangle tri in scene.Triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DepthOnlyTriangleRasterizer.Rasterize(zBuffer, width, height, tri, cameraPosition, basis);
        }
        int solidPixels = 0;
        int hiddenPixels = 0;
        foreach (Triangle tri in scene.Triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WireframeTriangleRenderer.Rasterize(pixels, zBuffer, width, height, tri, cameraPosition, basis, ref solidPixels, ref hiddenPixels);
        }
        stopwatch.Stop();
        details = $"Shadow raster wireframe - {width}x{height}, triangles={scene.Triangles.Count}, solid={solidPixels}, hidden={hiddenPixels}, frame={stopwatch.ElapsedMilliseconds}ms";
        return new RenderImage(width, height, pixels);
    }
}
