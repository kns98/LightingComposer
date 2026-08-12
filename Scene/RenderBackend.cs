/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
namespace LightingShowcase.SceneGraph;

// RenderBackend makes a closed set of choices compiler-visible instead of passing loosely related integers or
// strings. Code that switches over triangle, z, direct, and, then is where the behavioral meaning of each choice is
// implemented.
/// <summary>Render backend preference selected from the Render pane.</summary>
public enum RenderBackend
{
    /// <summary>Uses the CPU ray/path tracing renderer for still/final renders.</summary>
    Cpu = 0,

    /// <summary>Uses the Vulkan compute ray/path tracing renderer for still/final renders.</summary>
    VulkanGpu = 1,

    /// <summary>Uses the Vulkan compute renderer in small diagnostic batches.</summary>
    VulkanDiagnostic = 2,

    /// <summary>
    /// Uses Lighting Showcase's own software raster preview pipeline: camera
    /// projection, triangle rasterization, z-buffering, direct lighting, and
    /// shadow-map-style shadows. This is the fast AMD-style preview backend.
    /// </summary>
    ShadowRasterPreview = 3,

    /// <summary>
    /// Uses Vulkan's graphics pipeline for hardware triangle rasterization into
    /// an off-screen image, then presents the result in the composer viewport.
    /// This is separate from the Vulkan compute path tracer.
    /// </summary>
    VulkanRasterPreview = 4
}
