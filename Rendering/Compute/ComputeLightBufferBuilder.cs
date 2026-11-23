// File: Rendering/Compute/ComputeLightBufferBuilder.cs
// Purpose: First enabled scene lights packed into capped GPU records.

using LightingShowcase.Lighting;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class ComputeLightBufferBuilder
{
    private const int MaxGpuLights = 32;

    /// <summary>Builds the GPU light buffer from enabled scene lights.</summary>
    public GpuLight[] Build(Scene scene, out int sourceLightCount)
    {
        sourceLightCount = scene.Lights.Count;
        List<GpuLight> result = new(Math.Min(sourceLightCount, MaxGpuLights));
        for (int i = 0; i < result.Capacity; i++)
        {
            SceneLight light = scene.Lights[i];
            if (light.Enabled)
                result.Add(new GpuLight(light));
        }
        return result.ToArray();
    }
}
