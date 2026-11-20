// File: Rendering/Raster/Gpu/RasterTrianglePatch.cs
// Purpose: One GPU triangle rewritten by a mesh-edit preview, with its originals for restore.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal readonly record struct RasterTrianglePatch(
    bool Transparent,
    uint VertexStart,
    Triangle Source,
    byte CornerMask,
    RasterVertex[] OriginalVertices);
