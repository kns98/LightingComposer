using LightingShowcase.Composer;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class FinishPresetTests
{
    [Fact]
    public void Presets_HaveUniqueNonEmptyNames()
    {
        Assert.True(FinishPresets.All.Count >= 5);
        Assert.Equal(FinishPresets.All.Count, FinishPresets.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (FinishPreset preset in FinishPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
        }
    }

    [Fact]
    public void Presets_HaveValidValues()
    {
        foreach (FinishPreset preset in FinishPresets.All)
        {
            Assert.InRange(preset.Metallic, 0.0, 1.0);
            Assert.InRange(preset.Roughness, 0.02, 1.0);
        }
    }

    [Theory]
    [InlineData("Matte", 0.0, 0.90)]
    [InlineData("Gloss", 0.0, 0.12)]
    [InlineData("Mirror", 1.0, 0.05)]
    [InlineData("Brushed Metal", 1.0, 0.35)]
    public void Presets_SpotCheckValues(string name, double metallic, double roughness)
    {
        FinishPreset preset = FinishPresets.All.First(p => p.Name == name);
        Assert.Equal(metallic, preset.Metallic);
        Assert.Equal(roughness, preset.Roughness);
    }
}
