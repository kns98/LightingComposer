// File: Rendering/Compute/ComputeSceneBufferFactory.cs
// Purpose: Creates sized triangle, BVH, light, and texture GPU buffers.

using System.Runtime.InteropServices;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed record ComputeSceneBufferPlan(uint Triangles, uint BvhNodes, uint Lights, uint TexturePixels, uint TextureInfos)
{
    public long TotalBytes => (long)Triangles + BvhNodes + Lights + TexturePixels + TextureInfos;
}

internal sealed class ComputeSceneBufferFactory
{
    /// <summary>Validates the scene buffer sizes against the Vulkan buffer limit.</summary>
    public ComputeSceneBufferPlan Plan(
        int triangleCount, int bvhNodeCount, int lightCount, long texturePixelCount, int textureInfoCount)
    {
        int triangleStride = Marshal.SizeOf<GpuTriangle>();
        int bvhStride = Marshal.SizeOf<GpuBvhNode>();
        int lightStride = Marshal.SizeOf<GpuLight>();
        int textureInfoStride = Marshal.SizeOf<GpuTextureInfo>();

        return new ComputeSceneBufferPlan(
            CheckedSceneBufferSize((long)triangleCount * triangleStride, triangleStride, "triangle"),
            CheckedSceneBufferSize((long)bvhNodeCount * bvhStride, bvhStride, "BVH"),
            CheckedSceneBufferSize((long)lightCount * lightStride, lightStride, "light"),
            CheckedSceneBufferSize(texturePixelCount * sizeof(uint), sizeof(uint), "texture pixel"),
            CheckedSceneBufferSize((long)textureInfoCount * textureInfoStride, textureInfoStride, "texture info"));
    }

    /// <summary>Creates the triangle, BVH, light, and texture buffers from a validated plan.</summary>
    public ComputeSceneBuffers Create(ResourceFactory factory, ComputeSceneBufferPlan plan)
    {
        int triangleStride = Marshal.SizeOf<GpuTriangle>();
        int bvhStride = Marshal.SizeOf<GpuBvhNode>();
        int lightStride = Marshal.SizeOf<GpuLight>();
        int textureInfoStride = Marshal.SizeOf<GpuTextureInfo>();

        using GpuResourceScope scope = new();
        ComputeSceneBuffers buffers = new()
        {
            Triangles = scope.Track(factory.CreateBuffer(new BufferDescription(plan.Triangles, BufferUsage.StructuredBufferReadOnly, (uint)triangleStride))),
            BvhNodes = scope.Track(factory.CreateBuffer(new BufferDescription(plan.BvhNodes, BufferUsage.StructuredBufferReadOnly, (uint)bvhStride))),
            Lights = scope.Track(factory.CreateBuffer(new BufferDescription(plan.Lights, BufferUsage.StructuredBufferReadOnly, (uint)lightStride))),
            TexturePixels = scope.Track(factory.CreateBuffer(new BufferDescription(plan.TexturePixels, BufferUsage.StructuredBufferReadOnly, sizeof(uint)))),
            TextureInfos = scope.Track(factory.CreateBuffer(new BufferDescription(plan.TextureInfos, BufferUsage.StructuredBufferReadOnly, (uint)textureInfoStride)))
        };
        scope.Commit();
        return buffers;
    }

    /// <summary>Validates a scene buffer size against the Vulkan buffer limit.</summary>
    private static uint CheckedSceneBufferSize(long requestedBytes, int minimumBytes, string label)
    {
        long bytes = Math.Max(minimumBytes, requestedBytes);
        if (bytes > uint.MaxValue)
            throw new InvalidOperationException($"The Vulkan compute {label} buffer would require {bytes / (1024.0 * 1024.0):F0} MB, exceeding the 4 GB Vulkan buffer limit.");
        return checked((uint)bytes);
    }
}
