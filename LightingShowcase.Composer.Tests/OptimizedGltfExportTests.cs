/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 *
 * `Optimized_export_merges_chunks_and_welds_shared_vertices` verifies that optimized export merges chunks and
 * welds shared vertices. Temporary filesystem output is inspected/cleaned so persistence behavior is tested
 * end-to-end. The assertions establish that the operation must explicitly report success; the resulting
 * value/state must exactly match the expected result. Representative cases include `optimized`, `gltf`,
 * `preserved`, `gltf-hierarchy`, `meshes`, `primitives`, `attributes`.
 *
 * `Optimized_catalog_entries_are_default_gltf_and_glb_choices` verifies that optimized catalog entries are
 * default gltf and glb choices. The assertions establish that the operation must explicitly report success; the
 * disallowed path must be rejected. Representative cases include `gltf`, `glb`, `gltf-hierarchy`,
 * `glb-hierarchy`.
 *
 * `CreateChunkedQuadScene` constructs chunked quad scene in the normalized form expected downstream, so
 * allocation plus initialization of its invariants happen together.
 */
using System.Text.Json;
using LightingShowcase.CommandLine;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class OptimizedGltfExportTests
{
    [Fact]
    public void Optimized_export_merges_chunks_and_welds_shared_vertices()
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene scene = CreateChunkedQuadScene();
            SceneExportPackageService exporter = new();

            SceneExportPackageResult optimized = exporter.Export(
                scene, parent, "optimized", SceneExportFormats.Find("gltf"));
            SceneExportPackageResult preserved = exporter.Export(
                scene, parent, "preserved", SceneExportFormats.Find("gltf-hierarchy"));

            using JsonDocument optimizedJson = JsonDocument.Parse(File.ReadAllText(optimized.PrimaryFilePath));
            using JsonDocument preservedJson = JsonDocument.Parse(File.ReadAllText(preserved.PrimaryFilePath));

            JsonElement optimizedRoot = optimizedJson.RootElement;
            JsonElement preservedRoot = preservedJson.RootElement;
            Assert.Equal(1, optimizedRoot.GetProperty("meshes").GetArrayLength());
            Assert.Equal(2, preservedRoot.GetProperty("meshes").GetArrayLength());

            JsonElement primitive = optimizedRoot.GetProperty("meshes")[0].GetProperty("primitives")[0];
            int positionAccessorIndex = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            int normalAccessorIndex = primitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            int indexAccessorIndex = primitive.GetProperty("indices").GetInt32();
            JsonElement accessors = optimizedRoot.GetProperty("accessors");

            Assert.Equal(4, accessors[positionAccessorIndex].GetProperty("count").GetInt32());
            Assert.Equal(4, accessors[normalAccessorIndex].GetProperty("count").GetInt32());
            Assert.Equal(6, accessors[indexAccessorIndex].GetProperty("count").GetInt32());
            Assert.Equal(5123, accessors[indexAccessorIndex].GetProperty("componentType").GetInt32());

            long optimizedBytes = new FileInfo(Path.Combine(optimized.DirectoryPath, "res_0001.bin")).Length;
            long preservedBytes = new FileInfo(Path.Combine(preserved.DirectoryPath, "res_0001.bin")).Length;
            Assert.True(optimizedBytes < preservedBytes, $"Expected optimized buffer {optimizedBytes} to be smaller than preserved buffer {preservedBytes}.");
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }

    [Fact]
    public void Optimized_catalog_entries_are_default_gltf_and_glb_choices()
    {
        Assert.True(SceneExportFormats.Find("gltf").OptimizeGeometry);
        Assert.True(SceneExportFormats.Find("glb").OptimizeGeometry);
        Assert.False(SceneExportFormats.Find("gltf-hierarchy").OptimizeGeometry);
        Assert.False(SceneExportFormats.Find("glb-hierarchy").OptimizeGeometry);
    }

    private static Scene CreateChunkedQuadScene()
    {
        Scene scene = new();
        Material material = new(new Vec3(0.7, 0.7, 0.7));
        Vec3 normal = new(0, 0, 1);

        scene.BeginGroup("chunk_1");
        scene.AddTriangle(
            new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0),
            new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1),
            normal, normal, normal, material);
        scene.EndGroup();

        scene.BeginGroup("chunk_2");
        scene.AddTriangle(
            new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
            new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1),
            normal, normal, normal, material);
        scene.EndGroup();

        scene.SetDescription("Chunked quad");
        scene.RebuildWorldGeometry();
        return scene;
    }
}
