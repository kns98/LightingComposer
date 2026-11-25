using LightingShowcase.Composer;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class DetailEffectPresetTests
{
    [Fact]
    public void Presets_HaveUniqueNonEmptyNames()
    {
        Assert.True(DetailEffectPresets.All.Count >= 5);
        Assert.Equal(DetailEffectPresets.All.Count, DetailEffectPresets.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (DetailEffectPreset preset in DetailEffectPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
        }
    }

    [Fact]
    public void Presets_HaveValidValues()
    {
        foreach (DetailEffectPreset preset in DetailEffectPresets.All)
        {
            Assert.InRange(preset.NormalScale, -8.0, 8.0);
            Assert.InRange(preset.OcclusionStrength, 0.0, 1.0);
            Assert.InRange(preset.Specular, 0.0, 1.0);
            Assert.InRange(preset.SpecularColor.X, 0.0, 1.0);
            Assert.InRange(preset.SpecularColor.Y, 0.0, 1.0);
            Assert.InRange(preset.SpecularColor.Z, 0.0, 1.0);
            Assert.InRange(preset.SheenColor.X, 0.0, 1.0);
            Assert.InRange(preset.SheenColor.Y, 0.0, 1.0);
            Assert.InRange(preset.SheenColor.Z, 0.0, 1.0);
            Assert.InRange(preset.SheenRoughness, 0.0, 1.0);
            Assert.InRange(preset.Anisotropy, 0.0, 1.0);
            Assert.True(double.IsFinite(preset.AnisotropyRotation));
            Assert.InRange(preset.Iridescence, 0.0, 1.0);
            Assert.True(double.IsFinite(preset.IridescenceIor) && preset.IridescenceIor >= 1.0);
            Assert.True(double.IsFinite(preset.IridescenceThicknessMinimum) && preset.IridescenceThicknessMinimum >= 0.0);
            Assert.True(double.IsFinite(preset.IridescenceThicknessMaximum) && preset.IridescenceThicknessMaximum >= 0.0);
            Assert.True(preset.IridescenceThicknessMinimum <= preset.IridescenceThicknessMaximum);
        }
    }

    [Theory]
    [InlineData("Flat", 1.0, 0.0, 0.0)]
    [InlineData("Brushed Metal", 1.0, 0.8, 0.0)]
    [InlineData("Soap Film", 1.0, 0.0, 0.9)]
    [InlineData("Oil Slick", 0.9, 0.3, 1.0)]
    public void Presets_SpotCheckValues(string name, double specular, double anisotropy, double iridescence)
    {
        DetailEffectPreset preset = DetailEffectPresets.All.First(p => p.Name == name);
        Assert.Equal(specular, preset.Specular);
        Assert.Equal(anisotropy, preset.Anisotropy);
        Assert.Equal(iridescence, preset.Iridescence);
    }
}
