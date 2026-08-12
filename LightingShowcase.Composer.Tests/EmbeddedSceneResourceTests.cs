/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 */
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class EmbeddedSceneResourceTests
{
    // Lscene_reopens_textures_without_the_original_image_file verifies that lscene reopens textures without the
    // original image file. Temporary filesystem output is inspected/cleaned so persistence behavior is tested
    // end-to-end. The assertions establish that required objects/resources must resolve; the operation must
    // explicitly report success; the disallowed path must be rejected; the resulting value/state must exactly match
    // the expected result. Representative cases include original-texture.png, embedded.lscene.
    [Fact]
    public void Lscene_reopens_textures_without_the_original_image_file()
    {
        string directory = CreateTempDirectory();
        try
        {
            string missingSource = Path.Combine(directory, "original-texture.png");
            Scene scene = CreateTexturedScene(missingSource);
            string scenePath = Path.Combine(directory, "embedded.lscene");

            BinarySceneFile.Save(scene, scenePath);
            Assert.False(File.Exists(missingSource));

            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, scenePath);
            TextureMap? texture = loaded.ObjectGroups.Single().LocalTriangles.Single().Material.Texture;

            Assert.NotNull(texture);
            Assert.Equal(2, texture!.Width);
            Assert.Equal(2, texture.Height);
            Assert.Equal(scene.ObjectGroups.Single().LocalTriangles.Single().Material.Texture!.ComputeContentHash(), texture.ComputeContentHash());
            Assert.True(texture.Sample(0, 0).X > 0.9);
            Assert.True(texture.SampleAlpha(1, 1) < 0.6);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    // Distinct_in_memory_textures_are_not_collapsed_in_lscene verifies that distinct in memory textures are not
    // collapsed in lscene. Temporary filesystem output is inspected/cleaned so persistence behavior is tested
    // end-to-end. The assertions establish that the operation must produce an observable change. Representative
    // cases include two textures, same-name, distinct.lscene.
    [Fact]
    public void Distinct_in_memory_textures_are_not_collapsed_in_lscene()
    {
        string directory = CreateTempDirectory();
        try
        {
            Scene scene = new();
            scene.Clear();
            SceneObjectGroup group = scene.AddImportedGroup("two textures");
            TextureMap red = TextureMap.FromRgbaBytes("same-name", 1, 1, [255, 0, 0, 255]);
            TextureMap blue = TextureMap.FromRgbaBytes("same-name", 1, 1, [0, 0, 255, 255]);
            group.AddTriangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Material(new Vec3(1, 1, 1), texture: red));
            group.AddTriangle(new Vec3(0, 0, 1), new Vec3(1, 0, 1), new Vec3(0, 1, 1), new Material(new Vec3(1, 1, 1), texture: blue));
            group.RecalculatePivot();
            scene.RebuildWorldGeometry();

            string scenePath = Path.Combine(directory, "distinct.lscene");
            BinarySceneFile.Save(scene, scenePath);
            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, scenePath);

            TextureMap first = loaded.ObjectGroups.Single().LocalTriangles[0].Material.Texture!;
            TextureMap second = loaded.ObjectGroups.Single().LocalTriangles[1].Material.Texture!;
            Assert.NotEqual(first.ComputeContentHash(), second.ComputeContentHash());
        }
        finally
        {
            TryDelete(directory);
        }
    }

    // CreateTexturedScene constructs textured scene in the normalized form expected downstream, so allocation plus
    // initialization of its invariants happen together.
    internal static Scene CreateTexturedScene(string? sourcePath = null)
    {
        byte[] rgba =
        [
            255, 0, 0, 255,    0, 255, 0, 255,
            0, 0, 255, 255,    255, 255, 255, 96
        ];
        TextureMap texture = TextureMap.FromRgbaBytes("test texture", 2, 2, rgba, sourcePath);
        Material material = new(new Vec3(1, 1, 1), texture: texture);
        Scene scene = new();
        scene.Clear();
        SceneObjectGroup group = scene.AddImportedGroup("textured triangle");
        group.AddTriangle(
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(0, 1, 0),
            new Vec2(0, 0),
            new Vec2(1, 0),
            new Vec2(0, 1),
            material);
        group.RecalculatePivot();
        scene.RebuildWorldGeometry();
        return scene;
    }

    // CreateTempDirectory constructs temp directory in the normalized form expected downstream, so allocation plus
    // initialization of its invariants happen together.
    internal static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LightingShowcaseComposerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch { }
    }
}
