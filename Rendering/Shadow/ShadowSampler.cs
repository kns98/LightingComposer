// File: Rendering/Shadow/ShadowSampler.cs
// Purpose: Single-texel and 3x3 shadow factor sampling.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class ShadowSampler
{
    /// <summary>Samples one shadow texel for the fast preview path.</summary>
    public static double SampleFast(ShadowMap? map, Vec3 point, Vec3 normal)
    {
        if (map == null || !map.Enabled)
            return 1.0;

        Vec3 biasedPoint = point + normal * map.Projection.Bias;
        if (!map.Projection.TryProject(biasedPoint, map.Size, map.Size, out ProjectedVertex p))
            return 1.0;

        int sx = (int)Math.Round(p.X);
        int sy = (int)Math.Round(p.Y);
        if (sx < 0 || sx >= map.Size || sy < 0 || sy >= map.Size)
            return 1.0;

        double storedDepth = map.Depth[sy * map.Size + sx];
        double bias = map.Projection.DepthBias(normal, p.Z, map.Size);
        double visibility = (!double.IsFinite(storedDepth) || p.Z <= storedDepth + bias) ? 1.0 : 0.0;
        return 0.30 + visibility * 0.70;
    }

    /// <summary>Samples a 3 by 3 shadow kernel for soft shadows.</summary>
    public static double SampleSoft(ShadowMap? map, Vec3 point, Vec3 normal)
    {
        if (map == null || !map.Enabled)
            return 1.0;

        Vec3 biasedPoint = point + normal * map.Projection.Bias;
        if (!map.Projection.TryProject(biasedPoint, map.Size, map.Size, out ProjectedVertex p))
            return 1.0;

        int centerX = (int)Math.Round(p.X);
        int centerY = (int)Math.Round(p.Y);
        double bias = map.Projection.DepthBias(normal, p.Z, map.Size);
        double lit = 0.0;
        double samples = 0.0;
        for (int oy = -1; oy <= 1; oy++)
        {
            int sy = centerY + oy;
            if (sy < 0 || sy >= map.Size)
                continue;
            for (int ox = -1; ox <= 1; ox++)
            {
                int sx = centerX + ox;
                if (sx < 0 || sx >= map.Size)
                    continue;

                double storedDepth = map.Depth[sy * map.Size + sx];
                if (!double.IsFinite(storedDepth) || p.Z <= storedDepth + bias)
                    lit += 1.0;
                samples += 1.0;
            }
        }

        if (samples <= 0.0)
            return 1.0;

        double visibility = lit / samples;
        return Math.Clamp(0.24 + visibility * 0.76, 0.24, 1.0);
    }
}
