// File: Rendering/Compute/BvhNodeUploader.cs
// Purpose: Chunked BVH node upload with CPU-to-GPU conversion.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class BvhNodeUploader
{
    private const int NodesPerChunk = 4096;

    private readonly GraphicsDevice gd;
    private readonly ComputeStageLog log;

    public BvhNodeUploader(GraphicsDevice gd, ComputeStageLog log)
    {
        this.gd = gd;
        this.log = log;
    }

    /// <summary>Uploads bounding volume hierarchy nodes to the GPU.</summary>
    public void Upload(DeviceBuffer destination, CpuBvhNode[] source, int gpuStride, CancellationToken token)
    {
        if (source.Length == 0)
        {
            gd.UpdateBuffer(destination, 0, new[] { default(GpuBvhNode) });
            return;
        }

        GpuBvhNode[] chunk = new GpuBvhNode[Math.Min(NodesPerChunk, source.Length)];
        int offset = 0;
        while (offset < source.Length)
        {
            int count = Math.Min(chunk.Length, source.Length - offset);
            for (int i = 0; i < count; i++)
                chunk[i] = source[offset + i].ToGpu();

            GpuBvhNode[] upload = chunk;
            if (count != chunk.Length)
            {
                upload = new GpuBvhNode[count];
                Array.Copy(chunk, upload, count);
            }

            gd.UpdateBuffer(destination, checked((uint)((long)offset * gpuStride)), upload);
            offset += count;
            log.ThrowIfCancellationRequested(token, "upload cached Vulkan BVH buffer");
        }
    }
}
