using System.Text.Json;
using LightingShowcase.CommandLine;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class ExportPackageTests
{
    [Fact]
    public void Obj_package_contains_primary_mtl_manifest_and_local_texture_reference()
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = EmbeddedSceneResourceTests.CreateTexturedScene();
            SceneExportPackageResult result = new SceneExportPackageService().Export(
                scene, parent, "sample", SceneExportFormats.Find("obj"));

            Assert.True(Directory.Exists(result.DirectoryPath));
            Assert.True(File.Exists(result.PrimaryFilePath));
            Assert.Contains("sample.mtl", result.Files);
            Assert.Contains("export-manifest.json", result.Files);
            string texture = Assert.Single(result.TextureFiles);
            string exportedTexturePath = Path.Combine(result.DirectoryPath, texture.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(exportedTexturePath));
            TextureMap reopenedTexture = TextureMap.FromFile(exportedTexturePath);
            Assert.Equal(scene.ObjectGroups.Single().LocalTriangles.Single().Material.Texture!.ComputeContentHash(), reopenedTexture.ComputeContentHash());
            string mtl = File.ReadAllText(Path.Combine(result.DirectoryPath, "sample.mtl"));
            Assert.Contains("map_Kd textures/", mtl);
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }

    [Fact]
    public void Gltf_package_contains_bin_and_references_packaged_texture()
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = EmbeddedSceneResourceTests.CreateTexturedScene();
            SceneExportPackageResult result = new SceneExportPackageService().Export(
                scene, parent, "sample", SceneExportFormats.Find("gltf"));

            Assert.Contains("sample.bin", result.Files);
            string json = File.ReadAllText(result.PrimaryFilePath);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.True(root.TryGetProperty("images", out JsonElement images));
            Assert.Equal("textures/test_texture.png", images[0].GetProperty("uri").GetString());
            Assert.True(root.GetProperty("materials")[0].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out _));
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }


    [Fact]
    public void Export_catalog_routes_every_non_native_format_to_a_registered_exporter()
    {
        PluginBootstrap.EnsureLoaded();

        foreach (SceneExportFormat format in SceneExportFormats.All.Where(item => !item.IsNativeScene))
        {
            ISceneFormatPlugin plugin = SceneFormatRegistry.FindExporter("probe" + format.Extension);
            Assert.True(plugin.CanExport);
            if (!string.IsNullOrWhiteSpace(format.Variant))
                Assert.True(plugin.ExportVariants.Contains(format.Variant, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [MemberData(nameof(AllExportFormatIds))]
    public void Every_known_export_format_creates_a_new_directory_and_primary_file(string formatId)
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = EmbeddedSceneResourceTests.CreateTexturedScene();
            SceneExportFormat format = SceneExportFormats.Find(formatId);
            SceneExportPackageService exporter = new();

            SceneExportPackageResult first = exporter.Export(scene, parent, "sample", format);
            SceneExportPackageResult second = exporter.Export(scene, parent, "sample", format);

            Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
            Assert.True(File.Exists(first.PrimaryFilePath));
            Assert.True(File.Exists(second.PrimaryFilePath));
            Assert.Contains("export-manifest.json", first.Files);
            Assert.NotEmpty(first.TextureFiles);
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }

    public static IEnumerable<object[]> AllExportFormatIds() =>
        SceneExportFormats.All.Select(format => new object[] { format.Id });
}
