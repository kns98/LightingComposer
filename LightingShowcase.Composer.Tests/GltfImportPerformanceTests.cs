using LightingShowcase.CommandLine;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class GltfImportPerformanceTests
{
    [Fact]
    public void Identity_geometry_reuses_local_triangle_objects_and_defers_bvh()
    {
        Scene scene = CreateQuadScene();
        SceneObjectGroup group = Assert.Single(scene.ObjectGroups);

        scene.RebuildWorldGeometry(buildAccelerationStructure: false);

        Assert.False(scene.HasAccelerationStructure);
        Assert.Equal(2, scene.Triangles.Count);
        Assert.Same(group.LocalTriangles[0], scene.Triangles[0]);
        Assert.Same(group.LocalTriangles[1], scene.Triangles[1]);

        Hit? hit = scene.Intersect(new Ray(new Vec3(0.25, 0.25, -1.0), new Vec3(0.0, 0.0, 1.0)));
        Assert.NotNull(hit);
        Assert.True(scene.HasAccelerationStructure);
    }

    [Fact]
    public void Wrapping_imported_roots_does_not_rebuild_unchanged_world_geometry()
    {
        Scene scene = CreateQuadScene();
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);
        Triangle first = scene.Triangles[0];
        long revision = scene.Revision;
        int rootId = Assert.Single(scene.ObjectGroups).Id;

        SceneObjectGroup wrapper = scene.WrapRootGroups(new[] { rootId }, "Imported asset");

        Assert.Equal(revision, scene.Revision);
        Assert.Same(first, scene.Triangles[0]);
        Assert.Single(wrapper.Children);
    }

    [Fact]
    public void Optimized_gltf_import_uses_accessor_bounds_and_leaves_bvh_lazy()
    {
        PluginBootstrap.EnsureLoaded();
        string parent = EmbeddedSceneResourceTests.CreateTempDirectory();
        try
        {
            Scene source = CreateQuadScene();
            SceneExportPackageResult export = new SceneExportPackageService().Export(
                source,
                parent,
                "fast_import",
                SceneExportFormats.Find("gltf"));

            Scene imported = new();
            ObjLoadResult result = GltfSceneIO.LoadIntoScene(
                imported,
                export.PrimaryFilePath,
                new Material(new Vec3(0.8, 0.8, 0.8)));

            string details = Assert.IsType<string>(result.Details);
            Assert.Contains("accessorBounds=1", details);
            Assert.Contains("scannedBounds=0", details);
            Assert.Contains("bvh=deferred", details);
            Assert.False(imported.HasAccelerationStructure);
            SceneObjectGroup importedGroup = Assert.Single(imported.ObjectGroups);
            Assert.Same(importedGroup.LocalTriangles[0], imported.Triangles[0]);
        }
        finally
        {
            EmbeddedSceneResourceTests.TryDelete(parent);
        }
    }

    private static Scene CreateQuadScene()
    {
        Scene scene = new();
        Material material = new(new Vec3(0.7, 0.7, 0.7));
        Vec3 normal = new(0, 0, 1);
        scene.BeginGroup("quad");
        scene.AddTriangle(
            new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0),
            new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1),
            normal, normal, normal, material);
        scene.AddTriangle(
            new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
            new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1),
            normal, normal, normal, material);
        scene.EndGroup();
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);
        return scene;
    }
}
