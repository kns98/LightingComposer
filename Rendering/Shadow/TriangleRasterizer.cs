// File: Rendering/Shadow/TriangleRasterizer.cs
// Purpose: Projects and shades one triangle into pixel and depth buffers.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class TriangleRasterizer
{
    /// <summary>Projects and shades one triangle into the pixel and depth buffers.</summary>
    public void Rasterize(
        uint[] pixels, double[] zBuffer, int width, int height, Triangle tri,
        Vec3 cameraPosition, CameraBasis basis, IReadOnlyList<PreparedLight> lights, bool interactiveFast, double exposure)
    {
        if (CameraProjector.TryProject(tri.A, cameraPosition, basis, width, height, out ProjectedVertex a) &&
            CameraProjector.TryProject(tri.B, cameraPosition, basis, width, height, out ProjectedVertex b) &&
            CameraProjector.TryProject(tri.C, cameraPosition, basis, width, height, out ProjectedVertex c))
        {
            RasterizePiece(pixels, zBuffer, width, height, tri, a, b, c, null, cameraPosition, basis, lights, interactiveFast, exposure);
            return;
        }

        if (!RasterClipper.TryClip(tri, cameraPosition, basis, width, height,
                out RasterClipper.ClippedTriangle[] clipped, out _, out double za, out double zb, out double zc))
            return;

        foreach (RasterClipper.ClippedTriangle piece in clipped)
        {
            RasterizePiece(pixels, zBuffer, width, height, tri,
                new ProjectedVertex(piece.A.X, piece.A.Y, za),
                new ProjectedVertex(piece.B.X, piece.B.Y, zb),
                new ProjectedVertex(piece.C.X, piece.C.Y, zc),
                piece, cameraPosition, basis, lights, interactiveFast, exposure);
        }
    }

    /// <summary>Rasterizes one fully-projectable triangle piece with optional barycentric mapping.</summary>
    private void RasterizePiece(
        uint[] pixels, double[] zBuffer, int width, int height, Triangle tri,
        ProjectedVertex a, ProjectedVertex b, ProjectedVertex c, RasterClipper.ClippedTriangle? map,
        Vec3 cameraPosition, CameraBasis basis, IReadOnlyList<PreparedLight> lights, bool interactiveFast, double exposure)
    {
        double area = RasterMath.Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 1e-9)
            return;

        RasterMath.ClampBounds(a, b, c, width, height, out int minX, out int maxX, out int minY, out int maxY);
        if (maxX < minX || maxY < minY)
            return;

        Vec3 normal = tri.Normal.Normalize();
        Vec3 triangleCenter = (tri.A + tri.B + tri.C) / 3.0;
        if (normal.Dot(cameraPosition - triangleCenter) < 0.0)
            normal = normal * -1.0;

        PixelShader shader = interactiveFast ? new FastPixelShader() : new FullPixelShader();
        if (!shader.Prepare(tri, a, b, c))
            return;

        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            int row = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double qa = RasterMath.Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                double qb = RasterMath.Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                double qc = 1.0 - qa - qb;
                if (qa < -1e-7 || qb < -1e-7 || qc < -1e-7)
                    continue;
                (double wa, double wb, double wc) = map?.Map(qa, qb, qc) ?? (qa, qb, qc);

                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                int pixel = y * width + x;
                if (depth >= zBuffer[pixel])
                    continue;

                Vec3 world = tri.A * wa + tri.B * wb + tri.C * wc;
                PixelSample sample = new(tri, wa, wb, wc, world, normal, cameraPosition, lights);
                if (!shader.TryShade(in sample, out Vec3 shaded, out double alpha))
                    continue;

                if (alpha >= 0.999)
                {
                    zBuffer[pixel] = depth;
                    pixels[row + x] = ColorMath.PackShaded(shaded, exposure);
                }
                else
                {
                    Vec3 dst = ColorMath.UnpackDisplay(pixels[row + x]);
                    Vec3 src = ColorMath.ToDisplay(shaded, exposure);
                    pixels[row + x] = ColorMath.PackDisplay(Vec3.Lerp(dst, src, Math.Clamp(alpha, 0.0, 1.0)));
                }
            }
        }
    }
}
