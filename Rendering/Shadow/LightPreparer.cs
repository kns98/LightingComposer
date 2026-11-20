// File: Rendering/Shadow/LightPreparer.cs
// Purpose: Enabled lights paired with shadow maps and spot cone data.

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class LightPreparer
{
    /// <summary>Prepares enabled lights with shadow maps and spot cone data.</summary>
    public PreparedLight[] Prepare(Scene scene, IReadOnlyList<ShadowMap> shadowMaps)
    {
        Dictionary<string, ShadowMap> shadowByLightId = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShadowMap map in shadowMaps)
        {
            if (!string.IsNullOrWhiteSpace(map.LightId))
                shadowByLightId[map.LightId] = map;
        }

        List<PreparedLight> prepared = new();
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;

            shadowByLightId.TryGetValue(light.Id, out ShadowMap? shadowMap);
            Vec3 directionalLightDirection = light.Kind == SceneLightKind.Directional
                ? (light.Direction * -1.0).Normalize()
                : Vec3.Zero;
            Vec3 spotForward = light.Kind == SceneLightKind.Spot
                ? light.Direction.Normalize()
                : Vec3.Zero;
            double cosOuter = light.Kind == SceneLightKind.Spot ? Math.Cos(light.OuterConeAngle) : 0.0;
            double cosInner = light.Kind == SceneLightKind.Spot ? Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)) : 1.0;
            prepared.Add(new PreparedLight(light, shadowMap, directionalLightDirection, spotForward, cosOuter, cosInner));
        }

        return prepared.ToArray();
    }
}
