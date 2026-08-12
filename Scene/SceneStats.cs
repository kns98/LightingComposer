/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
namespace LightingShowcase.SceneGraph;

/// <summary>Compact summary of scene size for status display.</summary>
public readonly record struct SceneStats(int ObjectCount, int TriangleCount, int LightCount)
{
    // ToString returns the human-facing label/name for this value so Avalonia controls display meaningful text
    // instead of the generated record/type representation.
    public override string ToString()
        => $"{ObjectCount:N0} objects, {TriangleCount:N0} triangles, {LightCount:N0} lights";
}
