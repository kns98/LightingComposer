// File: Rendering/Raster/PreparedSceneBuilder.cs
// Purpose: Builds a fresh prepared GPU scene without consulting the cache.

using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class PreparedSceneBuilder
{
    private readonly RasterStageLog log;

    public PreparedSceneBuilder(RasterStageLog log)
    {
        this.log = log;
    }

    /// <summary>Builds a fresh prepared GPU scene without consulting the cache.</summary>
    public PreparedRasterScene Build(GraphicsDevice gd, ResourceLayout layout, Scene scene, CancellationToken token, bool gpuTextureSamplingRequested)
    {
        // Build only lightweight atlas placement metadata first. Pixel data is
        // transferred one texture at a time after the Vulkan texture exists, so
        // a full CPU-side atlas never coexists with the GPU atlas.
        TextureBuildResult textures = new TextureAtlasBuilder(log).Build(gd, scene, gpuTextureSamplingRequested);
        RasterGeometryBuildResult geometry = new GeometryMetadataBuilder(textures.TexturePlacements, textures.UsesGpuTextureSampling).Build(scene);
        IReadOnlyDictionary<int, PreviewRangeSet> groupRanges = new GroupRangeBuilder().Build(scene);
        RasterLight[] lights = new LightBufferBuilder().Build(scene);

        SceneGpuResources gpu = new SceneBufferFactory().Create(gd.ResourceFactory, layout, geometry, lights.Length, textures);
        new GeometryUploader(gd, log).Upload(gpu.Buffers, scene, geometry.MaterialIds, token);
        gd.UpdateBuffer(gpu.Buffers.Material, 0, geometry.Materials.Length == 0 ? new[] { default(RasterMaterial) } : geometry.Materials);
        geometry.Materials = Array.Empty<RasterMaterial>();
        gd.UpdateBuffer(gpu.Buffers.Light, 0, lights.Length == 0 ? new[] { default(RasterLight) } : lights);
        gd.UpdateBuffer(gpu.Buffers.IdentityTransform, 0, new RasterTransformConstants(preview: null));
        gd.UpdateBuffer(gpu.Buffers.PreviewTransform, 0, new RasterTransformConstants(preview: null));
        new TextureAtlasUploader(gd, log).Upload(gpu.Atlas, textures, token);

        SceneBounds bounds = SceneBounds.Compute(scene);
        return new PreparedRasterScene
        {
            Scene = scene,
            CacheStamp = SceneCacheStamp.Capture(scene),
            MaterialIds = geometry.MaterialIds,
            Buffers = gpu.Buffers,
            Atlas = gpu.Atlas,
            Descriptors = gpu.Descriptors,
            GroupRanges = groupRanges,
            Stats = new PreparedSceneStats
            {
                LightCount = lights.Length,
                OpaqueVertexCount = geometry.OpaqueVertexCount,
                TransparentVertexCount = geometry.TransparentVertexCount,
                MaterialCount = geometry.MaterialCount,
                SourceTriangleCount = geometry.SourceTriangleCount,
                TotalTriangleCount = geometry.TotalTriangles,
                NearClippedTriangleCount = geometry.NearClippedTriangles,
                TextureCount = textures.TextureCount,
                TexturePageCount = textures.PageCount,
                GpuTextureSamplingRequested = gpuTextureSamplingRequested,
                UsesGpuTextureSampling = textures.UsesGpuTextureSampling,
                TextureFallbackReason = textures.FallbackReason,
                BoundingCenter = bounds.Center,
                BoundingRadius = bounds.Radius
            }
        };
    }
}
