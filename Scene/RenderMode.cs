/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
namespace LightingShowcase.SceneGraph;

// RenderMode makes a closed set of choices compiler-visible instead of passing loosely related integers or strings.
// Code that switches over Lit, Unlit, NormalDebug, UvDebug, MaterialDebug, LightDebug, Wireframe, BoundingBox,
// Depth is where the behavioral meaning of each choice is implemented.
/// <summary>High-level render/debug mode shared by preview and final render pipelines.</summary>
public enum RenderMode
{
    Lit,
    Unlit,
    NormalDebug,
    UvDebug,
    MaterialDebug,
    LightDebug,
    Wireframe,
    BoundingBox,
    Depth
}
