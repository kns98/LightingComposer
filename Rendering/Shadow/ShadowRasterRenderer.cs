// File: Rendering/Shadow/ShadowRasterRenderer.cs
// Purpose: Public facade for the fast raster preview with shadow maps.
//
// This renderer is independent of the UI and the CPU/Vulkan
// ray tracers. It owns a small AMD-style preview pipeline: build reusable shadow
// maps from scene lights, project triangles, rasterize a depth buffer, and shade
// visible pixels with direct lighting. It is not a path tracer and it does not
// call Scene.Intersect for visibility or shadows.

using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

/// <summary>Independent realtime-style raster preview renderer with z-buffered triangle drawing and shadow-map shadows.</summary>
public static class ShadowRasterRenderer
{
    /// <summary>
    /// Reusable raster cache. Shadow maps are view-independent, so they should be
    /// rebuilt only when the scene or lights change, not on every camera orbit.
    /// </summary>
    public sealed class PreviewCache
    {
        /// <summary>Captures the scene stamp, shadow maps, and build statistics.</summary>
        internal PreviewCache(Scene scene, List<ShadowMap> shadowMaps, long buildMilliseconds)
        {
            CacheStamp = SceneCacheStamp.Capture(scene);
            Scene = scene;
            ShadowMaps = shadowMaps;
            BuildMilliseconds = buildMilliseconds;
            TriangleCount = scene.Triangles.Count;
            LightCount = scene.Lights.Count;
            EnabledShadowMapCount = shadowMaps.Count(m => m.Enabled);
        }

        public SceneCacheStamp CacheStamp { get; }
        public Scene Scene { get; }
        /// <summary>Returns true when the cache stamp still matches the scene.</summary>
        public bool IsCurrent(Scene scene) => CacheStamp.Matches(scene);
        public int TriangleCount { get; }
        public int LightCount { get; }
        public int EnabledShadowMapCount { get; }
        public long BuildMilliseconds { get; }
        internal IReadOnlyList<ShadowMap> ShadowMaps { get; }
    }

    /// <summary>Builds reusable shadow-map state for a scene snapshot.</summary>
    public static PreviewCache BuildCache(Scene scene, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<ShadowMap> shadowMaps = new ShadowMapBuilder().Build(scene, cancellationToken);
        stopwatch.Stop();
        return new PreviewCache(scene, shadowMaps, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Renders one shaded raster preview frame into a cross-platform RGBA image.</summary>
    public static RenderImage Render(
        Scene scene, Vec3 cameraPosition, CameraBasis cameraBasis, int width, int height,
        CancellationToken cancellationToken, out string details, bool wireframe = false,
        Vec3? backgroundTop = null, Vec3? backgroundBottom = null)
    {
        PreviewCache cache = BuildCache(scene, cancellationToken);
        return Render(cache, cameraPosition, cameraBasis, width, height, cancellationToken, out details,
            interactiveFast: false, wireframe: wireframe, backgroundTop: backgroundTop, backgroundBottom: backgroundBottom);
    }

    /// <summary>Renders one shaded raster preview frame using a reusable shadow-map cache.</summary>
    public static RenderImage Render(
        PreviewCache cache, Vec3 cameraPosition, CameraBasis cameraBasis, int width, int height,
        CancellationToken cancellationToken, out string details, bool interactiveFast = false, bool wireframe = false,
        Vec3? backgroundTop = null, Vec3? backgroundBottom = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return new ShadowFrameRenderer().Render(cache, cameraPosition, cameraBasis, width, height, cancellationToken, out details,
            interactiveFast, wireframe,
            backgroundTop ?? RenderSettings.DefaultBackgroundTop, backgroundBottom ?? RenderSettings.DefaultBackgroundBottom);
    }
}
