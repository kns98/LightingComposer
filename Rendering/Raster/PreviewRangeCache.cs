// File: Rendering/Raster/PreviewRangeCache.cs
// Purpose: Cached per-selection vertex ranges for transform previews.

namespace LightingShowcase.Rendering;

internal sealed class PreviewRangeCache
{
    private readonly Dictionary<int, PreviewRangeSet> cached = new();

    /// <summary>Returns cached vertex ranges for a transform preview selection, building them on first use.</summary>
    public PreviewRangeSet GetOrCreate(VulkanRasterTransformPreview preview, IReadOnlyDictionary<int, PreviewRangeSet> groupRanges)
    {
        if (cached.TryGetValue(preview.SelectionId, out PreviewRangeSet? hit))
            return hit;

        List<VertexRange> opaque = new();
        List<VertexRange> transparent = new();
        foreach (int groupId in preview.GroupIds)
        {
            if (!groupRanges.TryGetValue(groupId, out PreviewRangeSet? group))
                continue;
            opaque.AddRange(group.Opaque);
            transparent.AddRange(group.Transparent);
        }

        PreviewRangeSet result = new(VertexRangeList.Merge(opaque), VertexRangeList.Merge(transparent));
        cached[preview.SelectionId] = result;
        return result;
    }

    public void Clear() => cached.Clear();
}
