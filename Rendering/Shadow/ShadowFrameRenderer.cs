// File: Rendering/Shadow/ShadowFrameRenderer.cs
// Purpose: Shaded frame rendering from a reusable shadow-map cache.

using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class ShadowFrameRenderer
{
    private const int MaxInteractiveLights = 2;

    /// <summary>Renders one shaded raster preview frame using a reusable shadow-map cache.</summary>
    public RenderImage Render(
        ShadowRasterRenderer.PreviewCache cache, Vec3 cameraPosition, CameraBasis basis,
        int width, int height, CancellationToken cancellationToken, out string details,
        bool interactiveFast, bool wireframe, Vec3 backgroundTop, Vec3 backgroundBottom, double exposure)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (wireframe)
            return new WireframeFrameRenderer().Render(cache.Scene, cameraPosition, basis, width, height, cancellationToken, out details, backgroundTop, backgroundBottom, exposure);

        Console.Error.WriteLine($"DBG raster pos=({cameraPosition.X:0.##},{cameraPosition.Y:0.##},{cameraPosition.Z:0.##}) fwd=({basis.Forward.X:0.##},{basis.Forward.Y:0.##},{basis.Forward.Z:0.##})");
        Stopwatch stopwatch = Stopwatch.StartNew();
        Scene scene = cache.Scene;
        PreparedLight[] lights = new LightPreparer().Prepare(scene, cache.ShadowMaps);
        if (interactiveFast && lights.Length > MaxInteractiveLights)
            lights = lights.Take(MaxInteractiveLights).ToArray();

        uint[] pixels = new uint[checked(width * height)];
        double[] zBuffer = new double[pixels.Length];
        Array.Fill(zBuffer, double.PositiveInfinity);
        BackgroundPainter.Clear(pixels, width, height, backgroundTop, backgroundBottom, exposure);

        TriangleRasterizer rasterizer = new();
        foreach (Triangle tri in scene.Triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rasterizer.Rasterize(pixels, zBuffer, width, height, tri, cameraPosition, basis, lights, interactiveFast, exposure);
        }

        stopwatch.Stop();
        int nonBg = 0;
        for (int i = 0; i < pixels.Length; i += 97)
            if (pixels[i] != pixels[0]) nonBg++;
        uint center = pixels[(height / 2) * width + (width / 2)];
        Console.Error.WriteLine($"DBG pixels center=0x{center:X8} mixed={nonBg}");
        details = $"Shadow raster {(interactiveFast ? "20fps preview" : "OK")} - {width}x{height}, revision={cache.CacheStamp.Revision}, triangles={cache.TriangleCount}, lights={(interactiveFast ? lights.Length : cache.LightCount)}, shadowMaps={cache.EnabledShadowMapCount}, cache={cache.BuildMilliseconds}ms, frame={stopwatch.ElapsedMilliseconds}ms";
        return new RenderImage(width, height, pixels);
    }
}
