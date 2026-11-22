// File: Rendering/Raster/LightBufferBuilder.cs
// Purpose: Enabled scene lights collected into capped GPU light records.

using LightingShowcase.Lighting;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class LightBufferBuilder
{
    /// <summary>Collects enabled scene lights into GPU light records.</summary>
    public RasterLight[] Build(Scene scene) =>
        PackLights(scene).Select(entry => new RasterLight(entry.Light, entry.Layer)).ToArray();

    /// <summary>Selects the shadow-casting lights with their assigned shadow map layers.</summary>
    public IReadOnlyList<PreparedShadowLight> SelectShadowLights(Scene scene) =>
        PackLights(scene).Where(entry => entry.Layer >= 0).Select(entry => new PreparedShadowLight(entry.Light, entry.Layer)).ToArray();

    /// <summary>Walks packed lights once, assigning shadow map layers in packed order.</summary>
    private static IEnumerable<(SceneLight Light, int Layer)> PackLights(Scene scene)
    {
        int shadowLayer = 0;
        int packed = 0;
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;
            if (packed >= RasterLimits.MaxGpuLights)
                break;
            packed++;
            int layer = -1;
            if (light.CastsShadow && shadowLayer < RasterLimits.MaxShadowLights)
                layer = shadowLayer++;
            yield return (light, layer);
        }
    }
}
