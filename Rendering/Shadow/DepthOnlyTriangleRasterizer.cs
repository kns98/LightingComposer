// File: Rendering/Shadow/DepthOnlyTriangleRasterizer.cs
// Purpose: Rasterizes one triangle into the depth buffer without shading.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal static class DepthOnlyTriangleRasterizer
{
    /// <summary>Rasterizes one triangle into the depth buffer without shading.</summary>
    public static void Rasterize(double[] zBuffer, int width, int height, Triangle tri, Vec3 cameraPosition, CameraBasis basis)
    {
        if (CameraProjector.TryProject(tri.A, cameraPosition, basis, width, height, out ProjectedVertex a) &&
            CameraProjector.TryProject(tri.B, cameraPosition, basis, width, height, out ProjectedVertex b) &&
            CameraProjector.TryProject(tri.C, cameraPosition, basis, width, height, out ProjectedVertex c))
        {
            RasterizePiece(zBuffer, width, height, a, b, c, null);
            return;
        }

        if (!RasterClipper.TryClip(tri, cameraPosition, basis, width, height,
                out RasterClipper.ClippedTriangle[] clipped, out _, out double za, out double zb, out double zc))
            return;

        foreach (RasterClipper.ClippedTriangle piece in clipped)
        {
            RasterizePiece(zBuffer, width, height,
                new ProjectedVertex(piece.A.X, piece.A.Y, za),
                new ProjectedVertex(piece.B.X, piece.B.Y, zb),
                new ProjectedVertex(piece.C.X, piece.C.Y, zc),
                piece);
        }
    }

    /// <summary>Rasterizes one fully-projectable triangle piece into the depth buffer.</summary>
    private static void RasterizePiece(
        double[] zBuffer, int width, int height,
        ProjectedVertex a, ProjectedVertex b, ProjectedVertex c, RasterClipper.ClippedTriangle? map)
    {
        double area = RasterMath.Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 1e-9)
            return;
        double invZa = 1.0 / TriangleRasterizer.ClampDepth(a.Z);
        double invZb = 1.0 / TriangleRasterizer.ClampDepth(b.Z);
        double invZc = 1.0 / TriangleRasterizer.ClampDepth(c.Z);
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
                (double wa, double wb, double wc) = map?.Map(qa, qb, qc) ?? (qa, qb, qc);
                double invDepth = wa * invZa + wb * invZb + wc * invZc;
                double depth = invDepth > 1e-12 ? 1.0 / invDepth : double.PositiveInfinity;
                int index = y * width + x;
                if (depth < zBuffer[index])
                    zBuffer[index] = depth;
            }
        }
    }
}
