using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class DuplicateGroupTests
{
    [Fact]
    public void DuplicateGroup_RestampsTriangleGroupIds()
    {
        Scene scene = new();
        Material material = new(new Vec3(1, 1, 1));
        scene.BeginGroup("table");
        scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), material);
        scene.BeginGroup("leg");
        scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1), material);
        scene.EndGroup();
        scene.EndGroup();
        SceneObjectGroup original = scene.ObjectGroups[0];
        int originalChildId = original.Children[0].Id;

        SceneObjectGroup duplicate = scene.DuplicateGroup(original.Id);

        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.All(duplicate.LocalTriangles, tri => Assert.Equal(duplicate.Id, tri.GroupId));
        SceneObjectGroup duplicateChild = Assert.Single(duplicate.Children);
        Assert.NotEqual(originalChildId, duplicateChild.Id);
        Assert.All(duplicateChild.LocalTriangles, tri => Assert.Equal(duplicateChild.Id, tri.GroupId));

        // Bake and rebuild so the fast path reuses local triangles; world
        // attribution must still point at the duplicate, not the original.
        duplicate.BakeCurrentTransform();
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);
        Assert.Equal(duplicate.LocalTriangles.Count + duplicateChild.LocalTriangles.Count,
            scene.Triangles.Count(tri => tri.GroupId == duplicate.Id || tri.GroupId == duplicateChild.Id));
        Assert.DoesNotContain(scene.Triangles, tri =>
            tri.GroupId == original.Id && tri.Centroid.X > 0.2 && tri.Centroid.Z > 0.2);
    }
}
