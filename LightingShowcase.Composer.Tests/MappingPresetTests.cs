using LightingShowcase.Composer;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class MappingPresetTests
{
    [Fact]
    public void Presets_HaveUniqueNonEmptyNames()
    {
        Assert.True(MappingPresets.All.Count >= 5);
        Assert.Equal(MappingPresets.All.Count, MappingPresets.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (MappingPreset preset in MappingPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
        }
    }

    [Fact]
    public void Presets_HaveValidValues()
    {
        foreach (MappingPreset preset in MappingPresets.All)
        {
            Assert.True(double.IsFinite(preset.TileMeters) && preset.TileMeters > 0.0);
            Assert.True(double.IsFinite(preset.OffsetU));
            Assert.True(double.IsFinite(preset.OffsetV));
            Assert.True(double.IsFinite(preset.ScaleU));
            Assert.True(double.IsFinite(preset.ScaleV));
            Assert.True(double.IsFinite(preset.RotationDegrees));
            Assert.True(Enum.IsDefined(preset.WrapU));
            Assert.True(Enum.IsDefined(preset.WrapV));
        }
    }

    [Theory]
    [InlineData("Authored UVs", false, 1.0, 1.0, TextureAddressMode.Repeat, TextureAddressMode.Repeat)]
    [InlineData("Tile 2x", false, 2.0, 2.0, TextureAddressMode.Repeat, TextureAddressMode.Repeat)]
    [InlineData("Clamp Single", false, 1.0, 1.0, TextureAddressMode.ClampToEdge, TextureAddressMode.ClampToEdge)]
    public void Presets_SpotCheckValues(string name, bool box, double su, double sv, TextureAddressMode wu, TextureAddressMode wv)
    {
        MappingPreset preset = MappingPresets.All.First(p => p.Name == name);
        Assert.Equal(box, preset.BoxProjection);
        Assert.Equal(su, preset.ScaleU);
        Assert.Equal(sv, preset.ScaleV);
        Assert.Equal(wu, preset.WrapU);
        Assert.Equal(wv, preset.WrapV);
    }
}
