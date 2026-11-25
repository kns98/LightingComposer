using LightingShowcase.Composer;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class ColorPresetTests
{
    [Fact]
    public void Presets_HaveUniqueNonEmptyNames()
    {
        Assert.True(ColorPresets.All.Count >= 40);
        Assert.Equal(ColorPresets.All.Count, ColorPresets.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (ColorPreset preset in ColorPresets.All)
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
    }

    [Fact]
    public void Presets_HaveValidChannels()
    {
        foreach (ColorPreset preset in ColorPresets.All)
        {
            Assert.InRange(preset.Color.X, 0.0, 1.0);
            Assert.InRange(preset.Color.Y, 0.0, 1.0);
            Assert.InRange(preset.Color.Z, 0.0, 1.0);
        }
    }

    [Theory]
    [InlineData("Red", 1.0, 0.0, 0.0)]
    [InlineData("White", 1.0, 1.0, 1.0)]
    [InlineData("Black", 0.0, 0.0, 0.0)]
    [InlineData("Gold", 1.0, 0.843, 0.0)]
    [InlineData("Coral", 1.0, 0.498, 0.314)]
    [InlineData("Indigo", 0.294, 0.0, 0.510)]
    [InlineData("Ivory", 1.0, 1.0, 0.941)]
    public void Presets_SpotCheckValues(string name, double r, double g, double b)
    {
        ColorPreset preset = ColorPresets.All.First(p => p.Name == name);
        Assert.True(Math.Abs(preset.Color.X - r) < 0.005);
        Assert.True(Math.Abs(preset.Color.Y - g) < 0.005);
        Assert.True(Math.Abs(preset.Color.Z - b) < 0.005);
    }
}
