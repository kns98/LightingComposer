// File: Rendering/Compute/Gpu/GpuTriangle.cs
// Purpose: Packed GPU triangle with material and smooth normals.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuTriangle
{
    // Position and UV padding components carry the three authored vertex
    // normals. This preserves the original 11-vec4 record size while giving
    // the ray tracer smooth glTF shading without another scene-sized buffer.
    public readonly Vector4 A; // xyz=position A, w=normal A.x
    public readonly Vector4 B; // xyz=position B, w=normal A.y
    public readonly Vector4 C; // xyz=position C, w=normal A.z
    public readonly Vector4 BaseColor; // rgb=linear factor, a=alpha factor
    public readonly Vector4 Emission; // rgb=linear factor, a=strength
    public readonly Vector4 MaterialParams; // x=metallic, y=roughness, z=transmission, w=normal scale
    public readonly Vector4 UvA; // xy=UV A, zw=normal B.xy
    public readonly Vector4 UvB; // xy=UV B, z=normal B.z, w=normal C.x
    public readonly Vector4 UvC; // xy=UV C, zw=normal C.yz
    public readonly Vector4 TextureInfo; // x=base index, y=emissive index, z=alpha/double-sided flags, w=alpha cutoff
    public readonly Vector4 TextureInfo2; // x=MR index, y=normal index, z=occlusion index, w=occlusion strength

    /// <summary>Packs a scene triangle and its material into GPU format.</summary>
    public GpuTriangle(Triangle triangle, IReadOnlyDictionary<TextureMap, int> textureIds)
    {
        Vec3 normalA = triangle.NormalA.Normalize();
        Vec3 normalB = triangle.NormalB.Normalize();
        Vec3 normalC = triangle.NormalC.Normalize();
        A = triangle.A.ToVector4((float)normalA.X);
        B = triangle.B.ToVector4((float)normalA.Y);
        C = triangle.C.ToVector4((float)normalA.Z);
        UvA = new Vector4((float)triangle.UvA.U, (float)triangle.UvA.V, (float)normalB.X, (float)normalB.Y);
        UvB = new Vector4((float)triangle.UvB.U, (float)triangle.UvB.V, (float)normalB.Z, (float)normalC.X);
        UvC = new Vector4((float)triangle.UvC.U, (float)triangle.UvC.V, (float)normalC.Y, (float)normalC.Z);

        Material material = triangle.Material;
        Vec3 colorFactor = material.Color;
        BaseColor = new Vector4(
            (float)Math.Clamp(colorFactor.X, 0.0, 64.0),
            (float)Math.Clamp(colorFactor.Y, 0.0, 64.0),
            (float)Math.Clamp(colorFactor.Z, 0.0, 64.0),
            (float)Math.Clamp(material.Alpha, 0.0, 1.0));

        Emission = new Vector4(
            (float)Math.Max(0.0, material.EmissionColor.X),
            (float)Math.Max(0.0, material.EmissionColor.Y),
            (float)Math.Max(0.0, material.EmissionColor.Z),
            (float)Math.Max(0.0, material.Emission));

        MaterialParams = new Vector4(
            (float)Math.Clamp(material.Metallic, 0.0, 1.0),
            (float)Math.Clamp(material.Roughness, 0.02, 1.0),
            (float)Math.Clamp(material.Transmission, 0.0, 1.0),
            (float)material.NormalScale);

        int alphaFlags = (int)material.AlphaMode | (material.DoubleSided ? 4 : 0);
        TextureInfo = new Vector4(
            TextureIndex(textureIds, material.Texture),
            TextureIndex(textureIds, material.EmissiveTexture),
            alphaFlags,
            (float)material.AlphaCutoff);
        TextureInfo2 = new Vector4(
            TextureIndex(textureIds, material.MetallicRoughnessTexture),
            TextureIndex(textureIds, material.NormalTexture),
            TextureIndex(textureIds, material.OcclusionTexture),
            (float)material.OcclusionStrength);
    }

    /// <summary>Returns the GPU texture index for a texture map.</summary>
    private static int TextureIndex(IReadOnlyDictionary<TextureMap, int> textureIds, TextureMap? texture)
    {
        if (texture == null)
            return -1;
        return textureIds.TryGetValue(texture, out int textureIndex) ? textureIndex : -1;
    }
}
