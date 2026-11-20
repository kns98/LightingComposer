// File: Rendering/Raster/MeshEditPatchBuilder.cs
// Purpose: Locates GPU vertices affected by a mesh-edit preview.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class MeshEditPatchBuilder
{
    /// <summary>Locates the GPU vertex ranges and original vertices affected by a mesh edit preview.</summary>
    public MeshEditPatchState Build(PreparedRasterScene prepared, VulkanRasterMeshEditPreview preview)
    {
        Dictionary<int, byte> editByTriangle = new();
        foreach (VulkanRasterMeshTriangleEdit edit in preview.TriangleEdits)
        {
            if (edit.TriangleIndex >= 0 && edit.CornerMask != 0)
                editByTriangle[edit.TriangleIndex] = edit.CornerMask;
        }

        List<RasterTrianglePatch> patches = new(editByTriangle.Count);
        uint opaqueCursor = 0;
        uint transparentCursor = 0;
        int localTriangleIndex = 0;
        foreach (Triangle triangle in prepared.Scene.Triangles)
        {
            bool targetGroup = triangle.GroupId == preview.GroupId;
            int targetTriangleIndex = targetGroup ? localTriangleIndex++ : -1;
            if (!TriangleCuller.IsFinite(triangle.A) || !TriangleCuller.IsFinite(triangle.B) ||
                !TriangleCuller.IsFinite(triangle.C) || !TriangleCuller.IsFinite(triangle.Normal))
                continue;

            bool transparent = MaterialPacker.IsTransparent(triangle.Material);
            uint vertexStart = transparent ? transparentCursor : opaqueCursor;
            if (targetTriangleIndex >= 0 && editByTriangle.TryGetValue(targetTriangleIndex, out byte cornerMask))
            {
                int materialIndex = prepared.MaterialIds[triangle.Material];
                RasterVertex[] original =
                [
                    new RasterVertex(triangle.A, triangle.NormalA, triangle.UvA, materialIndex),
                    new RasterVertex(triangle.B, triangle.NormalB, triangle.UvB, materialIndex),
                    new RasterVertex(triangle.C, triangle.NormalC, triangle.UvC, materialIndex)
                ];
                patches.Add(new RasterTrianglePatch(transparent, vertexStart, triangle, cornerMask, original));
            }

            if (transparent)
                transparentCursor += 3;
            else
                opaqueCursor += 3;
        }

        return new MeshEditPatchState
        {
            SelectionId = preview.SelectionId,
            GroupId = preview.GroupId,
            Patches = patches
        };
    }
}
