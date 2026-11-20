// File: Rendering/Raster/Gpu/VertexRange.cs
// Purpose: Contiguous vertex span for partial buffer draws.

namespace LightingShowcase.Rendering;

internal readonly record struct VertexRange(uint Start, uint Count);
