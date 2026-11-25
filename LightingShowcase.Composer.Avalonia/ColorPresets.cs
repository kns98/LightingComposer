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
        new("Black", C(0, 0, 0)),
        new("Dark Orange", C(255, 140, 0)),
        new("Coral", C(255, 127, 80)),
        new("Salmon", C(250, 128, 114)),
        new("Maroon", C(128, 0, 0)),
        new("Olive", C(128, 128, 0)),
        new("Dark Green", C(0, 100, 0)),
        new("Sea Green", C(46, 139, 87)),
        new("Spring Green", C(0, 255, 127)),
        new("Turquoise", C(64, 224, 208)),
        new("Dark Cyan", C(0, 139, 139)),
        new("Steel Blue", C(70, 130, 180)),
        new("Royal Blue", C(65, 105, 225)),
        new("Indigo", C(75, 0, 130)),
        new("Violet", C(238, 130, 238)),
        new("Orchid", C(218, 112, 214)),
        new("Deep Pink", C(255, 20, 147)),
        new("Hot Pink", C(255, 105, 180)),
        new("Lavender", C(230, 230, 250)),
        new("Saddle Brown", C(139, 69, 19)),
        new("Sienna", C(160, 82, 45)),
        new("Tan", C(210, 180, 140)),
        new("Beige", C(245, 245, 220)),
        new("Ivory", C(255, 255, 240)),
        new("Khaki", C(240, 230, 140)),
        new("Silver", C(192, 192, 192)),
        new("Dark Gray", C(169, 169, 169)),
        new("Slate Gray", C(112, 128, 144)),
        new("Dark Slate Gray", C(47, 79, 79))
    };
}
