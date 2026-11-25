// Named color preset library shared by the material and light editors.
// Values are standard CSS colors in 0-1 linear-ish display space.
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

internal sealed record ColorPreset(string Name, Vec3 Color);

internal static class ColorPresets
{
    private static Vec3 C(int r, int g, int b) => new(r / 255.0, g / 255.0, b / 255.0);

    public static readonly IReadOnlyList<ColorPreset> All = new ColorPreset[]
    {
        new("Red", C(255, 0, 0)),
        new("Crimson", C(220, 20, 60)),
        new("Dark Red", C(139, 0, 0)),
        new("Orange", C(255, 165, 0)),
        new("Yellow", C(255, 255, 0)),
        new("Gold", C(255, 215, 0)),
        new("Lime", C(0, 255, 0)),
        new("Green", C(0, 128, 0)),
        new("Forest Green", C(34, 139, 34)),
        new("Teal", C(0, 128, 128)),
        new("Blue", C(0, 0, 255)),
        new("Navy", C(0, 0, 128)),
        new("Sky Blue", C(135, 206, 235)),
        new("Cyan", C(0, 255, 255)),
        new("Purple", C(128, 0, 128)),
        new("Magenta", C(255, 0, 255)),
        new("Pink", C(255, 192, 203)),
        new("Brown", C(165, 42, 42)),
        new("White", C(255, 255, 255)),
        new("Light Gray", C(211, 211, 211)),
        new("Gray", C(128, 128, 128)),
        new("Black", C(0, 0, 0))
    };
}
