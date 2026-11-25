using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class SceneMaterialLibraryTests
{
    [Fact]
    public void AddMaterialToLibrary_ValidatesNames()
    {
        Scene scene = new();
        Material material = new(new Vec3(0.8, 0.8, 0.8));

        Assert.Throws<ArgumentException>(() => scene.AddMaterialToLibrary("", material));
        Assert.Throws<ArgumentException>(() => scene.AddMaterialToLibrary("   ", material));
        Assert.Throws<ArgumentException>(() => scene.AddMaterialToLibrary(new string('n', 121), material));

        SceneMaterialEntry entry = scene.AddMaterialToLibrary("  My Look  ", material);
        Assert.Equal("My Look", entry.Name);
        Assert.Throws<ArgumentException>(() => scene.AddMaterialToLibrary("my look", material));
        Assert.Throws<ArgumentException>(() => scene.AddMaterialToLibrary("MY LOOK", material));
    }

    [Fact]
    public void Session_AddSceneMaterial_SavesValuesWithoutTextures()
    {
        ComposerSceneSession session = new();
        MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");

        SceneMaterialEntry entry = session.AddSceneMaterial(
            "Show Chrome", chrome.Material.Color, PresetMatchTests.PropsFrom(chrome.Material));

        Assert.Equal("Show Chrome", entry.Name);
        Assert.False(entry.Material.HasAnyTexture);
        Assert.Equal(chrome.Material.Color, entry.Material.Color);
        Assert.Equal(chrome.Material.Metallic, entry.Material.Metallic);
        Assert.Equal(chrome.Material.Roughness, entry.Material.Roughness);
        Assert.Single(session.GetSceneMaterials());
        Assert.Throws<ArgumentException>(() => session.AddSceneMaterial(
            "show chrome", chrome.Material.Color, PresetMatchTests.PropsFrom(chrome.Material)));
    }

    [Fact]
    public void BinaryScene_RoundTripsMaterialLibrary()
    {
        string directory = Directory.CreateTempSubdirectory("binary-lib-test").FullName;
        try
        {
            MaterialPreset chrome = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");
            Material single = new(new Vec3(0.2, 0.3, 0.4), doubleSided: false, roughness: 0.77);
            Scene scene = new();
            scene.BeginGroup("obj");
            scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4), chrome.Material);
            scene.EndGroup();
            scene.AddMaterialToLibrary("Show Chrome", chrome.Material);
            scene.AddMaterialToLibrary("Single Flat", single);

            string path = Path.Combine(directory, "lib.lscn");
            BinarySceneFile.Save(scene, path);

            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, path);

            Assert.Equal(2, loaded.MaterialLibrary.Count);
            SceneMaterialEntry reloaded = loaded.MaterialLibrary.First(e => e.Name == "Show Chrome");
            Assert.Equal(chrome.Material.Color, reloaded.Material.Color);
            Assert.Equal(chrome.Material.Metallic, reloaded.Material.Metallic);
            Assert.Equal(chrome.Material.Roughness, reloaded.Material.Roughness);
            Assert.True(reloaded.Material.DoubleSided);
            SceneMaterialEntry flat = loaded.MaterialLibrary.First(e => e.Name == "Single Flat");
            Assert.Equal(0.77, flat.Material.Roughness, precision: 9);
            Assert.False(flat.Material.DoubleSided);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
