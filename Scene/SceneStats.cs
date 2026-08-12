/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 *
 * `SceneStats` is an immutable packet of related values. Record value semantics make it suitable for snapshots,
 * options, commands, or parsed intermediate data because callers can copy/compare it without sharing mutable
 * state. Its constructor values (`ObjectCount`, `TriangleCount`, `LightCount`) travel together because consumers
 * need a consistent snapshot rather than reading those values independently from mutable objects.
 *
 * `ToString` returns the human-facing label/name for this value so Avalonia controls display meaningful text
 * instead of the generated record/type representation.
 */
namespace LightingShowcase.SceneGraph;

/// <summary>Compact summary of scene size for status display.</summary>
public readonly record struct SceneStats(int ObjectCount, int TriangleCount, int LightCount)
{
    public override string ToString()
        => $"{ObjectCount:N0} objects, {TriangleCount:N0} triangles, {LightCount:N0} lights";
}
