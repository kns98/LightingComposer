/*
 * The Vulkan path makes resource ownership and cache validity explicit. CPU-side scene data is packed into GPU
 * buffers/images, commands are submitted against those resources, and stale resources must be rebuilt when
 * geometry or transforms change; a numerically correct algorithm can still be wrong here if lifetime or
 * synchronization is mishandled.
 *
 * `VulkanRasterMeshTriangleEdit` is an immutable packet of related values. Record value semantics make it
 * suitable for snapshots, options, commands, or parsed intermediate data because callers can copy/compare it
 * without sharing mutable state. Its constructor values (`TriangleIndex`, `CornerMask`) travel together because
 * consumers need a consistent snapshot rather than reading those values independently from mutable objects.
 *
 * `VulkanRasterMeshEditPreview` holds transient data used only while an interactive edit is in progress;
 * committing/cancelling must either promote or discard it cleanly.
 *
 * `IsIdentity` is derived rather than separately stored: it evaluates `TriangleEdits.Count == 0 ||
 * WorldDelta.Length() <= 1e-12`. Keeping the value computed from its source fields prevents a second cached
 * flag/value from drifting out of sync.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

/// <summary>
/// One triangle affected by a transient mesh-component move. CornerMask uses
/// bit 0 for A, bit 1 for B, and bit 2 for C.
/// </summary>
public readonly record struct VulkanRasterMeshTriangleEdit(int TriangleIndex, byte CornerMask);

/// <summary>
/// Transient component deformation used by the Vulkan raster editor preview.
/// The renderer patches only affected triangle vertices in its already allocated
/// GPU buffers; the scene and undo history are changed only when the drag ends.
/// </summary>
public sealed class VulkanRasterMeshEditPreview
{
    public VulkanRasterMeshEditPreview(
        int selectionId,
        int groupId,
        IEnumerable<VulkanRasterMeshTriangleEdit> triangleEdits,
        Vec3 worldDelta)
    {
        SelectionId = selectionId;
        GroupId = groupId;
        TriangleEdits = triangleEdits?.Where(edit => edit.CornerMask != 0).ToArray()
            ?? throw new ArgumentNullException(nameof(triangleEdits));
        WorldDelta = worldDelta;
    }

    public int SelectionId { get; }
    public int GroupId { get; }
    public IReadOnlyList<VulkanRasterMeshTriangleEdit> TriangleEdits { get; }
    public Vec3 WorldDelta { get; }

    public bool IsIdentity => TriangleEdits.Count == 0 || WorldDelta.Length() <= 1e-12;
}
