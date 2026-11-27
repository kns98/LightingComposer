using LightingShowcase.CameraSystem;
using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class StudioSessionTests
{
    private static CameraDefinition TestCamera() => new()
    {
        Position = new Vec3(4, 2, 0),
        Target = new Vec3(0, 1, 0),
        Up = new Vec3(0, 1, 0),
        FieldOfViewDegrees = 60.0
    };

    [Fact]
    public void Session_CameraCrud()
    {
        ComposerSceneSession session = new();

        int id = session.AddStudioCamera("Wide", TestCamera());
        Assert.Throws<ArgumentException>(() => session.AddStudioCamera("  ", TestCamera()));
        Assert.True(session.RenameStudioCamera(id, "Establishing"));
        Assert.False(session.RenameStudioCamera(4242, "Lost"));
        Assert.True(session.SetStudioCameraDefinition(id, new CameraDefinition { Position = new Vec3(1, 1, 1) }));
        Assert.False(session.SetStudioCameraDefinition(4242, TestCamera()));

        StudioState state = session.GetStudioState();
        SceneCamera camera = Assert.Single(state.Cameras);
        Assert.Equal("Establishing", camera.Name);
        Assert.Equal(new Vec3(1, 1, 1), camera.Definition.Position);

        Assert.True(session.RemoveStudioCamera(id));
        Assert.False(session.RemoveStudioCamera(id));
        Assert.Empty(session.GetStudioState().Cameras);
    }

    [Fact]
    public void Session_ShotCrud()
    {
        ComposerSceneSession session = new();
        int camera = session.AddStudioCamera("Wide", TestCamera());

        int first = session.AddStudioShot("A", camera, 4, StudioShotMove.Static, 0.0);
        int second = session.AddStudioShot("B", camera, 2, StudioShotMove.Orbit, 45.0);
        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AddStudioShot("Bad", camera, 0, StudioShotMove.Static, 0.0));

        Assert.True(session.UpdateStudioShot(1, "B2", camera, 3, StudioShotMove.Push, 0.5));
        Assert.False(session.UpdateStudioShot(9, "Lost", camera, 1, StudioShotMove.Static, 0.0));
        Assert.True(session.MoveStudioShot(1, -1));
        Assert.Equal("B2", session.GetStudioState().Shots[0].Name);
        Assert.True(session.RemoveStudioShot(0));
        Assert.False(session.RemoveStudioShot(5));
        Assert.Equal("A", Assert.Single(session.GetStudioState().Shots).Name);
    }

    [Fact]
    public void Session_RemoveCameraCascadesShots()
    {
        ComposerSceneSession session = new();
        int camera = session.AddStudioCamera("Wide", TestCamera());
        session.AddStudioShot("A", camera, 2, StudioShotMove.Static, 0.0);

        Assert.True(session.RemoveStudioCamera(camera));
        Assert.Empty(session.GetStudioState().Shots);
    }

    [Fact]
    public void Session_KeyframeValidation()
    {
        ComposerSceneSession session = new();

        Assert.False(session.AddMotionKeyframe(4242, new MotionKeyframe(0, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1))));
        Assert.False(session.RemoveMotionKeyframe(4242, 0));
        Assert.False(session.RemoveMotionTrack(4242));
    }

    [Fact]
    public void Session_MovieSettingsRoundTrip()
    {
        ComposerSceneSession session = new();
        session.UpdateMovieSettings(new MovieSettings { Fps = 24, Width = 640, Height = 480, OutputName = "take1" });

        MovieSettings settings = session.GetStudioState().Settings;
        Assert.Equal(24, settings.Fps);
        Assert.Equal(640, settings.Width);
        Assert.Equal("take1", settings.OutputName);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.UpdateMovieSettings(new MovieSettings { Fps = 0 }));
    }

    [Fact]
    public void Scene_TrackHelpers()
    {
        Scene scene = new();
        scene.BeginGroup("box");
        scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Material(new Vec3(1, 1, 1)));
        scene.EndGroup();
        int boxId = scene.ObjectGroups[0].Id;

        MotionTrack track = scene.GetOrAddMotionTrack(boxId);
        Assert.Same(track, scene.GetOrAddMotionTrack(boxId));
        scene.GetOrAddMotionTrack(4242);
        scene.PruneMotionTracks();
        Assert.Equal(boxId, Assert.Single(scene.MotionTracks).ObjectId);
    }

    [Fact]
    public void ComposerCamera_ApplyViewRoundTrips()
    {
        ComposerCamera orbit = new();
        orbit.Orbit(30.0, -12.0);
        orbit.Pan(40.0, 10.0, 600.0);
        orbit.Zoom(2.0);
        orbit.FieldOfViewDegrees = 55.0;
        CameraDefinition snapshot = orbit.Snapshot();

        ComposerCamera applied = new();
        applied.ApplyView(snapshot);
        CameraDefinition roundTripped = applied.Snapshot();

        Assert.Equal(snapshot.Position.X, roundTripped.Position.X, precision: 9);
        Assert.Equal(snapshot.Position.Y, roundTripped.Position.Y, precision: 9);
        Assert.Equal(snapshot.Position.Z, roundTripped.Position.Z, precision: 9);
        Assert.Equal(snapshot.Target, roundTripped.Target);
        Assert.Equal(55.0, roundTripped.FieldOfViewDegrees);
    }
}
