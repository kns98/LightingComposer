using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;

namespace LightingShowcase.Composer.Tests;

public sealed class RendererCacheTests
{
    [Fact]
    public void Selection_overlay_does_not_mutate_scene_or_invalidate_geometry_cache_stamp()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        SceneCacheStamp before = session.CaptureSceneCacheStampForTests();

        Assert.True(session.SetSelectedObject(rootId));

        Assert.True(before.Matches(before.Scene));
        SceneCacheStamp after = session.CaptureSceneCacheStampForTests();
        Assert.Equal(before.Revision, after.Revision);
    }


    [Fact]
    public void Virtual_triangle_selection_does_not_add_nodes_or_change_scene_revision()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        var mesh = session.GetObjectInfos().First(info => info.LocalTriangleCount > 0);
        int objectsBefore = session.ObjectCount;
        SceneCacheStamp before = session.CaptureSceneCacheStampForTests();

        Assert.True(session.SetSelectedTriangle(mesh.Id, 0));

        SceneCacheStamp after = session.CaptureSceneCacheStampForTests();
        Assert.Equal(objectsBefore, session.ObjectCount);
        Assert.Equal(before.Revision, after.Revision);
        Assert.True(before.Matches(after.Scene));
        Assert.Equal(rootId, session.GetObjectInfos().First(info => info.Id == rootId).Id);
    }

    [Fact]
    public void Gizmo_pointer_moves_do_not_rebuild_or_reupload_scene_until_commit()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        SceneCacheStamp before = session.CaptureSceneCacheStampForTests();
        ComposerObjectState state = session.GetObjectState(rootId)!;

        Assert.True(session.SetSelectedObject(rootId));
        Assert.True(session.UpdateTransformTarget(
            rootId,
            new Vec3(2, 0, 0),
            state.Rotation + new Vec3(0, Math.PI / 4.0, 0),
            new Vec3(1.25, 0.8, 1.1)));

        SceneCacheStamp pending = session.CaptureSceneCacheStampForTests();
        Assert.Equal(before.Revision, pending.Revision);

        Assert.True(session.CommitPendingTransform(rootId));
        SceneCacheStamp committed = session.CaptureSceneCacheStampForTests();
        Assert.True(committed.Revision > before.Revision);
        ComposerObjectState committedState = session.GetObjectState(rootId)!;
        AssertVec3(Vec3.Zero, committedState.Position);
        AssertVec3(Vec3.Zero, committedState.Rotation);
        AssertVec3(new Vec3(1, 1, 1), committedState.Scale);
    }

    [Fact]
    public void Scene_revision_invalidates_a_cache_stamp_after_transform()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        SceneCacheStamp before = session.CaptureSceneCacheStampForTests();
        ShadowRasterRenderer.PreviewCache rasterCache = ShadowRasterRenderer.BuildCache(before.Scene, CancellationToken.None);
        Assert.True(rasterCache.IsCurrent(before.Scene));

        ComposerObjectState state = session.GetObjectState(rootId)!;
        Assert.True(session.UpdateObject(
            rootId,
            state.Name,
            state.Visible,
            new Vec3(1, 0, 0),
            state.Rotation,
            state.Scale));

        Assert.False(before.Matches(before.Scene));
        Assert.False(rasterCache.IsCurrent(before.Scene));
        SceneCacheStamp after = session.CaptureSceneCacheStampForTests();
        Assert.True(after.Matches(after.Scene));
        Assert.True(after.Revision > before.Revision);
    }

    [Fact]
    public void Software_preview_pixels_change_after_transform_using_same_scene_instance()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        session.FrameObject(rootId);
        var camera = session.Camera.Snapshot();

        ComposerFrame before = session.Render(
            ComposerRendererKind.Raster,
            camera,
            192,
            144,
            interactive: false,
            CancellationToken.None);

        ComposerObjectState state = session.GetObjectState(rootId)!;
        Assert.True(session.UpdateObject(
            rootId,
            state.Name,
            state.Visible,
            state.Position + new Vec3(1.25, 0.4, 0),
            state.Rotation + new Vec3(0, 0, Math.PI / 8),
            state.Scale));

        ComposerFrame after = session.Render(
            ComposerRendererKind.Raster,
            camera,
            192,
            144,
            interactive: false,
            CancellationToken.None);

        Assert.NotEqual(HashPixels(before.Image), HashPixels(after.Image));
    }

    private static void AssertVec3(Vec3 expected, Vec3 actual, double tolerance = 1e-8)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0, tolerance);
    }

    private static ulong HashPixels(RenderImage image)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (uint pixel in image.PackedRgba32)
        {
            hash ^= pixel;
            hash *= prime;
        }
        return hash;
    }
}
