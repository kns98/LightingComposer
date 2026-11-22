// File: Rendering/Shadow/FullPixelShader.cs
// Purpose: Diffuse, specular, shadow, and emission shading for full frames.

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class FullPixelShader : PixelShader
{
    private double invZa;
    private double invZb;
    private double invZc;

    public override bool Prepare(Triangle tri, ProjectedVertex a, ProjectedVertex b, ProjectedVertex c)
    {
        invZa = 1.0 / Math.Max(ShadowLimits.CameraNear, a.Z);
        invZb = 1.0 / Math.Max(ShadowLimits.CameraNear, b.Z);
        invZc = 1.0 / Math.Max(ShadowLimits.CameraNear, c.Z);
        return true;
    }

    /// <summary>Shades one point with diffuse, specular, shadow, and emission terms.</summary>
    public override bool TryShade(in PixelSample sample, out Vec3 shaded, out double alpha)
    {
        double invZ = sample.Wa * invZa + sample.Wb * invZb + sample.Wc * invZc;
        double u = ((sample.Wa * sample.Triangle.UvA.U * invZa) + (sample.Wb * sample.Triangle.UvB.U * invZb) + (sample.Wc * sample.Triangle.UvC.U * invZc)) / Math.Max(1e-12, invZ);
        double v = ((sample.Wa * sample.Triangle.UvA.V * invZa) + (sample.Wb * sample.Triangle.UvB.V * invZb) + (sample.Wc * sample.Triangle.UvC.V * invZc)) / Math.Max(1e-12, invZ);

        Material material = sample.Triangle.Material;
        alpha = material.AlphaMode == MaterialAlphaMode.Blend ? material.SampleAlpha(u, v) : 1.0;
        if (material.AlphaMode == MaterialAlphaMode.Mask && material.SampleAlpha(u, v) < material.AlphaCutoff)
        {
            shaded = Vec3.Zero;
            alpha = 0.0;
            return false;
        }

        Vec3 albedo = material.Sample(u, v);
        (double metallic, double roughness) = material.SampleMetallicRoughness(u, v);
        Vec3 viewDirection = (sample.CameraPosition - sample.World).Normalize();
        Vec3 normal = ApplyNormalMap(sample.Triangle, sample.Normal, material, u, v);
        if (normal.Dot(viewDirection) < 0.0)
            normal = -normal;
        double occlusion = material.SampleOcclusion(u, v);
        Vec3 color = albedo * ((0.075 + 0.035 * (1.0 - metallic)) * occlusion);

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
                double distance = toLight.Length();
                if (distance < 1e-8 || (light.Range > 0.0 && distance > light.Range))
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

            double shadow = light.CastsShadow ? ShadowSampler.SampleSoft(prepared.ShadowMap, sample.World, normal) : 1.0;
            color += LightContribution(albedo, normal, viewDirection, lightDir, light.Color, strength * attenuation * cone * shadow, metallic, roughness);
        }

        color += material.SampleEmission(u, v);
        shaded = ColorMath.Clamp(color);
        return true;
    }

    /// <summary>Perturbs the geometric normal using the material normal map.</summary>
    private static Vec3 ApplyNormalMap(Triangle tri, Vec3 normal, Material material, double u, double v)
    {
        normal = normal.Normalize();
        if (material.NormalTexture == null)
            return normal;

        tri.GetTangentBasis(normal, out Vec3 tangent, out Vec3 bitangent);
        Vec3 sample = material.SampleNormalMap(u, v);
        double scale = material.NormalScale;
        Vec3 tangentNormal = new Vec3(
            (sample.X * 2.0 - 1.0) * scale,
            (sample.Y * 2.0 - 1.0) * scale,
            sample.Z * 2.0 - 1.0).Normalize();
        return (tangent * tangentNormal.X + bitangent * tangentNormal.Y + normal * tangentNormal.Z).Normalize();
    }

    /// <summary>Computes diffuse plus specular contribution from one light.</summary>
    private static Vec3 LightContribution(Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, Vec3 lightDir, Vec3 lightColor, double strength, double metallic, double roughness)
    {
        double ndotl = Math.Max(0.0, normal.Dot(lightDir));
        if (ndotl <= 0.0 || strength <= 0.0)
            return Vec3.Zero;

        double previewScale = 0.18;
        Vec3 diffuse = surfaceColor.Multiply(lightColor) * ((1.0 - metallic) * ndotl * strength * previewScale);
        Vec3 halfVector = (lightDir + viewDirection).Normalize();
        double ndoth = Math.Max(0.0, normal.Dot(halfVector));
        double shininess = Math.Clamp(2.0 / (roughness * roughness) - 2.0, 2.0, 192.0);
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
        double specularTerm = Math.Pow(ndoth, shininess) * ndotl * strength * previewScale * (1.0 - roughness * 0.65);
        Vec3 specular = f0.Multiply(lightColor) * (specularTerm * (1.0 + metallic));
        return diffuse + specular;
    }
}
