// File: Rendering/Raster/Gpu/RasterGeometryBuildResult.cs
// Purpose: Triangle counts, material ids, and GPU materials from the geometry scan.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class RasterGeometryBuildResult
{
    public required IReadOnlyDictionary<Material, int> MaterialIds { get; init; }
    public required RasterMaterial[] Materials { get; set; }
    public int OpaqueTriangleCount { get; init; }
    public int TransparentTriangleCount { get; init; }
    public int NearClippedTriangles { get; init; }
    public int SourceTriangleCount { get; init; }
    public int OpaqueVertexCount => checked(OpaqueTriangleCount * 3);
    public int TransparentVertexCount => checked(TransparentTriangleCount * 3);
    public int MaterialCount => Materials.Length;
    public int TotalTriangles => checked(OpaqueTriangleCount + TransparentTriangleCount);
}
