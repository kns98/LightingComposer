// Named finish preset library for the Surface tab: metallic/roughness pairs for
// common looks. Hosts write the picked values into their metallic/roughness
// editors so validation and apply paths stay unchanged.
namespace LightingShowcase.Composer;

internal sealed record FinishPreset(string Name, double Metallic, double Roughness, string Summary);

internal static class FinishPresets
{
    public static readonly IReadOnlyList<FinishPreset> All = new FinishPreset[]
    {
        new("Matte", 0.0, 0.90, "Flat non-reflective dielectric."),
        new("Eggshell", 0.0, 0.70, "Soft low-sheen wall-paint look."),
        new("Satin", 0.0, 0.45, "Smooth plastic or varnished-wood sheen."),
        new("Semi-Gloss", 0.0, 0.28, "Trim paint or polished stone."),
        new("Gloss", 0.0, 0.12, "Car-paint or lacquer shine."),
        new("Mirror", 1.0, 0.05, "Near-perfect metal reflector."),
        new("Brushed Metal", 1.0, 0.35, "Satin metal; pair with anisotropy on the Detail tab."),
        new("Polished Metal", 1.0, 0.15, "Chrome-like hard metal."),
    };
}
