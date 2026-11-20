// File: Rendering/Shadow/ShadowTriangleRasterizer.cs
// Purpose: Rasterizes one triangle into a shadow map depth buffer.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal static class ShadowTriangleRasterizer
{
    /// <summary>Rasterizes one triangle into a shadow map depth buffer.</summary>
    public static void Rasterize(ShadowMap map, Triangle tri)
    {
        if (!map.Projection.TryProject(tri.A, map.Size, map.Size, out ProjectedVertex a) ||
            !map.Projection.TryProject(tri.B, map.Size, map.Size, out ProjectedVertex b) ||
            !map.Projection.TryProject(tri.C, map.Size, map.Size, out ProjectedVertex c))
            return;

        double area = RasterMath.Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 1e-9)
            return;

        RasterMath.ClampBounds(a, b, c, map.Size, map.Size, out int minX, out int maxX, out int minY, out int maxY);

        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double wa = RasterMath.Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                double wb = RasterMath.Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                double wc = 1.0 - wa - wb;
                if (wa < -1e-7 || wb < -1e-7 || wc < -1e-7)
                    continue;

                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                int index = y * map.Size + x;
                if (depth < map.Depth[index])
                {
                    map.Depth[index] = depth;
                    map.HasDepth = true;
                }
            }
        }
    }
}
