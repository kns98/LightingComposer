// Named alpha preset library for the Surface tab: alpha mode/opacity/cutoff
// triples for opaque, cutout, and fading looks. Hosts write the picked values
// into their alpha editors so validation and apply paths stay unchanged.
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record AlphaPreset(string Name, MaterialAlphaMode AlphaMode, double Alpha, double AlphaCutoff, string Summary);

internal static class AlphaPresets
{
    public static readonly IReadOnlyList<AlphaPreset> All = new AlphaPreset[]
    {
        new("Opaque", MaterialAlphaMode.Opaque, 1.0, 0.5, "Fully solid surface."),
        new("Cutout Hard", MaterialAlphaMode.Mask, 1.0, 0.5, "Crisp cutout for grilles and foliage cards."),
        new("Cutout Soft", MaterialAlphaMode.Mask, 1.0, 0.3, "Keeps more semi-transparent edge pixels."),
        new("Fade", MaterialAlphaMode.Blend, 0.6, 0.5, "Smooth partial fade."),
        new("Ghost", MaterialAlphaMode.Blend, 0.25, 0.5, "Barely-there holographic look."),
        new("Glass Fade", MaterialAlphaMode.Blend, 0.35, 0.5, "Tinted-glass style fade; pair with transmission."),
    };
}
