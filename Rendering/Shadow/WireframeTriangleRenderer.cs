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
        if (CameraProjector.TryProject(tri.A, cameraPosition, basis, width, height, out ProjectedVertex a) &&
            CameraProjector.TryProject(tri.B, cameraPosition, basis, width, height, out ProjectedVertex b) &&
            CameraProjector.TryProject(tri.C, cameraPosition, basis, width, height, out ProjectedVertex c))
        {
            RasterizePiece(pixels, zBuffer, width, height, a, b, c, null, null, ref solidPixels, ref hiddenPixels);
            return;
        }

        if (!RasterClipper.TryClip(tri, cameraPosition, basis, width, height,
                out RasterClipper.ClippedTriangle[] clipped, out RasterClipper.ClippedEdge[] edges,
                out double za, out double zb, out double zc))
            return;

        foreach (RasterClipper.ClippedTriangle piece in clipped)
            RasterizePiece(pixels, zBuffer, width, height,
                new ProjectedVertex(piece.A.X, piece.A.Y, za),
                new ProjectedVertex(piece.B.X, piece.B.Y, zb),
                new ProjectedVertex(piece.C.X, piece.C.Y, zc),
                piece, edges, ref solidPixels, ref hiddenPixels);
    }

    /// <summary>Rasterizes one fully-projectable triangle piece, drawing only original wireframe edges.</summary>
    private static void RasterizePiece(
        uint[] pixels, double[] zBuffer, int width, int height,
        ProjectedVertex a, ProjectedVertex b, ProjectedVertex c,
        RasterClipper.ClippedTriangle? map, RasterClipper.ClippedEdge[]? drawn,
        ref int solidPixels, ref int hiddenPixels)
    {
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
                double qa = RasterMath.Edge(b.X, b.Y, c.X, c.Y, x + 0.5, y + 0.5) / area;
                double qb = RasterMath.Edge(c.X, c.Y, a.X, a.Y, x + 0.5, y + 0.5) / area;
                double qc = 1.0 - qa - qb;
                if (qa < 0.0 || qb < 0.0 || qc < 0.0)
                    continue;
                double edgeDistance;
                if (drawn == null)
                {
                    double distA = Math.Abs(qa) * absArea * invBc;
                    double distB = Math.Abs(qb) * absArea * invCa;
                    double distC = Math.Abs(qc) * absArea * invAb;
                    edgeDistance = Math.Min(distA, Math.Min(distB, distC));
                }
                else
                {
                    edgeDistance = double.PositiveInfinity;
                    foreach (RasterClipper.ClippedEdge edge in drawn)
                        edgeDistance = Math.Min(edgeDistance, PointSegmentDistance(x + 0.5, y + 0.5, edge));
                }
                if (edgeDistance > 0.75)
                    continue;
                (double wa, double wb, double wc) = map?.Map(qa, qb, qc) ?? (qa, qb, qc);
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

    /// <summary>Measures point-to-segment distance in pixels.</summary>
    private static double PointSegmentDistance(double px, double py, RasterClipper.ClippedEdge edge)
    {
        double dx = edge.X1 - edge.X0;
        double dy = edge.Y1 - edge.Y0;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared > 1e-12 ? ((px - edge.X0) * dx + (py - edge.Y0) * dy) / lengthSquared : 0.0;
        t = Math.Clamp(t, 0.0, 1.0);
        double ex = edge.X0 + t * dx - px;
        double ey = edge.Y0 + t * dy - py;
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
