using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

/// <summary>
/// Transient transform used by the Vulkan raster editor preview. It never
/// changes scene geometry or the scene revision; the final pointer release still
/// uses the normal scene bake and history path.
/// </summary>
public sealed class VulkanRasterTransformPreview
{
    public VulkanRasterTransformPreview(
        int selectionId,
        IEnumerable<int> groupIds,
        Vec3 pivot,
        Vec3 position,
        Vec3 rotation,
        Vec3 scale)
    {
        SelectionId = selectionId;
        GroupIds = groupIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(groupIds));
        Pivot = pivot;
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public int SelectionId { get; }
    public IReadOnlyList<int> GroupIds { get; }
    public Vec3 Pivot { get; }
    public Vec3 Position { get; }
    public Vec3 Rotation { get; }
    public Vec3 Scale { get; }

    public bool IsIdentity =>
        Position.Length() <= 1e-12 &&
        Rotation.Length() <= 1e-12 &&
        Math.Abs(Scale.X - 1.0) <= 1e-12 &&
        Math.Abs(Scale.Y - 1.0) <= 1e-12 &&
        Math.Abs(Scale.Z - 1.0) <= 1e-12;
}
