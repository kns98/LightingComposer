// File: Rendering/Raster/LightBufferBuilder.cs
// Purpose: Enabled scene lights collected into capped GPU light records.

using LightingShowcase.Lighting;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class LightBufferBuilder
{
    /// <summary>Collects enabled scene lights into GPU light records.</summary>
    public RasterLight[] Build(Scene scene)
    {
        List<RasterLight> lights = new();
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;
            lights.Add(new RasterLight(light));
            if (lights.Count >= RasterLimits.MaxGpuLights)
                break;
        }
        return lights.ToArray();
    }
}
