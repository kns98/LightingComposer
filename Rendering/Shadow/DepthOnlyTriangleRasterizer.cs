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
        if (!CameraProjector.TryProject(tri.A, cameraPosition, basis, width, height, out ProjectedVertex a))
            return;
        if (!CameraProjector.TryProject(tri.B, cameraPosition, basis, width, height, out ProjectedVertex b))
            return;
        if (!CameraProjector.TryProject(tri.C, cameraPosition, basis, width, height, out ProjectedVertex c))
            return;
        double area = RasterMath.Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 1e-9)
            return;
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
                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                int index = y * width + x;
                if (depth < zBuffer[index])
                    zBuffer[index] = depth;
            }
        }
    }
}
