// File: Rendering/Raster/MaterialPacker.cs
// Purpose: Pure material-to-GPU-vector packing used by geometry and material builders.

using System.Numerics;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal static class MaterialPacker
{
    /// <summary>Packs a material base color and effective alpha into a vector.</summary>
    public static Vector4 ColorAlpha(Material material) => new(
        (float)Math.Clamp(material.Color.X, 0.0, 1.0),
        (float)Math.Clamp(material.Color.Y, 0.0, 1.0),
        (float)Math.Clamp(material.Color.Z, 0.0, 1.0),
        (float)Math.Clamp(EffectiveAlpha(material), 0.0, 1.0));

    /// <summary>Packs a material emission color and strength into a vector.</summary>
    public static Vector4 Emission(Material material) => new(
        (float)Math.Max(0.0, material.EmissionColor.X),
        (float)Math.Max(0.0, material.EmissionColor.Y),
        (float)Math.Max(0.0, material.EmissionColor.Z),
        (float)Math.Max(0.0, material.Emission));

    /// <summary>Returns true when a material uses alpha blend mode.</summary>
    public static bool IsTransparent(Material material)
    {
        // KHR_materials_transmission does not make a surface geometrically
        // transparent. OPAQUE and MASK transmission materials still represent
        // a covered surface and must write depth. Sending them through the
        // alpha-blended, depth-write-disabled pass lets farther lamp parts draw
        // over the nearer glass as the camera orbits. Only genuine alpha BLEND
        // materials belong in the transparent pass.
        return material.AlphaMode == MaterialAlphaMode.Blend;
    }

    private static double EffectiveAlpha(Material material)
    {
        // Match ShadowRasterRenderer, not the ray tracer: the fast CPU raster mode
        // uses base alpha only as a discard mask and does not reduce opacity for
        // transmission. Treating transmissive/glass materials as blended in the
        // Vulkan preview was the main source of objects looking like they were
        // drawing through each other.
        return Math.Clamp(material.Alpha, 0.0, 1.0);
    }
}
