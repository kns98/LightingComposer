// File: Rendering/Raster/Gpu/MeshEditPatchState.cs
// Purpose: Active set of GPU triangle patches for one mesh-edit drag.

namespace LightingShowcase.Rendering;

internal sealed class MeshEditPatchState
{
    public required int SelectionId { get; init; }
    public required int GroupId { get; init; }
    public required IReadOnlyList<RasterTrianglePatch> Patches { get; init; }
}
