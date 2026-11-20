// File: Rendering/Raster/Gpu/PreviewRangeSet.cs
// Purpose: Opaque and transparent vertex ranges drawn with the preview transform.

namespace LightingShowcase.Rendering;

internal sealed record PreviewRangeSet(
    IReadOnlyList<VertexRange> Opaque,
    IReadOnlyList<VertexRange> Transparent);
