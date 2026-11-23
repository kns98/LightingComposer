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
        return new ComputeSceneBufferPlan(
            CheckedSize((long)triangleCount * Marshal.SizeOf<GpuTriangle>(), Marshal.SizeOf<GpuTriangle>(), "triangle"),
            CheckedSize((long)bvhNodeCount * Marshal.SizeOf<GpuBvhNode>(), Marshal.SizeOf<GpuBvhNode>(), "BVH"),
            CheckedSize((long)lightCount * Marshal.SizeOf<GpuLight>(), Marshal.SizeOf<GpuLight>(), "light"),
            CheckedSize(texturePixelCount * sizeof(uint), sizeof(uint), "texture pixel"),
            CheckedSize((long)textureInfoCount * Marshal.SizeOf<GpuTextureInfo>(), Marshal.SizeOf<GpuTextureInfo>(), "texture info"));
    }

    /// <summary>Creates the triangle, BVH, light, and texture buffers from a validated plan.</summary>
    public ComputeSceneBuffers Create(ResourceFactory factory, ComputeSceneBufferPlan plan)
    {
        using GpuResourceScope scope = new();
        ComputeSceneBuffers buffers = new()
        {
            Triangles = scope.Track(factory.CreateBuffer(new BufferDescription(plan.Triangles, BufferUsage.StructuredBufferReadOnly, (uint)Marshal.SizeOf<GpuTriangle>()))),
            BvhNodes = scope.Track(factory.CreateBuffer(new BufferDescription(plan.BvhNodes, BufferUsage.StructuredBufferReadOnly, (uint)Marshal.SizeOf<GpuBvhNode>()))),
            Lights = scope.Track(factory.CreateBuffer(new BufferDescription(plan.Lights, BufferUsage.StructuredBufferReadOnly, (uint)Marshal.SizeOf<GpuLight>()))),
            TexturePixels = scope.Track(factory.CreateBuffer(new BufferDescription(plan.TexturePixels, BufferUsage.StructuredBufferReadOnly, sizeof(uint)))),
            TextureInfos = scope.Track(factory.CreateBuffer(new BufferDescription(plan.TextureInfos, BufferUsage.StructuredBufferReadOnly, (uint)Marshal.SizeOf<GpuTextureInfo>())))
        };
        scope.Commit();
        return buffers;
    }

    private static uint CheckedSize(long requestedBytes, int minimumBytes, string label)
    {
        long bytes = Math.Max(minimumBytes, requestedBytes);
        if (bytes > uint.MaxValue)
            throw new InvalidOperationException($"The Vulkan compute {label} buffer would require {bytes / (1024.0 * 1024.0):F0} MB, exceeding the 4 GB Vulkan buffer limit.");
        return checked((uint)bytes);
    }
}
