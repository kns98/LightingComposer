// File: Rendering/Raster/GeometryUploader.cs
// Purpose: Chunked scene-triangle upload into opaque and transparent vertex buffers.

using System.Runtime.InteropServices;
using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class GeometryUploader
{
    private const int TrianglesPerChunk = 2048;

    private readonly GraphicsDevice gd;
    private readonly RasterStageLog log;
    private readonly int vertexStride = Marshal.SizeOf<RasterVertex>();

    public GeometryUploader(GraphicsDevice gd, RasterStageLog log)
    {
        this.gd = gd;
        this.log = log;
    }

    /// <summary>Uploads scene triangles into the opaque and transparent vertex buffers in chunks.</summary>
    public void Upload(SceneGpuBuffers buffers, Scene scene, IReadOnlyDictionary<Material, int> materialIds, CancellationToken token)
    {
        // Keep upload memory constant while preserving the separate transparent
        // pass. Uploading every triangle into the opaque buffer and hard-coding
        // the transparent count to zero makes transmission and alpha-blended
        // materials depth-write as solid white.
        RasterVertex[] opaqueVertices = new RasterVertex[TrianglesPerChunk * 3];
        RasterVertex[] transparentVertices = new RasterVertex[TrianglesPerChunk * 3];
        int opaqueInChunk = 0;
        int transparentInChunk = 0;
        int uploadedOpaque = 0;
        int uploadedTransparent = 0;
        int processed = 0;

        foreach (Triangle triangle in scene.Triangles)
        {
            if (!TriangleCuller.IsFinite(triangle.A) || !TriangleCuller.IsFinite(triangle.B) ||
                !TriangleCuller.IsFinite(triangle.C) || !TriangleCuller.IsFinite(triangle.Normal))
                continue;

            int materialIndex = materialIds[triangle.Material];
            bool transparent = MaterialPacker.IsTransparent(triangle.Material);
            RasterVertex[] target = transparent ? transparentVertices : opaqueVertices;
            int vertexIndex = (transparent ? transparentInChunk : opaqueInChunk) * 3;
            target[vertexIndex] = new RasterVertex(triangle.A, triangle.NormalA, triangle.UvA, materialIndex);
            target[vertexIndex + 1] = new RasterVertex(triangle.B, triangle.NormalB, triangle.UvB, materialIndex);
            target[vertexIndex + 2] = new RasterVertex(triangle.C, triangle.NormalC, triangle.UvC, materialIndex);

            if (transparent)
            {
                transparentInChunk++;
                if (transparentInChunk == TrianglesPerChunk)
                {
                    UploadChunk(buffers.Transparent, transparentVertices, transparentInChunk, ref uploadedTransparent);
                    transparentInChunk = 0;
                }
            }
            else
            {
                opaqueInChunk++;
                if (opaqueInChunk == TrianglesPerChunk)
                {
                    UploadChunk(buffers.Opaque, opaqueVertices, opaqueInChunk, ref uploadedOpaque);
                    opaqueInChunk = 0;
                }
            }

            processed++;
            if (processed % (TrianglesPerChunk * 2) == 0)
                log.ThrowIfCancellationRequested(token, "upload Vulkan raster geometry");
        }

        if (opaqueInChunk > 0)
            UploadChunk(buffers.Opaque, opaqueVertices, opaqueInChunk, ref uploadedOpaque);
        if (transparentInChunk > 0)
            UploadChunk(buffers.Transparent, transparentVertices, transparentInChunk, ref uploadedTransparent);
    }

    /// <summary>Uploads one chunk of raster vertices at the running buffer offset.</summary>
    private void UploadChunk(DeviceBuffer destination, RasterVertex[] vertices, int triangleCount, ref int uploadedTriangles)
    {
        RasterVertex[] upload = vertices;
        if (triangleCount != TrianglesPerChunk)
        {
            upload = new RasterVertex[triangleCount * 3];
            Array.Copy(vertices, upload, upload.Length);
        }

        uint vertexOffset = checked((uint)((long)uploadedTriangles * 3L * vertexStride));
        gd.UpdateBuffer(destination, vertexOffset, upload);
        uploadedTriangles += triangleCount;
    }
}
