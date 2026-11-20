// File: Rendering/Shadow/FastPixelShader.cs
// Purpose: Flat diffuse-only shading for interactive previews.

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class FastPixelShader : PixelShader
{
    private Vec3 albedo;

    public override bool Prepare(Triangle tri, ProjectedVertex a, ProjectedVertex b, ProjectedVertex c)
    {
        double fastU = (tri.UvA.U + tri.UvB.U + tri.UvC.U) / 3.0;
        double fastV = (tri.UvA.V + tri.UvB.V + tri.UvC.V) / 3.0;
        albedo = tri.Material.Sample(fastU, fastV);
        return tri.Material.SampleAlpha(fastU, fastV) >= 0.04;
    }

    /// <summary>Shades one point with fast diffuse-only lighting.</summary>
    public override bool TryShade(in PixelSample sample, out Vec3 shaded)
    {
        Vec3 color = albedo * 0.085;

        foreach (PreparedLight prepared in sample.Lights)
        {
            SceneLight light = prepared.Light;
            Vec3 lightDir;
            double strength = light.Intensity;
            double attenuation = 1.0;
            double cone = 1.0;

            if (light.Kind == SceneLightKind.Directional)
            {
                lightDir = prepared.DirectionalLightDirection;
            }
            else
            {
                Vec3 toLight = light.Position - sample.World;
                double distanceSquared = toLight.Dot(toLight);
                if (distanceSquared < 1e-12)
                    continue;
                double distance = Math.Sqrt(distanceSquared);
                if (light.Range > 0.0 && distance > light.Range)
                    continue;

                lightDir = toLight / distance;
                attenuation = LightFalloff.DistanceAttenuation(distance, light.Range);
                if (light.Kind == SceneLightKind.Spot)
                {
                    cone = LightFalloff.SpotConeFactor(prepared, lightDir * -1.0);
                    if (cone <= 0.0)
                        continue;
                }
            }

            double ndotl = Math.Max(0.0, sample.Normal.Dot(lightDir));
            if (ndotl <= 0.0)
                continue;

            double shadow = light.CastsShadow ? ShadowSampler.SampleFast(prepared.ShadowMap, sample.World, sample.Normal) : 1.0;
            color += albedo.Multiply(light.Color) * (ndotl * strength * attenuation * cone * shadow * 0.18);
        }

        shaded = ColorMath.Clamp(color);
        return true;
    }
}
