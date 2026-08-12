/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
namespace LightingShowcase.Composer;

// ComposerRendererKind makes a closed set of choices compiler-visible instead of passing loosely related integers
// or strings. Code that switches over Raster, VulkanRaster, VulkanCompute, Cpu is where the behavioral meaning of
// each choice is implemented.
internal enum ComposerRendererKind
{
    Raster,
    VulkanRaster,
    VulkanCompute,
    Cpu
}
