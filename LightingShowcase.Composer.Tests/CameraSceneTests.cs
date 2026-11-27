using LightingShowcase.CameraSystem;
using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class CameraSceneTests
{
    private static CameraDefinition TestView() => new()
    {
        Position = new Vec3(0, 0, -10),
        Target = new Vec3(0, 0, 0),
        Up = new Vec3(0, 1, 0),
        FieldOfViewDegrees = 72.0
    };

    private static CameraDefinition TestCameraDef(Vec3 position) => new()
    {
        Position = position,
        Target = new Vec3(0, 1, 0),
        Up = new Vec3(0, 1, 0),
        FieldOfViewDegrees = 60.0
    };

    [Fact]
    public void PickCameraMarker_FindsCenteredCamera()
    {
        ComposerSceneSession session = new();
        int id = session.AddStudioCamera("A", TestCameraDef(new Vec3(0, 0, 5)));
        session.AddStudioCamera("B", TestCameraDef(new Vec3(0.5, 0, 5)));

        Assert.Equal(id, session.PickCameraMarker(TestView(), 0.5, 0.5, 480, 360));
        Assert.Null(session.PickCameraMarker(TestView(), 0.05, 0.05, 480, 360));
        Assert.True(session.SetShowCameraMarkers(false));
        Assert.Null(session.PickCameraMarker(TestView(), 0.5, 0.5, 480, 360));
        Assert.False(session.SetShowCameraMarkers(false));
    }

    [Fact]
    public void CameraMove_RigidlyTranslatesAndUndoes()
    {
        ComposerSceneSession session = new();
        int id = session.AddStudioCamera("A", TestCameraDef(new Vec3(4, 2, 0)));
        session.SetSelectedCamera(id);

        SceneCamera? snapshot = session.BeginSelectedCameraMove();
        Assert.NotNull(snapshot);
        Assert.True(session.PreviewSelectedCameraPosition(new Vec3(5, 2, 0)));

        SceneCamera moved = session.GetStudioState().Cameras.First(c => c.Id == id);
        Assert.Equal(new Vec3(5, 2, 0), moved.Definition.Position);
        Assert.Equal(new Vec3(1, 1, 0), moved.Definition.Target);
        Assert.True(session.CommitSelectedCameraMove());

        session.Undo();
        SceneCamera undone = session.GetStudioState().Cameras.First(c => c.Id == id);
        Assert.Equal(new Vec3(4, 2, 0), undone.Definition.Position);
        Assert.Equal(new Vec3(0, 1, 0), undone.Definition.Target);

        session.Redo();
        SceneCamera redone = session.GetStudioState().Cameras.First(c => c.Id == id);
        Assert.Equal(new Vec3(5, 2, 0), redone.Definition.Position);
    }

    [Fact]
    public void CameraMove_CancelRestores()
    {
        ComposerSceneSession session = new();
        int id = session.AddStudioCamera("A", TestCameraDef(new Vec3(4, 2, 0)));
        session.SetSelectedCamera(id);

        Assert.NotNull(session.BeginSelectedCameraMove());
        Assert.True(session.PreviewSelectedCameraPosition(new Vec3(9, 9, 9)));
        session.CancelSelectedCameraMove();

        SceneCamera restored = session.GetStudioState().Cameras.First(c => c.Id == id);
        Assert.Equal(new Vec3(4, 2, 0), restored.Definition.Position);
        Assert.Equal(new Vec3(0, 1, 0), restored.Definition.Target);
    }

    [Fact]
    public void CameraCrud_IsUndoable()
    {
        ComposerSceneSession session = new();
        int id = session.AddStudioCamera("A", TestCameraDef(new Vec3(4, 2, 0)));
        session.AddStudioShot("S", id, 4, StudioShotMove.Static, 0.0);

        Assert.True(session.RenameStudioCamera(id, "B"));
        session.Undo();
        Assert.Equal("A", Assert.Single(session.GetStudioState().Cameras).Name);

        Assert.True(session.RemoveStudioCamera(id));
        Assert.Empty(session.GetStudioState().Cameras);
        Assert.Empty(session.GetStudioState().Shots);
        session.Undo();
        Assert.Equal("A", Assert.Single(session.GetStudioState().Cameras).Name);
        Assert.Equal("S", Assert.Single(session.GetStudioState().Shots).Name);
    }

    [Fact]
    public void CameraSelection_TracksExistingCameras()
    {
        ComposerSceneSession session = new();
        int id = session.AddStudioCamera("A", TestCameraDef(new Vec3(4, 2, 0)));

        session.SetSelectedCamera(id);
        Assert.Equal(id, session.SelectedCameraId);
        Assert.NotNull(session.GetSelectedCameraGizmoBounds());
        session.SetSelectedCamera(4242);
        Assert.Null(session.SelectedCameraId);
        Assert.Null(session.GetSelectedCameraGizmoBounds());
    }

    [Fact]
    public void DrawCameras_MarksPixels()
    {
        CameraDefinition view = TestView();
        SceneCamera camera = new(1, "A", TestCameraDef(new Vec3(0, 0, 5)));
        RenderImage plain = new(64, 48, new uint[64 * 48]);
        ComposerOverlayRenderer.DrawCameras(plain, view, new[] { camera }, null);
        Assert.Contains(plain.PackedRgba32, static pixel => pixel != 0);

        RenderImage selected = new(64, 48, new uint[64 * 48]);
        ComposerOverlayRenderer.DrawCameras(selected, view, new[] { camera }, 1);
        Assert.Contains(selected.PackedRgba32, static pixel => pixel != 0);

        RenderImage behind = new(64, 48, new uint[64 * 48]);
        SceneCamera hidden = new(2, "B", TestCameraDef(new Vec3(0, 0, -20)));
        ComposerOverlayRenderer.DrawCameras(behind, view, new[] { hidden }, null);
        Assert.DoesNotContain(behind.PackedRgba32, static pixel => pixel != 0);
    }
}
