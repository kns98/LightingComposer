// File: Rendering/Compute/PreparedComputeSceneBuilder.cs
// Purpose: Builds a fresh prepared GPU scene without consulting the cache.

using System.Runtime.InteropServices;
using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class PreparedComputeSceneBuilder
{
    private readonly ComputeStageLog log;

    public PreparedComputeSceneBuilder(ComputeStageLog log)
    {
        this.log = log;
    }

    /// <summary>Builds a fresh prepared GPU scene without consulting the cache.</summary>
    public PreparedComputeScene Build(GraphicsDevice gd, Scene scene, CancellationToken token)
    {
        log.Stage("Build cached texture metadata");
        ComputeTextureBuild textureBuild = new ComputeTextureBuilder().Build(scene);
        log.Stage($"Texture metadata built: textures={textureBuild.TextureCount}, pixels={textureBuild.PixelCount}, infos={textureBuild.Infos.Length}");

        int sourceTriangleCount = scene.Triangles.Count;
        if (sourceTriangleCount == 0)
            throw new InvalidOperationException("Vulkan scene render requires at least one triangle.");

        // The BVH keeps only an integer triangle-order array and compact CPU
        // nodes. GPU records are generated directly into small upload chunks.
        log.Stage("Build cached traversal BVH");
        BvhBuildResult bvh = new BvhBuilder(log).Build(scene);
        GpuLight[] lights = new ComputeLightBufferBuilder().Build(scene, out int sourceLightCount);

        // Keep all scene triangles unless the buffer byte-size check overflows.
        ComputeSceneBufferFactory bufferFactory = new();
        ComputeSceneBufferPlan plan = bufferFactory.Plan(
            sourceTriangleCount, bvh.Nodes.Length, lights.Length,
            textureBuild.PixelCount, textureBuild.Infos.Length);
        log.Stage($"Create cached Vulkan compute scene: {plan.TotalBytes / (1024.0 * 1024.0):F1} MB, triangles={sourceTriangleCount}, BVH nodes={bvh.Nodes.Length}, textures={textureBuild.TextureCount}");
        ComputeSceneBuffers buffers = bufferFactory.Create(gd.ResourceFactory, plan);
        int bvhStride = Marshal.SizeOf<GpuBvhNode>();

        log.ThrowIfCancellationRequested(token, "upload cached Vulkan triangle buffer");
        new GpuTriangleUploader(gd, log).Upload(buffers.Triangles, scene.Triangles, bvh.TriangleIndices, textureBuild.TextureIds, token);
        bvh.TriangleIndices = Array.Empty<int>();
        textureBuild.TextureIds.Clear();

        new BvhNodeUploader(gd, log).Upload(buffers.BvhNodes, bvh.Nodes, bvhStride, token);
        int bvhNodeCount = bvh.Nodes.Length;
        bvh.Nodes = Array.Empty<CpuBvhNode>();

        gd.UpdateBuffer(buffers.Lights, 0, lights.Length == 0 ? new[] { default(GpuLight) } : lights);
        new TexturePixelUploader(gd, log).Upload(buffers.TexturePixels, textureBuild, token);
        gd.UpdateBuffer(buffers.TextureInfos, 0, textureBuild.Infos.Length == 0 ? new[] { new GpuTextureInfo(0, 1, 1) } : textureBuild.Infos);
        textureBuild.Infos = Array.Empty<GpuTextureInfo>();

        PreparedComputeScene built = new()
        {
            Scene = scene,
            CacheStamp = SceneCacheStamp.Capture(scene),
            Buffers = buffers,
            SourceTriangleCount = sourceTriangleCount,
            TriangleCount = sourceTriangleCount,
            BvhNodeCount = bvhNodeCount,
            SourceLightCount = sourceLightCount,
            LightCount = lights.Length,
            TextureCount = textureBuild.TextureCount,
            SceneBufferBytes = plan.TotalBytes
        };
        log.Memory("after cached Vulkan scene upload");
        return built;
    }
}
