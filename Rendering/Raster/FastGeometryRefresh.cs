// File: Rendering/Raster/FastGeometryRefresh.cs
// Purpose: In-place vertex reupload when only baked positions changed.

using System.Diagnostics;
using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class FastGeometryRefresh
{
    private readonly RasterStageLog log;

    public FastGeometryRefresh(RasterStageLog log)
    {
        this.log = log;
    }

    /// <summary>
    /// Reuploads only vertex positions, normals, UVs and material indices into an
    /// already allocated Vulkan scene. Textures, pipelines, descriptors, render
    /// targets and buffer allocations stay hot. The caller must hold the engine locks.
    /// </summary>
    public bool TryRefresh(GraphicsDevice? gd, PreparedRasterScene? prepared, Scene scene, CancellationToken token, out string details)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (prepared == null || gd == null || !ReferenceEquals(prepared.Scene, scene))
        {
            details = "No compatible Vulkan raster scene cache is active.";
            return false;
        }
        if (prepared.CacheStamp.Matches(scene))
        {
            details = "Vulkan raster geometry cache is already current.";
            return true;
        }

        int opaqueTriangles = 0;
        int transparentTriangles = 0;
        foreach (Triangle triangle in scene.Triangles)
        {
            token.ThrowIfCancellationRequested();
            if (!TriangleCuller.IsFinite(triangle.A) || !TriangleCuller.IsFinite(triangle.B) ||
                !TriangleCuller.IsFinite(triangle.C) || !TriangleCuller.IsFinite(triangle.Normal))
                continue;
            if (!prepared.MaterialIds.ContainsKey(triangle.Material))
            {
                details = "A material changed, so the Vulkan raster scene requires a full rebuild.";
                return false;
            }

            if (MaterialPacker.IsTransparent(triangle.Material))
                transparentTriangles++;
            else
                opaqueTriangles++;
        }

        if (opaqueTriangles * 3 != prepared.Stats.OpaqueVertexCount ||
            transparentTriangles * 3 != prepared.Stats.TransparentVertexCount)
        {
            details = "Scene topology or transparency classification changed, so the Vulkan raster scene requires a full rebuild.";
            return false;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        new GeometryUploader(gd, log).Upload(prepared.Buffers, scene, prepared.MaterialIds, token);
        SceneBounds bounds = SceneBounds.Compute(scene);
        prepared.Stats.BoundingCenter = bounds.Center;
        prepared.Stats.BoundingRadius = bounds.Radius;
        prepared.CacheStamp = SceneCacheStamp.Capture(scene);
        prepared.PreviewCache.Clear();
        prepared.ActiveMeshEdit = null;
        stopwatch.Stop();
        details = $"Vulkan raster geometry refreshed in-place in {stopwatch.ElapsedMilliseconds} ms; allocations and textures were reused.";
        log.Stage(details);
        return true;
    }
}
