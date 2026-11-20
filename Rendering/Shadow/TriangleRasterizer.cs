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
        Vec3 cameraPosition, CameraBasis basis, IReadOnlyList<PreparedLight> lights, bool interactiveFast)
    {
        if (!CameraProjector.TryProject(tri.A, cameraPosition, basis, width, height, out ProjectedVertex a) ||
            !CameraProjector.TryProject(tri.B, cameraPosition, basis, width, height, out ProjectedVertex b) ||
            !CameraProjector.TryProject(tri.C, cameraPosition, basis, width, height, out ProjectedVertex c))
            return;

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
                double wa = RasterMath.Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                double wb = RasterMath.Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                double wc = 1.0 - wa - wb;
                if (wa < -1e-7 || wb < -1e-7 || wc < -1e-7)
                    continue;

                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                int pixel = y * width + x;
                if (depth >= zBuffer[pixel])
                    continue;

                Vec3 world = tri.A * wa + tri.B * wb + tri.C * wc;
                PixelSample sample = new(tri, wa, wb, wc, world, normal, cameraPosition, lights);
                if (!shader.TryShade(in sample, out Vec3 shaded))
                    continue;

                zBuffer[pixel] = depth;
                pixels[row + x] = ColorMath.PackShaded(shaded);
            }
        }
    }
}
