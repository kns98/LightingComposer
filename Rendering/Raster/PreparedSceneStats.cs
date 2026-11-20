// File: Rendering/Raster/PreparedSceneStats.cs
// Purpose: Prepared scene counts, sampling flags, and bounding sphere.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal sealed class PreparedSceneStats
{
    public required int LightCount { get; init; }
    public required int OpaqueVertexCount { get; init; }
    public required int TransparentVertexCount { get; init; }
    public required int MaterialCount { get; init; }
    public required int SourceTriangleCount { get; init; }
    public required int TotalTriangleCount { get; init; }
    public required int NearClippedTriangleCount { get; init; }
    public required int TextureCount { get; init; }
    public required int TexturePageCount { get; init; }
    public required bool GpuTextureSamplingRequested { get; init; }
    public required bool UsesGpuTextureSampling { get; init; }
    public string? TextureFallbackReason { get; init; }
    public required double BoundingRadius { get; set; }
    public required Vec3 BoundingCenter { get; set; }
}
