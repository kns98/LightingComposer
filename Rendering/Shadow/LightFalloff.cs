// File: Rendering/Shadow/LightFalloff.cs
// Purpose: Distance attenuation and spot cone falloff shared by both shaders.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class LightFalloff
{
    /// <summary>Computes distance attenuation with smooth range falloff.</summary>
    public static double DistanceAttenuation(double distance, double range)
    {
        double attenuation = 1.0 / (1.0 + 0.11 * distance * distance);
        if (range <= 0.0)
            return attenuation;

        double normalized = Math.Clamp(distance / range, 0.0, 1.0);
        double edgeFade = 1.0 - normalized * normalized;
        return attenuation * edgeFade * edgeFade;
    }

    /// <summary>Computes the smooth spot cone falloff for one direction.</summary>
    public static double SpotConeFactor(PreparedLight prepared, Vec3 lightToSurface)
    {
        double theta = prepared.SpotForward.Dot(lightToSurface.Normalize());
        if (theta <= prepared.CosOuter)
            return 0.0;
        if (theta >= prepared.CosInner)
            return 1.0;
        return (theta - prepared.CosOuter) / Math.Max(1e-8, prepared.CosInner - prepared.CosOuter);
    }
}
