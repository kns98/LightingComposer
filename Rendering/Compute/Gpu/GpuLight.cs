// File: Rendering/Compute/Gpu/GpuLight.cs
// Purpose: Packed GPU light record for the compute shader.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.Lighting;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuLight
{
    public readonly Vector4 PositionKind;   // xyz=position, w=0 point / 1 directional / 2 spot
    public readonly Vector4 DirectionRange; // xyz=direction light travels, w=range
    public readonly Vector4 ColorIntensity; // rgb=color, w=intensity
    public readonly Vector4 ConeShadow;     // x=cos inner, y=cos outer, z=castsShadow, w=enabled

    /// <summary>Packs a scene light into GPU format.</summary>
    public GpuLight(SceneLight light)
    {
        float kind = light.Kind switch
        {
            SceneLightKind.Directional => 1.0f,
            SceneLightKind.Spot => 2.0f,
            _ => 0.0f
        };
        PositionKind = light.Position.ToVector4(kind);
        DirectionRange = light.Direction.Normalize().ToVector4((float)light.Range);
        ColorIntensity = light.Color.ToVector4((float)light.Intensity);
        ConeShadow = new Vector4(
            (float)Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)),
            (float)Math.Cos(light.OuterConeAngle),
            light.CastsShadow ? 1.0f : 0.0f,
            light.Enabled ? 1.0f : 0.0f);
    }
}
