using LightingShowcase.Composer;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class AlphaPresetTests
{
    [Fact]
    public void Presets_HaveUniqueNonEmptyNames()
    {
        Assert.True(AlphaPresets.All.Count >= 5);
        Assert.Equal(AlphaPresets.All.Count, AlphaPresets.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (AlphaPreset preset in AlphaPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
        }
    }

    [Fact]
    public void Presets_HaveValidValues()
    {
        foreach (AlphaPreset preset in AlphaPresets.All)
        {
            Assert.True(Enum.IsDefined(preset.AlphaMode));
            Assert.InRange(preset.Alpha, 0.0, 1.0);
            Assert.InRange(preset.AlphaCutoff, 0.0, 1.0);
        }
    }

    [Theory]
    [InlineData("Opaque", MaterialAlphaMode.Opaque, 1.0, 0.5)]
    [InlineData("Cutout Hard", MaterialAlphaMode.Mask, 1.0, 0.5)]
    [InlineData("Ghost", MaterialAlphaMode.Blend, 0.25, 0.5)]
    public void Presets_SpotCheckValues(string name, MaterialAlphaMode mode, double alpha, double cutoff)
    {
        AlphaPreset preset = AlphaPresets.All.First(p => p.Name == name);
        Assert.Equal(mode, preset.AlphaMode);
        Assert.Equal(alpha, preset.Alpha);
        Assert.Equal(cutoff, preset.AlphaCutoff);
    }
}
