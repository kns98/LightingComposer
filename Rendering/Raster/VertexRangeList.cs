// File: Rendering/Raster/VertexRangeList.cs
// Purpose: Vertex span collection that merges overlapping neighbors on append.

namespace LightingShowcase.Rendering;

internal sealed class VertexRangeList
{
    private readonly List<VertexRange> ranges = new();

    public IReadOnlyList<VertexRange> Ranges => ranges;

    /// <summary>Appends a vertex range, merging it into the previous range when overlapping.</summary>
    public void Add(uint start, uint count)
    {
        if (count == 0)
            return;
        if (ranges.Count > 0)
        {
            VertexRange previous = ranges[^1];
            uint previousEnd = previous.Start + previous.Count;
            if (start <= previousEnd)
            {
                uint end = Math.Max(previousEnd, start + count);
                ranges[^1] = new VertexRange(previous.Start, end - previous.Start);
                return;
            }
        }
        ranges.Add(new VertexRange(start, count));
    }

    public void AddRange(IEnumerable<VertexRange> items)
    {
        foreach (VertexRange range in items)
            Add(range.Start, range.Count);
    }

    /// <summary>Sorts vertex ranges and merges overlapping neighbors into a new list.</summary>
    public static IReadOnlyList<VertexRange> Merge(List<VertexRange> items)
    {
        if (items.Count <= 1)
            return items;

        items.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        VertexRangeList merged = new();
        merged.AddRange(items);
        return merged.Ranges;
    }
}
