// Named glass preset library for the Light & Volume tab: transmission, volume,
// and IOR values for believable glass. Hosts write the picked values into
// their editors so validation and apply paths stay unchanged.
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

internal sealed record GlassPreset(string Name, double Transmission, double Thickness, Vec3 AttenuationColor, double AttenuationDistance, double Ior, string Summary);

internal static class GlassPresets
{
    private static Vec3 C(int r, int g, int b) => new(r / 255.0, g / 255.0, b / 255.0);

    public static readonly IReadOnlyList<GlassPreset> All = new GlassPreset[]
    {
        new("Clear", 0.90, 0.01, C(255, 255, 255), 2.0, 1.50, "Window-glass clarity; pair with a Gloss finish."),
        new("Low-Iron", 0.95, 0.02, C(244, 255, 255), 5.0, 1.52, "Extra-clear display glass."),
        new("Frosted", 0.60, 0.05, C(232, 240, 242), 0.5, 1.50, "Privacy glass; pair with a Satin finish."),
        new("Smoked", 0.50, 0.10, C(58, 63, 68), 0.3, 1.50, "Dark tinted glass."),
        new("Amber", 0.70, 0.08, C(217, 123, 31), 0.4, 1.50, "Warm bottle glass."),
        new("Sea Green", 0.75, 0.10, C(95, 191, 143), 0.5, 1.50, "Vintage green glass."),
    };
}
