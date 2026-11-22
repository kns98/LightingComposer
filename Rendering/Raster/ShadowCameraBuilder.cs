// File: Rendering/Raster/ShadowCameraBuilder.cs
// Purpose: Shadow frustum cameras mirroring the CPU shadow projections.

using System.Numerics;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

/// <summary>Builds shadow camera rows mirroring the CPU shadow projections.</summary>
internal static class ShadowCameraBuilder
{
    /// <summary>Builds the packed camera rows for every shadow map layer.</summary>
    public static Vector4[] BuildFrameRows(IReadOnlyList<PreparedShadowLight> shadowLights, Vec3 center, double radius)
    {
        Vector4[] rows = new Vector4[RasterLimits.MaxShadowLights * 5];
        foreach (PreparedShadowLight shadow in shadowLights)
        {
            if (shadow.Layer < 0 || shadow.Layer >= RasterLimits.MaxShadowLights)
                continue;
            BuildRows(shadow.Light, center, radius).CopyTo(rows, shadow.Layer * 5);
        }
        return rows;
    }

    /// <summary>Builds position, basis, and projection rows for one shadow light.</summary>
    public static Vector4[] BuildRows(SceneLight light, Vec3 center, double radius)
    {
        radius = Math.Max(0.35, radius);
        Vec3 forward;
        Vec3 eye;
        float tanHalfFov;
        float near;
        float far;

        if (light.Kind == SceneLightKind.Directional)
        {
            forward = NormalizeOrFallback(light.Direction, new Vec3(0.45, -0.75, 0.55));
            eye = center - forward * (radius * 2.5);
            tanHalfFov = -(float)(radius * 1.45);
            near = 0.0f;
            far = (float)(radius * 5.0);
        }
        else if (light.Kind == SceneLightKind.Spot)
        {
            forward = NormalizeOrFallback(light.Direction, new Vec3(0, -1, 0));
            eye = light.Position;
            double fov = Math.Clamp(light.OuterConeAngle * 2.2, 18.0 * Math.PI / 180.0, 140.0 * Math.PI / 180.0);
            tanHalfFov = (float)Math.Tan(fov * 0.5);
            near = (float)ShadowLimits.CameraNear;
            far = (float)(light.Range > 0.0 ? Math.Max(light.Range, radius * 2.0) : radius * 5.0);
        }
        else
        {
            forward = NormalizeOrFallback(center - light.Position, new Vec3(0, -1, 0));
            eye = light.Position;
            tanHalfFov = (float)Math.Tan(Math.PI * 0.36);
            near = (float)ShadowLimits.CameraNear;
            far = (float)(light.Range > 0.0 ? Math.Max(light.Range, radius * 2.0) : radius * 5.0);
        }

        OrthonormalBasis.Create(forward, out Vec3 f, out Vec3 r, out Vec3 u);
        return new[]
        {
            eye.ToVector4(1.0f),
            f.ToVector4(0.0f),
            r.ToVector4(0.0f),
            u.ToVector4(0.0f),
            new Vector4(1.0f, tanHalfFov, near, far)
        };
    }

    /// <summary>Normalizes a direction, falling back when it is degenerate.</summary>
    private static Vec3 NormalizeOrFallback(Vec3 value, Vec3 fallback)
    {
        Vec3 normalized = value.Normalize();
        return normalized.Length() < 1e-8 ? fallback.Normalize() : normalized;
    }
}
