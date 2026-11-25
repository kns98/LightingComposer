// Named glow preset library for the Light & Volume tab: emission strength and
// color pairs. Hosts write the picked values into their emission editors so
// validation and apply paths stay unchanged.
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

internal sealed record GlowPreset(string Name, double Emission, Vec3 EmissionColor, string Summary);

internal static class GlowPresets
{
    private static Vec3 C(int r, int g, int b) => new(r / 255.0, g / 255.0, b / 255.0);

    public static readonly IReadOnlyList<GlowPreset> All = new GlowPreset[]
    {
        new("Off", 0.0, C(255, 255, 255), "No self-illumination."),
        new("Candle", 1.2, C(255, 154, 60), "Warm dim flame glow."),
        new("Warm LED", 3.0, C(255, 184, 92), "Warm white fixture glow."),
        new("Cool LED", 2.6, C(179, 217, 255), "Cool blue-white glow."),
        new("Daylight Panel", 1.8, C(255, 244, 224), "Soft ceiling-panel glow."),
        new("Neon Red", 4.2, C(255, 26, 10), "Strong red sign glow."),
        new("Neon Green", 4.0, C(57, 255, 20), "Strong green sign glow."),
        new("Neon Blue", 4.0, C(26, 77, 255), "Strong blue sign glow."),
    };
}
