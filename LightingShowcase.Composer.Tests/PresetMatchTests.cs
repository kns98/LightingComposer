using Avalonia.Controls;
using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class PresetMatchTests
{
    internal static ComposerMaterialModel ModelFrom(Material material) => new(
        ObjectId: 1,
        ObjectName: "Test",
        BaseColor: material.Color,
        Metallic: material.Metallic,
        Roughness: material.Roughness,
        Transmission: material.Transmission,
        Alpha: material.Alpha,
        Emission: material.Emission,
        EmissionColor: material.EmissionColor,
        AlphaMode: material.AlphaMode,
        AlphaCutoff: material.AlphaCutoff,
        DoubleSided: material.DoubleSided,
        Ior: material.Ior,
        Thickness: material.Thickness,
        AttenuationColor: material.AttenuationColor,
        AttenuationDistance: material.AttenuationDistance,
        Clearcoat: material.Clearcoat,
        ClearcoatRoughness: material.ClearcoatRoughness,
        NormalScale: material.NormalScale,
        OcclusionStrength: material.OcclusionStrength,
        Specular: material.Specular,
        SpecularColor: material.SpecularColor,
        SheenColor: material.SheenColor,
        SheenRoughness: material.SheenRoughness,
        Anisotropy: material.Anisotropy,
        AnisotropyRotation: material.AnisotropyRotation,
        Iridescence: material.Iridescence,
        IridescenceIor: material.IridescenceIor,
        IridescenceThicknessMinimum: material.IridescenceThicknessMinimum,
        IridescenceThicknessMaximum: material.IridescenceThicknessMaximum,
        TextureName: null,
        TexturePath: null,
        HasStoredTextureProjection: false,
        UsesBoxProjection: false,
        TextureTileMeters: 0.25,
        TextureSlots: Array.Empty<ComposerTextureSlotModel>());

    [Fact]
    public void ExactMatch_ReturnsLibraryPreset()
    {
        MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");

        MaterialPreset? match = MaterialEditorWindow.FindMatchingPreset(ModelFrom(chrome.Material));

        Assert.Same(chrome, match);
    }

    [Fact]
    public void DeviatingRoughness_ReturnsNull()
    {
        MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");
        ComposerMaterialModel model = ModelFrom(chrome.Material) with { Roughness = 0.5 };

        Assert.Null(MaterialEditorWindow.FindMatchingPreset(model));
    }

    [Fact]
    public void DeviatingColor_ReturnsNull()
    {
        MaterialPreset milk = MaterialPresetLibrary.Common.First(p => p.Name == "Milk");
        ComposerMaterialModel model = ModelFrom(milk.Material) with { BaseColor = new Vec3(0.1, 0.2, 0.3) };

        Assert.Null(MaterialEditorWindow.FindMatchingPreset(model));
    }

    [Fact]
    public void DeviatingDoubleSided_StillMatches()
    {
        MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");
        ComposerMaterialModel model = ModelFrom(chrome.Material) with { DoubleSided = false };

        Assert.Same(chrome, MaterialEditorWindow.FindMatchingPreset(model));
    }

    [Fact]
    public void AppliedPreset_AlwaysMatches()
    {
        Material target = new(new Vec3(0.1, 0.2, 0.3), doubleSided: false, roughness: 0.9);
        MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");

        Material applied = target.WithPreset(chrome.Material);

        Assert.Same(chrome, MaterialEditorWindow.FindMatchingPreset(ModelFrom(applied)));
    }

    internal static ComposerMaterialProperties PropsFrom(Material material) => new(
        Metallic: material.Metallic,
        Roughness: material.Roughness,
        Transmission: material.Transmission,
        Alpha: material.Alpha,
        Emission: material.Emission,
        EmissionColor: material.EmissionColor,
        AlphaMode: material.AlphaMode,
        AlphaCutoff: material.AlphaCutoff,
        DoubleSided: material.DoubleSided,
        Ior: material.Ior,
        Thickness: material.Thickness,
        AttenuationColor: material.AttenuationColor,
        AttenuationDistance: material.AttenuationDistance,
        Clearcoat: material.Clearcoat,
        ClearcoatRoughness: material.ClearcoatRoughness,
        NormalScale: material.NormalScale,
        OcclusionStrength: material.OcclusionStrength,
        Specular: material.Specular,
        SpecularColor: material.SpecularColor,
        SheenColor: material.SheenColor,
        SheenRoughness: material.SheenRoughness,
        Anisotropy: material.Anisotropy,
        AnisotropyRotation: material.AnisotropyRotation,
        Iridescence: material.Iridescence,
        IridescenceIor: material.IridescenceIor,
        IridescenceThicknessMinimum: material.IridescenceThicknessMinimum,
        IridescenceThicknessMaximum: material.IridescenceThicknessMaximum);

    [Fact]
    public void DialogValues_MatchLibraryPreset()
    {
        MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");

        MaterialPreset? match = MaterialEditorWindow.FindMatchingPreset(chrome.Material.Color, PropsFrom(chrome.Material));

        Assert.Same(chrome, match);
    }

    [Fact]
    public void DialogValues_Deviation_ReturnsNull()
    {
        MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");
        ComposerMaterialProperties deviating = PropsFrom(chrome.Material) with { Roughness = 0.5 };

        Assert.Null(MaterialEditorWindow.FindMatchingPreset(chrome.Material.Color, deviating));
        Assert.Null(MaterialEditorWindow.FindMatchingPreset(new Vec3(0.1, 0.2, 0.3), PropsFrom(chrome.Material)));
    }

    [Fact]
    public void FinishPicker_HighlightsExactlyOnePreset()
    {
        FinishPresetPicker picker = new();
        FinishPreset first = FinishPresets.All[0];

        picker.SetSelected(first);

        Assert.Equal(1, picker.Children.OfType<Button>().Count(static b => b.Content is TextBlock));
        picker.SetSelected(null);
        Assert.Equal(0, picker.Children.OfType<Button>().Count(static b => b.Content is TextBlock));
    }

    [Fact]
    public void ColorPicker_HighlightsExactlyOneSwatch()
    {
        ColorPresetPicker picker = new();
        ColorPreset first = ColorPresets.All[0];

        picker.SetSelected(first);

        Assert.Equal(1, picker.Children.OfType<Button>().Count(static b => b.BorderBrush != null));
        picker.SetSelected(null);
        Assert.Equal(0, picker.Children.OfType<Button>().Count(static b => b.BorderBrush != null));
    }

    [Fact]
    public void ScalarMatchers_MatchEveryEntry()
    {
        foreach (FinishPreset p in FinishPresets.All)
            Assert.Same(p, MaterialEditorWindow.MatchFinishPreset(p.Metallic, p.Roughness));
        foreach (AlphaPreset p in AlphaPresets.All)
            Assert.Same(p, MaterialEditorWindow.MatchAlphaPreset(p.AlphaMode, p.Alpha, p.AlphaCutoff));
        foreach (GlowPreset p in GlowPresets.All)
            Assert.Same(p, MaterialEditorWindow.MatchGlowPreset(p.Emission, p.EmissionColor));
        foreach (GlassPreset p in GlassPresets.All)
            Assert.Same(p, MaterialEditorWindow.MatchGlassPreset(p.Transmission, p.Thickness, p.AttenuationColor, p.AttenuationDistance, p.Ior));
        foreach (ColorPreset p in ColorPresets.All)
            Assert.Same(p, MaterialEditorWindow.MatchColorPreset(p.Color));
        foreach (MappingPreset p in MappingPresets.All)
        {
            ComposerTextureSlotModel slot = new(
                MaterialTextureSlot.BaseColor, string.Empty, null, null,
                p.OffsetU, p.OffsetV, p.ScaleU, p.ScaleV, p.RotationDegrees, p.WrapU, p.WrapV);
            Assert.Same(p, MaterialEditorWindow.MatchMappingPreset(p.BoxProjection, p.TileMeters, slot));
        }
    }

    [Fact]
    public void DetailMatcher_MatchesEveryEntry()
    {
        ComposerMaterialProperties defaults = PropsFrom(new Material(new Vec3(1, 1, 1)));
        foreach (DetailEffectPreset p in DetailEffectPresets.All)
        {
            ComposerMaterialProperties props = defaults with
            {
                NormalScale = p.NormalScale,
                OcclusionStrength = p.OcclusionStrength,
                Specular = p.Specular,
                SpecularColor = p.SpecularColor,
                SheenColor = p.SheenColor,
                SheenRoughness = p.SheenRoughness,
                Anisotropy = p.Anisotropy,
                AnisotropyRotation = p.AnisotropyRotation,
                Iridescence = p.Iridescence,
                IridescenceIor = p.IridescenceIor,
                IridescenceThicknessMinimum = p.IridescenceThicknessMinimum,
                IridescenceThicknessMaximum = p.IridescenceThicknessMaximum,
            };
            Assert.Same(p, MaterialEditorWindow.MatchDetailEffectPreset(props));
        }
    }
}
