// File: Rendering/Shadow/RasterMath.cs
// Purpose: Edge function and bounding-box math shared by all rasterizers.

namespace LightingShowcase.Rendering;

internal static class RasterMath
{
    /// <summary>Evaluates the signed edge function for rasterization.</summary>
    public static double Edge(double ax, double ay, double bx, double by, double px, double py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    /// <summary>Clamps a projected triangle to a pixel bounding box.</summary>
    public static void ClampBounds(
        ProjectedVertex a, ProjectedVertex b, ProjectedVertex c,
        int width, int height, out int minX, out int maxX, out int minY, out int maxY)
    {
        minX = Math.Clamp((int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1);
        maxX = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1);
        minY = Math.Clamp((int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1);
        maxY = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
    }
}
