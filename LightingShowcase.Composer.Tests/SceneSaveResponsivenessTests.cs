/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 */
using System.Diagnostics;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class SceneSaveResponsivenessTests
{
    // Shared_textured_mesh_saves_without_rehashing_the_texture_per_triangle verifies that shared textured mesh
    // saves without rehashing the texture per triangle. Timing is measured because responsiveness/performance is
    // part of the contract being protected. Temporary filesystem output is inspected/cleaned so persistence
    // behavior is tested end-to-end. The assertions establish that the operation must explicitly report success.
    // Representative cases include shared, many triangles, responsive.lscene.
    [Fact]
    public void Shared_textured_mesh_saves_without_rehashing_the_texture_per_triangle()
    {
        string directory = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            const int width = 256;
            const int height = 256;
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = 120;
                rgba[i + 1] = 170;
                rgba[i + 2] = 220;
                rgba[i + 3] = 255;
            }

            TextureMap texture = TextureMap.FromRgbaBytes("shared", width, height, rgba);
            Material material = new(new Vec3(1, 1, 1), texture: texture);
            Scene scene = new();
            scene.Clear();
            SceneObjectGroup group = scene.AddImportedGroup("many triangles");
            for (int i = 0; i < 5_000; i++)
            {
                double x = i % 100;
                double y = i / 100;
                group.AddTriangle(
                    new Vec3(x, y, 0),
                    new Vec3(x + 0.8, y, 0),
                    new Vec3(x, y + 0.8, 0),
                    material);
            }
            group.RecalculatePivot();
            scene.RebuildWorldGeometry();

            string scenePath = Path.Combine(directory, "responsive.lscene");
            Stopwatch stopwatch = Stopwatch.StartNew();
            BinarySceneFile.Save(scene, scenePath);
            stopwatch.Stop();

            Assert.True(File.Exists(scenePath));
            Assert.True(new FileInfo(scenePath).Length > 0);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20),
                $"Saving a shared-texture scene took {stopwatch.Elapsed.TotalSeconds:F1}s.");
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(directory);
        }
    }

    // Save_replaces_destination_atomically_and_removes_temporary_file verifies that save replaces destination
    // atomically and removes temporary file. Temporary filesystem output is inspected/cleaned so persistence
    // behavior is tested end-to-end. The assertions establish that the operation must explicitly report success.
    // Representative cases include atomic.lscene, old content, .atomic.lscene.*.tmp.
    [Fact]
    public void Save_replaces_destination_atomically_and_removes_temporary_file()
    {
        string directory = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = EmbeddedSceneResourceTests.CreateTexturedScene();
            string scenePath = Path.Combine(directory, "atomic.lscene");
            File.WriteAllText(scenePath, "old content");

            BinarySceneFile.Save(scene, scenePath);

            Assert.True(new FileInfo(scenePath).Length > "old content".Length);
            Assert.Empty(Directory.EnumerateFiles(directory, ".atomic.lscene.*.tmp"));

            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, scenePath);
            Assert.NotEmpty(loaded.Triangles);
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(directory);
        }
    }
}
