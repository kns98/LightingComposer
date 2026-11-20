// File: Rendering/Shadow/ShadowProjectionSelector.cs
// Purpose: Creates the shadow projection matching the light kind.

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class ShadowProjectionSelector
{
    /// <summary>Creates the shadow projection matching the light kind.</summary>
    public static ShadowProjection Create(SceneLight light, Vec3 center, double radius)
    {
        if (light.Kind == SceneLightKind.Directional)
        {
            Vec3 directionalForward = light.Direction.Normalize();
            if (directionalForward.Length() < 1e-8)
                directionalForward = new Vec3(0.45, -0.75, 0.55).Normalize();
            Vec3 origin = center - directionalForward * (radius * 2.5);
            return ShadowProjection.CreateOrthographic(origin, directionalForward, radius * 1.45, radius * 5.0, bias: radius * 0.0025 + 0.0015);
        }

        Vec3 forward = light.Kind == SceneLightKind.Spot
            ? light.Direction.Normalize()
            : (center - light.Position).Normalize();
        if (forward.Length() < 1e-8)
            forward = new Vec3(0, -1, 0);

        double fov = light.Kind == SceneLightKind.Spot
            ? Math.Clamp(light.OuterConeAngle * 2.20, 18.0 * Math.PI / 180.0, 140.0 * Math.PI / 180.0)
            : Math.PI * 0.72;
        double far = light.Range > 0.0 ? Math.Max(light.Range, radius * 2.0) : radius * 5.0;
        return ShadowProjection.CreatePerspective(light.Position, forward, fov, far, bias: radius * 0.0035 + 0.0025);
    }
}
