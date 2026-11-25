using LightingShowcase.Composer;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class MaterialShowcaseTests
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

    private static Material GroupMaterial(Scene scene, string group) =>
        scene.ObjectGroups.First(g => g.Name == group).FirstMaterialOrDefault()!;

    private static void AssertShowcaseMatches(Scene scene)
    {
        Assert.Equal(MaterialShowcase.PurePresetGroups.Length + MaterialShowcase.ComposedGroupNames.Length, scene.ObjectGroups.Count);
        foreach ((string group, string preset) in MaterialShowcase.PurePresetGroups)
        {
            MaterialPreset? match = MaterialEditorWindow.FindMatchingPreset(
                PresetMatchTests.ModelFrom(GroupMaterial(scene, group)));
            Assert.NotNull(match);
            Assert.Equal(preset, match.Name);
        }
        foreach (string group in MaterialShowcase.ComposedGroupNames)
        {
            Assert.Null(MaterialEditorWindow.FindMatchingPreset(
                PresetMatchTests.ModelFrom(GroupMaterial(scene, group))));
        }
    }

    private static void AssertTextured(Scene scene, string group)
    {
        Material material = GroupMaterial(scene, group);
        Assert.NotNull(material.Texture);
        Assert.NotNull(material.NormalTexture);
        Assert.NotNull(material.MetallicRoughnessTexture);
        Assert.Equal(1024, material.Texture.Width);
        Assert.Equal(1024, material.Texture.Height);
    }

    private static void AssertComposedTabValues(Scene scene)
    {
        DetailEffectPreset velvet = DetailEffectPresets.All.First(p => p.Name == "Velvet");
        Material rug = GroupMaterial(scene, "Rug");
        Assert.Equal(velvet.Specular, rug.Specular);
        Assert.Equal(velvet.SheenColor, rug.SheenColor);
        Assert.Equal(velvet.SheenRoughness, rug.SheenRoughness);

        DetailEffectPreset leather = DetailEffectPresets.All.First(p => p.Name == "Leather Grain");
        Material sofa = GroupMaterial(scene, "Sofa");
        Assert.Equal(leather.Specular, sofa.Specular);
        Assert.Equal(leather.SheenColor, sofa.SheenColor);

        DetailEffectPreset brushed = DetailEffectPresets.All.First(p => p.Name == "Brushed Metal");
        Assert.Equal(brushed.Anisotropy, GroupMaterial(scene, "Floor Lamp").Anisotropy);

        GlassPreset frosted = GlassPresets.All.First(p => p.Name == "Frosted");
        Material window = GroupMaterial(scene, "Window Glass");
        Assert.Equal(frosted.Transmission, window.Transmission);
        Assert.Equal(frosted.Thickness, window.Thickness);
        Assert.Equal(frosted.Ior, window.Ior);
    }

    [Fact]
    public void Builder_MatchesPurePresetsAndComposedTabValues()
    {
        Scene scene = MaterialShowcase.Build();
        AssertShowcaseMatches(scene);
        AssertComposedTabValues(scene);
        foreach (string group in new[] { "Floor", "Left Wall", "Side Table", "Sofa" })
            AssertTextured(scene, group);
    }

    [Fact]
    public void CheckedInFile_MatchesPurePresetsAndComposedTabValues()
    {
        Scene scene = new();
        BinarySceneFile.LoadIntoScene(scene, FindRepoFile("models", "MaterialShowcase", "MaterialShowcase.lscene"));
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);
        AssertShowcaseMatches(scene);
        AssertComposedTabValues(scene);
        foreach (string group in new[] { "Floor", "Left Wall", "Side Table", "Sofa" })
            AssertTextured(scene, group);
    }
}
