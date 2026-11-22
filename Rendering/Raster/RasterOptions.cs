// File: Rendering/Raster/RasterOptions.cs
// Purpose: Environment-driven rasterizer settings (texture sampling, debug views).

namespace LightingShowcase.Rendering;

internal sealed class RasterOptions
{
    // Default to per-fragment GPU texture sampling so high-frequency glTF atlas
    // textures match the CPU shadow rasterizer. Set
    // LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES=1 only as a diagnostic fallback.
    public bool UseGpuTextureSampling =>
        !IsEnabled(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES"));

    public int RasterDebugMode => Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_RASTER_DEBUG")?.Trim().ToLowerInvariant() switch
    {
        "uv" => 1,
        "atlas" => 2,
        "texture" => 3,
        "material" => 4,
        "normal" => 5,
        "metallic" => 6,
        "roughness" => 7,
        "occlusion" => 8,
        "emissive" => 9,
        "direct" => 10,
        "ibl" => 11,
        "shadow" => 12,
        _ => 0
    };

    public bool IsVulkanDebugEnabled => IsEnabled(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_DEBUG"), "yes");

    public bool StageLoggingEnabled =>
        IsVulkanDebugEnabled || string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_STAGE_LOG"), "1", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnabled(string? value, string? extra = null) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        (extra != null && string.Equals(value, extra, StringComparison.OrdinalIgnoreCase));
}
