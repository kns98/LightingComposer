// File: Rendering/Raster/FrameDetails.cs
// Purpose: Human-readable frame summary with cache state and phase timings.

namespace LightingShowcase.Rendering;

internal sealed record FrameTimings(
    long DeviceMs, long TargetsMs, long PrepareMs, long MeshEditUploadMs,
    long UniformMs, long RecordMs, long GpuWaitMs, long ReadbackMs);

internal static class FrameDetails
{
    public static string Build(FrameRequest request, PreparedRasterScene prepared, FrameTimings timings, long totalMs)
    {
        bool hasPreview = request.TransformPreview != null && !request.TransformPreview.IsIdentity && request.TransformPreview.GroupIds.Count > 0;
        bool hasMeshEdit = request.MeshEditPreview != null && !request.MeshEditPreview.IsIdentity;

        string textureMode = prepared.Stats.UsesGpuTextureSampling
            ? $"textures={prepared.Stats.TextureCount}, pages={prepared.Stats.TexturePageCount}"
            : prepared.Stats.GpuTextureSamplingRequested && !string.IsNullOrWhiteSpace(prepared.Stats.TextureFallbackReason)
                ? $"textures=base-color fallback ({prepared.Stats.TextureFallbackReason})"
                : "textures=base color";
        string triangleMode = prepared.Stats.NearClippedTriangleCount > 0
            ? $"triangles={prepared.Stats.TotalTriangleCount}/{prepared.Stats.SourceTriangleCount} ({prepared.Stats.NearClippedTriangleCount} invalid skipped)"
            : $"triangles={prepared.Stats.TotalTriangleCount}";
        string previewMode = hasPreview ? $", live-transform={request.TransformPreview!.SelectionId}" : string.Empty;
        if (hasMeshEdit)
            previewMode += $", live-mesh-edit={request.MeshEditPreview!.SelectionId}";
        return $"VULKAN RASTER PBR CACHED - {request.Width}x{request.Height}, revision={prepared.CacheStamp.Revision}, {triangleMode}, lights={prepared.Stats.LightCount}, {textureMode}, cache={(timings.PrepareMs == 0 ? "hot" : "ready")}{previewMode}, device={timings.DeviceMs}ms, targets={timings.TargetsMs}ms, prepare={timings.PrepareMs}ms, mesh-edit-upload={timings.MeshEditUploadMs}ms, uniform={timings.UniformMs}ms, record={timings.RecordMs}ms, gpu+wait={timings.GpuWaitMs}ms, readback={timings.ReadbackMs}ms, total={totalMs}ms" + (request.Wireframe ? ", wireframe" : string.Empty);
    }
}
