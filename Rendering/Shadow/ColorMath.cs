// File: Rendering/Shadow/ColorMath.cs
// Purpose: Clamping, gamma, and RGBA packing for preview pixels.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class ColorMath
{
    /// <summary>Clamps a color to the preview range.</summary>
    public static Vec3 Clamp(Vec3 color) => new(
        Math.Clamp(color.X, 0.0, 8.0),
        Math.Clamp(color.Y, 0.0, 8.0),
        Math.Clamp(color.Z, 0.0, 8.0));

    /// <summary>Applies gamma correction to one channel.</summary>
    public static double Gamma(double value) => Math.Pow(Math.Clamp(value, 0.0, 1.0), 1.0 / 2.2);

    /// <summary>Packs four bytes into one RGBA pixel.</summary>
    public static uint PackRgba(byte r, byte g, byte b, byte a) =>
        (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    /// <summary>Converts one unit channel value to a byte.</summary>
    public static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

    /// <summary>Gamma-corrects and packs a shaded color into one opaque pixel.</summary>
    public static uint PackShaded(Vec3 shaded) =>
        PackRgba(ToByte(Gamma(shaded.X)), ToByte(Gamma(shaded.Y)), ToByte(Gamma(shaded.Z)), 255);
}
