// File: Rendering/Shadow/BackgroundPainter.cs
// Purpose: Vertical gradient background shared by shaded and wireframe frames.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class BackgroundPainter
{
    /// <summary>Clears the frame with a vertical gradient background.</summary>
    public static void Clear(uint[] pixels, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            double t = height <= 1 ? 0.0 : y / (double)(height - 1);
            Vec3 color = Vec3.Lerp(new Vec3(0.055, 0.060, 0.074), new Vec3(0.010, 0.012, 0.016), t);
            uint packed = ColorMath.PackRgba(
                ColorMath.ToByte(ColorMath.Gamma(color.X)),
                ColorMath.ToByte(ColorMath.Gamma(color.Y)),
                ColorMath.ToByte(ColorMath.Gamma(color.Z)),
                255);
            Array.Fill(pixels, packed, y * width, width);
        }
    }
}
