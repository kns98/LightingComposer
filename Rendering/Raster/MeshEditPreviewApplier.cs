// File: Rendering/Raster/MeshEditPreviewApplier.cs
// Purpose: Patches and restores GPU vertices during a mesh-edit drag.

using System.Runtime.InteropServices;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class MeshEditPreviewApplier
{
    private readonly GraphicsDevice gd;
    private readonly RasterStageLog log;

    public MeshEditPreviewApplier(GraphicsDevice gd, RasterStageLog log)
    {
        this.gd = gd;
        this.log = log;
    }

    /// <summary>Applies the active mesh edit delta to affected GPU vertices, or restores them when idle.</summary>
    public void Apply(PreparedRasterScene prepared, VulkanRasterMeshEditPreview? preview, CancellationToken token)
    {
        if (preview == null || preview.IsIdentity)
        {
            Restore(prepared);
            return;
        }

        MeshEditPatchState? active = prepared.ActiveMeshEdit;
        if (active == null || active.SelectionId != preview.SelectionId || active.GroupId != preview.GroupId)
        {
            Restore(prepared);
            active = new MeshEditPatchBuilder().Build(prepared, preview);
            prepared.ActiveMeshEdit = active;
        }

        int patchIndex = 0;
        foreach (RasterTrianglePatch patch in active.Patches)
        {
            token.ThrowIfCancellationRequested();
            Triangle source = patch.Source;
            Vec3 a = (patch.CornerMask & 1) != 0 ? source.A + preview.WorldDelta : source.A;
            Vec3 b = (patch.CornerMask & 2) != 0 ? source.B + preview.WorldDelta : source.B;
            Vec3 c = (patch.CornerMask & 4) != 0 ? source.C + preview.WorldDelta : source.C;
            Triangle moved = new(a, b, c, source.UvA, source.UvB, source.UvC, source.Material, source.GroupId);
            int materialIndex = prepared.MaterialIds[source.Material];
            RasterVertex[] vertices =
            [
                new RasterVertex(moved.A, moved.NormalA, moved.UvA, materialIndex),
                new RasterVertex(moved.B, moved.NormalB, moved.UvB, materialIndex),
                new RasterVertex(moved.C, moved.NormalC, moved.UvC, materialIndex)
            ];
            Upload(prepared, patch, vertices);

            patchIndex++;
            if ((patchIndex & 63) == 0)
                log.ThrowIfCancellationRequested(token, "upload Vulkan mesh edit preview");
        }
    }

    /// <summary>Restores previously patched GPU vertices to their original values.</summary>
    public void Restore(PreparedRasterScene prepared)
    {
        MeshEditPatchState? active = prepared.ActiveMeshEdit;
        if (active == null)
            return;

        foreach (RasterTrianglePatch patch in active.Patches)
            Upload(prepared, patch, patch.OriginalVertices);
        prepared.ActiveMeshEdit = null;
    }

    /// <summary>Uploads three replacement vertices into the opaque or transparent buffer.</summary>
    private void Upload(PreparedRasterScene prepared, RasterTrianglePatch patch, RasterVertex[] vertices)
    {
        DeviceBuffer destination = patch.Transparent ? prepared.Buffers.Transparent : prepared.Buffers.Opaque;
        uint byteOffset = checked(patch.VertexStart * (uint)Marshal.SizeOf<RasterVertex>());
        gd.UpdateBuffer(destination, byteOffset, vertices);
    }
}
