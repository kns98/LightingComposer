// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerRendererKind.cs
// Purpose: Renderer backend selection.
//
// Names the four preview backends: software raster, Vulkan raster, Vulkan compute, and CPU ray rendering.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Composer;

internal enum ComposerRendererKind
{
    Raster,
    VulkanRaster,
    VulkanCompute,
    Cpu
}
