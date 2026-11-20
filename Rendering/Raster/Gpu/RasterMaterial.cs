// File: Rendering/Raster/Gpu/RasterMaterial.cs
// Purpose: Packed GPU PBR material with atlas placements for every texture slot.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RasterMaterial
{
    public readonly Vector4 BaseColorAlpha;
    public readonly Vector4 EmissiveFactor;
    public readonly Vector4 PbrFactors;      // x=metallic, y=roughness, z=normal scale, w=occlusion strength
    public readonly Vector4 AlphaFlags;      // x=alpha mode, y=cutoff, z=double sided, w=transmission
    public readonly Vector4 OpticalFactors;  // x=ior, y=thickness, z=clearcoat, w=clearcoat roughness
    public readonly Vector4 AttenuationColorDistance; // rgb=attenuation color, w=distance (0=infinite)
    public readonly Vector4 TextureFlags;    // base, metallic-roughness, normal, emissive
    public readonly Vector4 TextureFlags2;   // x=occlusion, y=transmission, z=clearcoat reuses transmission mask

    public readonly Vector4 BaseAtlasTransform;
    public readonly Vector4 BaseTextureAddress;
    public readonly Vector4 BaseTextureTransform;

    public readonly Vector4 MrAtlasTransform;
    public readonly Vector4 MrTextureAddress;
    public readonly Vector4 MrTextureTransform;

    public readonly Vector4 NormalAtlasTransform;
    public readonly Vector4 NormalTextureAddress;
    public readonly Vector4 NormalTextureTransform;

    public readonly Vector4 EmissiveAtlasTransform;
    public readonly Vector4 EmissiveTextureAddress;
    public readonly Vector4 EmissiveTextureTransform;

    public readonly Vector4 OcclusionAtlasTransform;
    public readonly Vector4 OcclusionTextureAddress;
    public readonly Vector4 OcclusionTextureTransform;

    public readonly Vector4 TransmissionAtlasTransform;
    public readonly Vector4 TransmissionTextureAddress;
    public readonly Vector4 TransmissionTextureTransform;

    /// <summary>Packs a material and its texture placements into GPU constants.</summary>
    public RasterMaterial(
        Material material,
        RasterTexturePlacement basePlacement,
        RasterTexturePlacement mrPlacement,
        RasterTexturePlacement normalPlacement,
        RasterTexturePlacement emissivePlacement,
        RasterTexturePlacement occlusionPlacement,
        RasterTexturePlacement transmissionPlacement)
    {
        BaseColorAlpha = MaterialPacker.ColorAlpha(material);
        EmissiveFactor = MaterialPacker.Emission(material);
        PbrFactors = new Vector4((float)material.Metallic, (float)material.Roughness, (float)material.NormalScale, (float)material.OcclusionStrength);
        AlphaFlags = new Vector4((float)(int)material.AlphaMode, (float)material.AlphaCutoff, material.DoubleSided ? 1.0f : 0.0f, (float)material.Transmission);
        OpticalFactors = new Vector4((float)material.Ior, (float)material.Thickness, (float)material.Clearcoat, (float)material.ClearcoatRoughness);
        AttenuationColorDistance = new Vector4((float)material.AttenuationColor.X, (float)material.AttenuationColor.Y, (float)material.AttenuationColor.Z, (float)material.AttenuationDistance);
        TextureFlags = new Vector4(
            basePlacement.HasTexture ? 1.0f : 0.0f,
            mrPlacement.HasTexture ? 1.0f : 0.0f,
            normalPlacement.HasTexture ? 1.0f : 0.0f,
            emissivePlacement.HasTexture ? 1.0f : 0.0f);
        TextureFlags2 = new Vector4(
            occlusionPlacement.HasTexture ? 1.0f : 0.0f,
            transmissionPlacement.HasTexture ? 1.0f : 0.0f,
            material.ClearcoatUsesTransmissionTexture ? 1.0f : 0.0f,
            0.0f);

        (BaseAtlasTransform, BaseTextureAddress, BaseTextureTransform) = Unpack(basePlacement);
        (MrAtlasTransform, MrTextureAddress, MrTextureTransform) = Unpack(mrPlacement);
        (NormalAtlasTransform, NormalTextureAddress, NormalTextureTransform) = Unpack(normalPlacement);
        (EmissiveAtlasTransform, EmissiveTextureAddress, EmissiveTextureTransform) = Unpack(emissivePlacement);
        (OcclusionAtlasTransform, OcclusionTextureAddress, OcclusionTextureTransform) = Unpack(occlusionPlacement);
        (TransmissionAtlasTransform, TransmissionTextureAddress, TransmissionTextureTransform) = Unpack(transmissionPlacement);

        static (Vector4 Atlas, Vector4 Address, Vector4 Transform) Unpack(RasterTexturePlacement placement) =>
            (placement.AtlasTransform, placement.TextureAddress, placement.TextureTransform);
    }
}
