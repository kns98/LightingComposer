// File: Rendering/Shadow/PixelShader.cs
// Purpose: Per-pixel shading strategy with its sample inputs.

using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal readonly record struct PixelSample(
    Triangle Triangle,
    double Wa,
    double Wb,
    double Wc,
    Vec3 World,
    Vec3 Normal,
    Vec3 CameraPosition,
    IReadOnlyList<PreparedLight> Lights);

internal abstract class PixelShader
{
    /// <summary>Prepares triangle-level values; returns false to skip the triangle.</summary>
    public virtual bool Prepare(Triangle tri, ProjectedVertex a, ProjectedVertex b, ProjectedVertex c) => true;

    /// <summary>Shades one pixel; returns false to skip it.</summary>
    public abstract bool TryShade(in PixelSample sample, out Vec3 shaded);
}
