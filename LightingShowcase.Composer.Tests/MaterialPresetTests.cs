using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class MaterialPresetTests
{
    [Fact]
    public void Library_HasUniqueNonEmptyNames()
    {
        Assert.True(MaterialPresetLibrary.Common.Count >= 85);
        Assert.Equal(
            MaterialPresetLibrary.Common.Count,
            MaterialPresetLibrary.Common.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (MaterialPreset preset in MaterialPresetLibrary.Common)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Category));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
            Assert.NotNull(preset.Material);
        }
    }

    [Fact]
    public void Library_CoversExpectedCategories()
    {
        string[] categories = MaterialPresetLibrary.Common.Select(static p => p.Category).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string expected in new[] { "Metals", "Paint", "Plastics", "Glass", "Stone", "Organic", "Liquids", "Emission", "Ceramic", "Gems", "Paper", "Food", "Nature" })
            Assert.Contains(expected, categories);
    }

    [Fact]
    public void Library_HasValidValues()
    {
        foreach (MaterialPreset preset in MaterialPresetLibrary.Common)
        {
            Material material = preset.Material;
            Assert.InRange(material.Color.X, 0.0, 1.0);
            Assert.InRange(material.Color.Y, 0.0, 1.0);
            Assert.InRange(material.Color.Z, 0.0, 1.0);
            Assert.InRange(material.Metallic, 0.0, 1.0);
            Assert.InRange(material.Roughness, 0.02, 1.0);
            Assert.InRange(material.Transmission, 0.0, 1.0);
            Assert.InRange(material.Alpha, 0.0, 1.0);
            Assert.InRange(material.Emission, 0.0, 100000.0);
            Assert.InRange(material.EmissionColor.X, 0.0, 1.0);
            Assert.InRange(material.EmissionColor.Y, 0.0, 1.0);
            Assert.InRange(material.EmissionColor.Z, 0.0, 1.0);
        }
    }

    [Theory]
    [InlineData("Chrome", "Metals", 1.0, 0.06, 0.0)]
    [InlineData("White Porcelain", "Ceramic", 0.0, 0.10, 0.0)]
    [InlineData("Diamond", "Gems", 0.0, 0.02, 0.85)]
    [InlineData("Terracotta", "Stone", 0.0, 0.80, 0.0)]
    [InlineData("Honey", "Liquids", 0.0, 0.10, 0.40)]
    [InlineData("Milk", "Food", 0.0, 0.40, 0.0)]
    public void Library_SpotCheckValues(string name, string category, double metallic, double roughness, double transmission)
    {
        MaterialPreset preset = MaterialPresetLibrary.Common.First(p => p.Name == name);
        Assert.Equal(category, preset.Category);
        Assert.Equal(metallic, preset.Material.Metallic);
        Assert.Equal(roughness, preset.Material.Roughness);
        Assert.Equal(transmission, preset.Material.Transmission);
    }
}
