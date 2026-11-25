using LightingShowcase.Composer;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class GlowPresetTests
{
    [Fact]
    public void Presets_HaveUniqueNonEmptyNames()
    {
        Assert.True(GlowPresets.All.Count >= 5);
        Assert.Equal(GlowPresets.All.Count, GlowPresets.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (GlowPreset preset in GlowPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
        }
    }

    [Fact]
    public void Presets_HaveValidValues()
    {
        foreach (GlowPreset preset in GlowPresets.All)
        {
            Assert.InRange(preset.Emission, 0.0, 100000.0);
            Assert.InRange(preset.EmissionColor.X, 0.0, 1.0);
            Assert.InRange(preset.EmissionColor.Y, 0.0, 1.0);
            Assert.InRange(preset.EmissionColor.Z, 0.0, 1.0);
        }
    }

    [Theory]
    [InlineData("Off", 0.0, 1.0, 1.0, 1.0)]
    [InlineData("Warm LED", 3.0, 1.0, 0.722, 0.361)]
    [InlineData("Neon Red", 4.2, 1.0, 0.102, 0.039)]
    public void Presets_SpotCheckValues(string name, double emission, double r, double g, double b)
    {
        GlowPreset preset = GlowPresets.All.First(p => p.Name == name);
        Assert.Equal(emission, preset.Emission);
        Assert.True(Math.Abs(preset.EmissionColor.X - r) < 0.005);
        Assert.True(Math.Abs(preset.EmissionColor.Y - g) < 0.005);
        Assert.True(Math.Abs(preset.EmissionColor.Z - b) < 0.005);
    }
}
