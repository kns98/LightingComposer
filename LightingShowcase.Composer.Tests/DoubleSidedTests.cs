using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class DoubleSidedTests
{
    [Fact]
    public void NewMaterials_DefaultToDoubleSided()
    {
        Assert.True(new Material(new Vec3(0.8, 0.8, 0.8)).DoubleSided);
        Assert.True(new MaterialDefinition().DoubleSided);
    }

    [Fact]
    public void Presets_AreDoubleSided()
    {
        Assert.NotEmpty(MaterialPresetLibrary.Common);
        foreach (MaterialPreset preset in MaterialPresetLibrary.Common)
            Assert.True(preset.Material.DoubleSided, $"Preset not double-sided: {preset.Category} - {preset.Name}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithPreset_PreservesDoubleSidedChoice(bool doubleSided)
    {
        Material target = new(new Vec3(0.5, 0.5, 0.5), doubleSided: doubleSided);
        MaterialPreset preset = MaterialPresetLibrary.Common.First(p => p.Name == "Chrome");

        Material applied = target.WithPreset(preset.Material);

        Assert.Equal(doubleSided, applied.DoubleSided);
        Assert.Equal(preset.Material.Metallic, applied.Metallic);
    }

    [Fact]
    public void BinaryScene_RoundTripsExplicitSingleSided()
    {
        string directory = Directory.CreateTempSubdirectory("binary-sided-test").FullName;
        try
        {
            Material single = new(new Vec3(0.9, 0.8, 0.7), doubleSided: false);
            Material both = new(new Vec3(0.7, 0.8, 0.9));
            Scene scene = new();
            scene.BeginGroup("sided");
            scene.AddTriangle(new Vec3(-1, -1, 4), new Vec3(1, -1, 4), new Vec3(1, 1, 4), single);
            scene.AddTriangle(new Vec3(-1, -1, 5), new Vec3(1, -1, 5), new Vec3(1, 1, 5), both);
            scene.EndGroup();
            string path = Path.Combine(directory, "sided.lscn");
            BinarySceneFile.Save(scene, path);

            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, path);
            loaded.RebuildWorldGeometry(buildAccelerationStructure: false);

            Assert.Equal(2, loaded.Triangles.Count);
            Assert.False(loaded.Triangles[0].Material.DoubleSided);
            Assert.True(loaded.Triangles[1].Material.DoubleSided);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
