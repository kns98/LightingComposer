// File: Rendering/Raster/GeometryMetadataBuilder.cs
// Purpose: Triangle census plus material ids and GPU materials for scene upload.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class GeometryMetadataBuilder
{
    private readonly IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements;
    private readonly bool useGpuTextureSampling;

    public GeometryMetadataBuilder(IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements, bool useGpuTextureSampling)
    {
        this.texturePlacements = texturePlacements;
        this.useGpuTextureSampling = useGpuTextureSampling;
    }

    /// <summary>Counts opaque and transparent triangles and assigns material ids with texture placements.</summary>
    public RasterGeometryBuildResult Build(Scene scene)
    {
        int opaqueTriangleCount = 0;
        int transparentTriangleCount = 0;
        int nearClippedTriangles = 0;
        Dictionary<Material, int> materialIds = new();
        List<RasterMaterial> materials = new();

        foreach (Triangle triangle in scene.Triangles)
        {
            if (!TriangleCuller.IsFinite(triangle.A) || !TriangleCuller.IsFinite(triangle.B) ||
                !TriangleCuller.IsFinite(triangle.C) || !TriangleCuller.IsFinite(triangle.Normal))
            {
                nearClippedTriangles++;
                continue;
            }

            if (MaterialPacker.IsTransparent(triangle.Material))
                transparentTriangleCount++;
            else
                opaqueTriangleCount++;

            if (!materialIds.ContainsKey(triangle.Material))
            {
                materialIds.Add(triangle.Material, materials.Count);
                materials.Add(new RasterMaterial(
                    triangle.Material,
                    Lookup(triangle.Material.Texture),
                    Lookup(triangle.Material.MetallicRoughnessTexture),
                    Lookup(triangle.Material.NormalTexture),
                    Lookup(triangle.Material.EmissiveTexture),
                    Lookup(triangle.Material.OcclusionTexture),
                    Lookup(triangle.Material.TransmissionTexture)));
            }
        }

        return new RasterGeometryBuildResult
        {
            MaterialIds = materialIds,
            Materials = materials.ToArray(),
            OpaqueTriangleCount = opaqueTriangleCount,
            TransparentTriangleCount = transparentTriangleCount,
            NearClippedTriangles = nearClippedTriangles,
            SourceTriangleCount = scene.Triangles.Count
        };
    }

    private RasterTexturePlacement Lookup(TextureMap? texture)
    {
        if (!useGpuTextureSampling || texture == null)
            return RasterTexturePlacement.None;
        return texturePlacements.TryGetValue(texture, out RasterTexturePlacement placement) ? placement : RasterTexturePlacement.None;
    }
}
