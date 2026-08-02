using System.Text;
using System.Text.Json;
using LightingShowcase.CommandLine;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class ExportPackageTests
{
    [Fact]
    public void Obj_package_contains_numbered_mtl_and_local_texture_reference()
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
            Assert.Contains("res_0001.mtl", result.Files);
            string texture = Assert.Single(result.TextureFiles);
            string exportedTexturePath = Path.Combine(result.DirectoryPath, texture.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(exportedTexturePath));
            TextureMap reopenedTexture = TextureMap.FromFile(exportedTexturePath);
            Assert.Equal(scene.ObjectGroups.Single().LocalTriangles.Single().Material.Texture!.ComputeContentHash(), reopenedTexture.ComputeContentHash());
            Assert.Equal("res_0002.png", texture);
            string obj = File.ReadAllText(result.PrimaryFilePath);
            Assert.Contains("mtllib res_0001.mtl", obj);
            string mtl = File.ReadAllText(Path.Combine(result.DirectoryPath, "res_0001.mtl"));
            Assert.Contains("map_Kd res_0002.png", mtl);
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

            Assert.Contains("res_0001.bin", result.Files);
            string json = File.ReadAllText(result.PrimaryFilePath);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.True(root.TryGetProperty("images", out JsonElement images));
            Assert.Equal("res_0002.png", images[0].GetProperty("uri").GetString());
            Assert.Equal("res_0001.bin", root.GetProperty("buffers")[0].GetProperty("uri").GetString());
            Assert.True(root.GetProperty("materials")[0].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out _));
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }

    [Fact]
    public void Glb_package_uses_external_numbered_buffer_and_texture()
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = EmbeddedSceneResourceTests.CreateTexturedScene();
            SceneExportPackageResult result = new SceneExportPackageService().Export(
                scene, parent, "sample", SceneExportFormats.Find("glb"));

            Assert.Contains("res_0001.bin", result.Files);
            Assert.Contains("res_0002.png", result.Files);

            using BinaryReader reader = new(File.OpenRead(result.PrimaryFilePath), Encoding.UTF8);
            Assert.Equal(0x46546C67u, reader.ReadUInt32());
            Assert.Equal(2u, reader.ReadUInt32());
            _ = reader.ReadUInt32();
            int jsonLength = checked((int)reader.ReadUInt32());
            Assert.Equal(0x4E4F534Au, reader.ReadUInt32());
            string json = Encoding.UTF8.GetString(reader.ReadBytes(jsonLength)).TrimEnd('\0', ' ', '\r', '\n', '\t');
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal("res_0001.bin", document.RootElement.GetProperty("buffers")[0].GetProperty("uri").GetString());
            Assert.Equal("res_0002.png", document.RootElement.GetProperty("images")[0].GetProperty("uri").GetString());
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }


    [Fact]
    public void Native_export_uses_external_numbered_resources_while_normal_save_remains_embedded()
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = EmbeddedSceneResourceTests.CreateTexturedScene();
            SceneExportPackageResult result = new SceneExportPackageService().Export(
                scene, parent, "sample", SceneExportFormats.Find("lscene"));

            Assert.Equal(new[] { "res_0001.png" }, result.TextureFiles);
            Assert.True(File.Exists(Path.Combine(result.DirectoryPath, "res_0001.png")));

            Scene reopened = new();
            BinarySceneFile.LoadIntoScene(reopened, result.PrimaryFilePath);
            TextureMap? reopenedTexture = reopened.ObjectGroups.Single().LocalTriangles.Single().Material.Texture;
            Assert.NotNull(reopenedTexture);
            Assert.Equal(
                scene.ObjectGroups.Single().LocalTriangles.Single().Material.Texture!.ComputeContentHash(),
                reopenedTexture!.ComputeContentHash());
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }

    [Theory]
    [MemberData(nameof(AllExportFormatIds))]
    public void Export_related_files_use_numbered_resource_names(string formatId)
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = EmbeddedSceneResourceTests.CreateTexturedScene();
            SceneExportPackageResult result = new SceneExportPackageService().Export(
                scene, parent, "sample", SceneExportFormats.Find(formatId));

            string primaryName = Path.GetFileName(result.PrimaryFilePath);
            string[] resources = result.Files
                .Where(file => !string.Equals(file, primaryName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.NotEmpty(resources);
            Assert.All(resources, file => Assert.Matches(@"^res_[0-9]{4}\.[A-Za-z0-9]+$", file));
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
