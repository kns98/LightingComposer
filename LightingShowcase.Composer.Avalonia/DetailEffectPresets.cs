// Named detail-effect preset library for the Detail tab: normal, occlusion,
// specular, sheen, anisotropy, and iridescence values for one-click surface
// character. Hosts write the picked values into their editors so validation
// and apply paths stay unchanged.
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

internal sealed record DetailEffectPreset(
    string Name,
    double NormalScale,
    double OcclusionStrength,
    double Specular,
    Vec3 SpecularColor,
    Vec3 SheenColor,
    double SheenRoughness,
    double Anisotropy,
    double AnisotropyRotation,
    double Iridescence,
    double IridescenceIor,
    double IridescenceThicknessMinimum,
    double IridescenceThicknessMaximum,
    string Summary);

internal static class DetailEffectPresets
{
    private static Vec3 C(int r, int g, int b) => new(r / 255.0, g / 255.0, b / 255.0);

    private static DetailEffectPreset P(
        string name,
        double normalScale = 1.0,
        double occlusionStrength = 1.0,
        double specular = 1.0,
        Vec3? specularColor = null,
        Vec3? sheenColor = null,
        double sheenRoughness = 0.0,
        double anisotropy = 0.0,
        double anisotropyRotation = 0.0,
        double iridescence = 0.0,
        double iridescenceIor = 1.3,
        double iridescenceThicknessMinimum = 100.0,
        double iridescenceThicknessMaximum = 400.0,
        string summary = "") => new(
            name,
            normalScale,
            occlusionStrength,
            specular,
            specularColor ?? new Vec3(1.0, 1.0, 1.0),
            sheenColor ?? Vec3.Zero,
            sheenRoughness,
            anisotropy,
            anisotropyRotation,
            iridescence,
            iridescenceIor,
            iridescenceThicknessMinimum,
            iridescenceThicknessMaximum,
            summary);

    public static readonly IReadOnlyList<DetailEffectPreset> All = new DetailEffectPreset[]
    {
        P("Flat", summary: "No detail effects; neutral scales."),
        P("Brushed Metal", anisotropy: 0.8, summary: "Stretched highlights for brushed metal along U."),
        P("Satin Metal", anisotropy: 0.4, anisotropyRotation: 0.785, specular: 0.9, summary: "Softer directional sheen, rotated 45 degrees."),
        P("Velvet", specular: 0.3, sheenColor: C(87, 5, 28), sheenRoughness: 0.9, summary: "Deep soft red velvet retroreflection."),
        P("Silk Fabric", specular: 0.5, sheenColor: C(217, 207, 191), sheenRoughness: 0.35, summary: "Tight silky cloth highlight."),
        P("Leather Grain", specular: 0.6, sheenColor: C(51, 26, 13), sheenRoughness: 0.6, summary: "Worn brown leather softness."),
        P("Soap Film", iridescence: 0.9, summary: "Thin-film rainbow like a soap bubble."),
        P("Oil Slick", specular: 0.9, anisotropy: 0.3, iridescence: 1.0, iridescenceIor: 1.8, iridescenceThicknessMinimum: 200.0, iridescenceThicknessMaximum: 800.0, summary: "Thick-film rainbow with directional smear."),
    };
}
