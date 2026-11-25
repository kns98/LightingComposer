using LightingShowcase.Composer;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class GlassPresetTests
{
    [Fact]
    public void Presets_HaveUniqueNonEmptyNames()
    {
        Assert.True(GlassPresets.All.Count >= 5);
        Assert.Equal(GlassPresets.All.Count, GlassPresets.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (GlassPreset preset in GlassPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
        }
    }

    [Fact]
    public void Presets_HaveValidValues()
    {
        foreach (GlassPreset preset in GlassPresets.All)
        {
            Assert.InRange(preset.Transmission, 0.0, 1.0);
            Assert.True(double.IsFinite(preset.Thickness) && preset.Thickness >= 0.0);
            Assert.InRange(preset.AttenuationColor.X, 0.0, 1.0);
            Assert.InRange(preset.AttenuationColor.Y, 0.0, 1.0);
            Assert.InRange(preset.AttenuationColor.Z, 0.0, 1.0);
            Assert.True(double.IsFinite(preset.AttenuationDistance) && preset.AttenuationDistance >= 0.0);
            Assert.InRange(preset.Ior, 1.0, 2.333);
        }
    }

    [Theory]
    [InlineData("Clear", 0.90, 0.01, 2.0, 1.50)]
    [InlineData("Frosted", 0.60, 0.05, 0.5, 1.50)]
    [InlineData("Amber", 0.70, 0.08, 0.4, 1.50)]
    public void Presets_SpotCheckValues(string name, double transmission, double thickness, double distance, double ior)
    {
        GlassPreset preset = GlassPresets.All.First(p => p.Name == name);
        Assert.Equal(transmission, preset.Transmission);
        Assert.Equal(thickness, preset.Thickness);
        Assert.Equal(distance, preset.AttenuationDistance);
        Assert.Equal(ior, preset.Ior);
    }
}
