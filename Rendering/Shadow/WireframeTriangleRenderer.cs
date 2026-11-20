// File: Rendering/Shadow/WireframeTriangleRenderer.cs
// Purpose: Draws one triangle's edges as solid visible or dashed hidden lines.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal static class WireframeTriangleRenderer
{
    private static readonly uint SolidColor = ColorMath.PackRgba(ColorMath.ToByte(ColorMath.Gamma(0.83)), ColorMath.ToByte(ColorMath.Gamma(0.91)), ColorMath.ToByte(ColorMath.Gamma(1.0)), 255);
    private static readonly uint HiddenColor = ColorMath.PackRgba(ColorMath.ToByte(ColorMath.Gamma(0.15)), ColorMath.ToByte(ColorMath.Gamma(0.23)), ColorMath.ToByte(ColorMath.Gamma(0.40)), 255);

    /// <summary>Draws one triangle's edges as solid visible or dashed hidden lines.</summary>
    public static void Rasterize(
        uint[] pixels, double[] zBuffer, int width, int height, Triangle tri,
        Vec3 cameraPosition, CameraBasis basis, ref int solidPixels, ref int hiddenPixels)
    {
        if (!CameraProjector.TryProject(tri.A, cameraPosition, basis, width, height, out ProjectedVertex a))
            return;
        if (!CameraProjector.TryProject(tri.B, cameraPosition, basis, width, height, out ProjectedVertex b))
            return;
        if (!CameraProjector.TryProject(tri.C, cameraPosition, basis, width, height, out ProjectedVertex c))
            return;
        double area = RasterMath.Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 1e-9)
            return;
        double dxA = c.X - b.X;
        double dyA = c.Y - b.Y;
        double invBc = 1.0 / Math.Max(1e-9, Math.Sqrt(dxA * dxA + dyA * dyA));
        double dxB = a.X - c.X;
        double dyB = a.Y - c.Y;
        double invCa = 1.0 / Math.Max(1e-9, Math.Sqrt(dxB * dxB + dyB * dyB));
        double dxC = b.X - a.X;
        double dyC = b.Y - a.Y;
        double invAb = 1.0 / Math.Max(1e-9, Math.Sqrt(dxC * dxC + dyC * dyC));
        double absArea = Math.Abs(area);
        RasterMath.ClampBounds(a, b, c, width, height, out int minX, out int maxX, out int minY, out int maxY);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double wa = RasterMath.Edge(b.X, b.Y, c.X, c.Y, x + 0.5, y + 0.5) / area;
                double wb = RasterMath.Edge(c.X, c.Y, a.X, a.Y, x + 0.5, y + 0.5) / area;
                double wc = 1.0 - wa - wb;
                if (wa < 0.0 || wb < 0.0 || wc < 0.0)
                    continue;
                double distA = Math.Abs(wa) * absArea * invBc;
                double distB = Math.Abs(wb) * absArea * invCa;
                double distC = Math.Abs(wc) * absArea * invAb;
                double edgeDistance = Math.Min(distA, Math.Min(distB, distC));
                if (edgeDistance > 0.75)
                    continue;
                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                int index = y * width + x;
                if (depth <= zBuffer[index] + 1e-6 * (1.0 + zBuffer[index]))
                {
                    pixels[index] = SolidColor;
                    solidPixels++;
                    zBuffer[index] = Math.Min(zBuffer[index], depth);
                }
                else if (((x + 2 * y) & 7) < 4)
                {
                    pixels[index] = HiddenColor;
                    hiddenPixels++;
                }
            }
        }
    }
}
