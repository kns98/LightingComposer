// -----------------------------------------------------------------------------
// File: Scene/MaterialDefinition.cs
// Purpose: Renderer-independent PBR material record.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Canonical PBR-style material definition used before adapting to a concrete renderer.</summary>
public sealed class MaterialDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Material";
    public Vec3 BaseColor { get; set; } = new(1, 1, 1);
    public string? BaseColorTextureId { get; set; }
    public double Metallic { get; set; }
    public double Roughness { get; set; } = 0.72;
    public string? MetallicRoughnessTextureId { get; set; }
    public string? NormalTextureId { get; set; }
    public double NormalScale { get; set; } = 1.0;
    public string? OcclusionTextureId { get; set; }
    public double OcclusionStrength { get; set; } = 1.0;
    public string? TransmissionTextureId { get; set; }
    public Vec3 EmissiveColor { get; set; } = new(1, 1, 1);
    public string? EmissiveTextureId { get; set; }
    public double EmissiveStrength { get; set; }
    public double Opacity { get; set; } = 1.0;
    public bool AlphaBlend { get; set; }
    public MaterialAlphaMode AlphaMode { get; set; } = MaterialAlphaMode.Opaque;
    public double AlphaCutoff { get; set; } = 0.5;
    public double Transmission { get; set; }
    public double Ior { get; set; } = 1.5;
    public double Thickness { get; set; }
    public Vec3 AttenuationColor { get; set; } = new(1, 1, 1);
    public double AttenuationDistance { get; set; }
    public double Clearcoat { get; set; }
    public double ClearcoatRoughness { get; set; }
    public bool ClearcoatUsesTransmissionTexture { get; set; }
    public bool DoubleSided { get; set; } = true;
    public double Specular { get; set; } = 1.0;
    public Vec3 SpecularColor { get; set; } = new(1, 1, 1);
    public string? SpecularTextureId { get; set; }
    public string? SpecularColorTextureId { get; set; }
    public Vec3 SheenColor { get; set; } = new(0, 0, 0);
    public double SheenRoughness { get; set; }
    public string? SheenColorTextureId { get; set; }
    public string? SheenRoughnessTextureId { get; set; }
    public double Anisotropy { get; set; }
    public double AnisotropyRotation { get; set; }
    public string? AnisotropyTextureId { get; set; }
    public double Iridescence { get; set; }
    public double IridescenceIor { get; set; } = 1.3;
    public double IridescenceThicknessMinimum { get; set; } = 100.0;
    public double IridescenceThicknessMaximum { get; set; } = 400.0;
    public string? IridescenceTextureId { get; set; }
    public string? IridescenceThicknessTextureId { get; set; }

    /// <summary>Creates a material definition from the given renderer material.</summary>
    public static MaterialDefinition FromMaterial(Material material, string id, string name)
    {
        if (material == null) throw new ArgumentNullException(nameof(material));
        return new MaterialDefinition
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Name = string.IsNullOrWhiteSpace(name) ? "Material" : name,
            BaseColor = material.Color,
            BaseColorTextureId = material.Texture?.Name,
            Metallic = material.Metallic,
            Roughness = material.Roughness,
            MetallicRoughnessTextureId = material.MetallicRoughnessTexture?.Name,
            NormalTextureId = material.NormalTexture?.Name,
            NormalScale = material.NormalScale,
            OcclusionTextureId = material.OcclusionTexture?.Name,
            OcclusionStrength = material.OcclusionStrength,
            TransmissionTextureId = material.TransmissionTexture?.Name,
            EmissiveColor = material.EmissionColor,
            EmissiveTextureId = material.EmissiveTexture?.Name,
            EmissiveStrength = material.Emission,
            Opacity = material.Alpha,
            AlphaBlend = material.AlphaBlend,
            AlphaMode = material.AlphaMode,
            AlphaCutoff = material.AlphaCutoff,
            Transmission = material.Transmission,
            Ior = material.Ior,
            Thickness = material.Thickness,
            AttenuationColor = material.AttenuationColor,
            AttenuationDistance = material.AttenuationDistance,
            Clearcoat = material.Clearcoat,
            ClearcoatRoughness = material.ClearcoatRoughness,
            ClearcoatUsesTransmissionTexture = material.ClearcoatUsesTransmissionTexture,
            DoubleSided = material.DoubleSided,
            Specular = material.Specular,
            SpecularColor = material.SpecularColor,
            SpecularTextureId = material.SpecularTexture?.Name,
            SpecularColorTextureId = material.SpecularColorTexture?.Name,
            SheenColor = material.SheenColor,
            SheenRoughness = material.SheenRoughness,
            SheenColorTextureId = material.SheenColorTexture?.Name,
            SheenRoughnessTextureId = material.SheenRoughnessTexture?.Name,
            Anisotropy = material.Anisotropy,
            AnisotropyRotation = material.AnisotropyRotation,
            AnisotropyTextureId = material.AnisotropyTexture?.Name,
            Iridescence = material.Iridescence,
            IridescenceIor = material.IridescenceIor,
            IridescenceThicknessMinimum = material.IridescenceThicknessMinimum,
            IridescenceThicknessMaximum = material.IridescenceThicknessMaximum,
            IridescenceTextureId = material.IridescenceTexture?.Name,
            IridescenceThicknessTextureId = material.IridescenceThicknessTexture?.Name
        };
    }

    /// <summary>Converts this definition back into a renderer material with the given textures.</summary>
    public Material ToMaterial(
        TextureMap? baseColorTexture = null,
        TextureMap? emissiveTexture = null,
        TextureMap? metallicRoughnessTexture = null,
        TextureMap? normalTexture = null,
        TextureMap? occlusionTexture = null,
        TextureMap? transmissionTexture = null,
        TextureMap? specularTexture = null,
        TextureMap? specularColorTexture = null,
        TextureMap? sheenColorTexture = null,
        TextureMap? sheenRoughnessTexture = null,
        TextureMap? anisotropyTexture = null,
        TextureMap? iridescenceTexture = null,
        TextureMap? iridescenceThicknessTexture = null)
    {
        return new Material(
            BaseColor, EmissiveStrength, null, baseColorTexture, EmissiveColor, emissiveTexture,
            Opacity, AlphaBlend, Metallic, Roughness, Transmission, metallicRoughnessTexture,
            normalTexture, occlusionTexture, NormalScale, OcclusionStrength, AlphaMode, AlphaCutoff, DoubleSided,
            transmissionTexture, Ior, Thickness, AttenuationColor, AttenuationDistance, Clearcoat,
            ClearcoatRoughness, ClearcoatUsesTransmissionTexture,
            Specular, SpecularColor, specularTexture, specularColorTexture,
            SheenColor, SheenRoughness, sheenColorTexture, sheenRoughnessTexture,
            Anisotropy, AnisotropyRotation, anisotropyTexture,
            Iridescence, IridescenceIor, IridescenceThicknessMinimum, IridescenceThicknessMaximum,
            iridescenceTexture, iridescenceThicknessTexture);
    }
}
