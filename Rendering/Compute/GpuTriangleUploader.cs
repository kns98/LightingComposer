// File: Rendering/Compute/GpuTriangleUploader.cs
// Purpose: Chunked triangle upload to the GPU in traversal order.

using System.Runtime.InteropServices;
using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class GpuTriangleUploader
{
    private const int TrianglesPerChunk = 2048;

    private readonly GraphicsDevice gd;
    private readonly ComputeStageLog log;

    public GpuTriangleUploader(GraphicsDevice gd, ComputeStageLog log)
    {
        this.gd = gd;
        this.log = log;
    }

    /// <summary>Uploads triangle records to the GPU in traversal order.</summary>
    public void Upload(
        DeviceBuffer destination, IReadOnlyList<Triangle> sourceTriangles,
        IReadOnlyList<int> orderedTriangleIndices, IReadOnlyDictionary<TextureMap, int> textureIds,
        CancellationToken token)
    {
        int stride = Marshal.SizeOf<GpuTriangle>();
        GpuTriangle[] chunk = new GpuTriangle[TrianglesPerChunk];
        int pending = 0;
        int uploaded = 0;

        for (int i = 0; i < orderedTriangleIndices.Count; i++)
        {
            chunk[pending++] = new GpuTriangle(sourceTriangles[orderedTriangleIndices[i]], textureIds);
            if (pending == chunk.Length)
            {
                gd.UpdateBuffer(destination, checked((uint)((long)uploaded * stride)), chunk);
                uploaded += pending;
                pending = 0;
                log.ThrowIfCancellationRequested(token, "upload cached Vulkan triangle buffer");
            }
        }

        if (pending > 0)
        {
            GpuTriangle[] tail = new GpuTriangle[pending];
            Array.Copy(chunk, tail, pending);
            gd.UpdateBuffer(destination, checked((uint)((long)uploaded * stride)), tail);
        }
    }
}
