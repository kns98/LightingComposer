using LightingShowcase.Composer;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class TexturePresetTests
{
    private static string FindRepoFile(params string[] parts)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        return Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());
    }

    private static IEnumerable<string> AllFiles()
    {
        foreach (TexturePreset preset in TexturePresetLibrary.All)
        {
            yield return preset.AlbedoFile;
            if (preset.NormalFile != null)
                yield return preset.NormalFile;
            if (preset.RoughnessFile != null)
                yield return preset.RoughnessFile;
        }
    }

    [Fact]
    public void Library_HasUniqueNonEmptyNames()
    {
        Assert.True(TexturePresetLibrary.All.Count >= 6);
        Assert.Equal(TexturePresetLibrary.All.Count, TexturePresetLibrary.All.Select(static p => p.Name).Distinct(StringComparer.Ordinal).Count());
        foreach (TexturePreset preset in TexturePresetLibrary.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Category));
            Assert.False(string.IsNullOrWhiteSpace(preset.Summary));
            Assert.True(Enum.IsDefined(preset.DefaultSlot));
        }
    }

    [Fact]
    public void Library_FilesExist()
    {
        foreach (string file in AllFiles())
            Assert.True(File.Exists(FindRepoFile(file)), $"Missing library file: {file}");
    }

    [Fact]
    public void Library_FilesDecodeAtExpectedDimensions()
    {
        foreach (string file in AllFiles())
        {
            TextureMap map = TextureMap.FromFile(FindRepoFile(file));
            Assert.Equal(1024, map.Width);
            Assert.Equal(1024, map.Height);
        }
    }

    [Fact]
    public void Library_PathsResolve()
    {
        foreach (string file in AllFiles())
            Assert.NotNull(TexturePresetLibrary.ResolvePath(file));
    }

    [Fact]
    public void Library_FilesAreCoveredByLicenses()
    {
        string licenses = File.ReadAllText(FindRepoFile("textures", "TEXTURES_LICENSES.md"));
        Assert.Contains("CC0", licenses, StringComparison.Ordinal);
        foreach (string file in AllFiles())
            Assert.Contains(Path.GetFileName(file), licenses, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Red Brick Wall", "Brick", MaterialTextureSlot.BaseColor)]
    [InlineData("White Marble", "Stone", MaterialTextureSlot.BaseColor)]
    [InlineData("Metal Plate", "Metal", MaterialTextureSlot.BaseColor)]
    public void Library_SpotCheckEntries(string name, string category, MaterialTextureSlot slot)
    {
        TexturePreset preset = TexturePresetLibrary.All.First(p => p.Name == name);
        Assert.Equal(category, preset.Category);
        Assert.Equal(slot, preset.DefaultSlot);
        Assert.NotNull(preset.NormalFile);
        Assert.NotNull(preset.RoughnessFile);
    }
}
