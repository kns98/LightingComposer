// File: Rendering/Raster/GroupRangeBuilder.cs
// Purpose: Per-group opaque and transparent vertex ranges for preview draws.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class GroupRangeBuilder
{
    /// <summary>Builds per-group opaque and transparent vertex ranges for the scene.</summary>
    public IReadOnlyDictionary<int, PreviewRangeSet> Build(Scene scene)
    {
        Dictionary<int, (VertexRangeList Opaque, VertexRangeList Transparent)> builders = new();
        uint opaqueCursor = 0;
        uint transparentCursor = 0;

        foreach (Triangle triangle in scene.Triangles)
        {
            if (!TriangleCuller.IsFinite(triangle.A) || !TriangleCuller.IsFinite(triangle.B) ||
                !TriangleCuller.IsFinite(triangle.C) || !TriangleCuller.IsFinite(triangle.Normal))
                continue;

            bool isTransparent = MaterialPacker.IsTransparent(triangle.Material);
            uint start = isTransparent ? transparentCursor : opaqueCursor;
            if (triangle.GroupId >= 0)
            {
                if (!builders.TryGetValue(triangle.GroupId, out var builder))
                {
                    builder = (new VertexRangeList(), new VertexRangeList());
                    builders.Add(triangle.GroupId, builder);
                }
                (isTransparent ? builder.Transparent : builder.Opaque).Add(start, 3);
            }

            if (isTransparent)
                transparentCursor += 3;
            else
                opaqueCursor += 3;
        }

        return builders.ToDictionary(
            pair => pair.Key,
            pair => new PreviewRangeSet(pair.Value.Opaque.Ranges, pair.Value.Transparent.Ranges));
    }
}
