using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class StudioTimelineTests
{
    private static string FindRepoFile(params string[] parts)
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        return Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());
    }

    private static CameraDefinition TestCamera() => new()
    {
        Position = new Vec3(4, 2, 0),
        Target = new Vec3(0, 1, 0),
        Up = new Vec3(0, 1, 0),
        FieldOfViewDegrees = 60.0,
        NearPlane = 0.05,
        FarPlane = 5000.0
    };

    [Fact]
    public void ResolveFrames_StaticHoldsPosition()
    {
        SceneCamera camera = new(1, "Wide", TestCamera());

        IReadOnlyList<StudioFrame> frames = StudioTimeline.ResolveFrames(
            new[] { camera }, new[] { new StudioShot("Hold", 1, 3) });

        Assert.Equal(3, frames.Count);
        Assert.Equal(new Vec3(4, 2, 0), frames[0].Camera.Position);
        Assert.Equal(new Vec3(4, 2, 0), frames[2].Camera.Position);
        Assert.Equal(new[] { 0, 1, 2 }, frames.Select(static f => f.GlobalFrame));
    }

    [Fact]
    public void ResolveFrames_OrbitSweepsArc()
    {
        SceneCamera camera = new(1, "Wide", TestCamera());

        IReadOnlyList<StudioFrame> frames = StudioTimeline.ResolveFrames(
            new[] { camera }, new[] { new StudioShot("Arc", 1, 3, StudioShotMove.Orbit, 90.0) });

        Assert.Equal(new Vec3(4, 2, 0), frames[0].Camera.Position);
        Assert.Equal(2.82842712, frames[1].Camera.Position.X, precision: 6);
        Assert.Equal(2.0, frames[1].Camera.Position.Y, precision: 9);
        Assert.Equal(-2.82842712, frames[1].Camera.Position.Z, precision: 6);
        Assert.Equal(0.0, frames[2].Camera.Position.X, precision: 6);
        Assert.Equal(-4.0, frames[2].Camera.Position.Z, precision: 6);
    }

    [Fact]
    public void ResolveFrames_PushDolliesIn()
    {
        SceneCamera camera = new(1, "Wide", TestCamera());

        IReadOnlyList<StudioFrame> frames = StudioTimeline.ResolveFrames(
            new[] { camera }, new[] { new StudioShot("Push", 1, 3, StudioShotMove.Push, 0.5) });

        Assert.Equal(new Vec3(4, 2, 0), frames[0].Camera.Position);
        Assert.Equal(3.0, frames[1].Camera.Position.X, precision: 9);
        Assert.Equal(1.75, frames[1].Camera.Position.Y, precision: 9);
        Assert.Equal(2.0, frames[2].Camera.Position.X, precision: 9);
        Assert.Equal(1.5, frames[2].Camera.Position.Y, precision: 9);
        Assert.Equal(0.0, frames[2].Camera.Position.Z, precision: 9);
    }

    [Fact]
    public void ResolveFrames_IsDeterministicAcrossShots()
    {
        SceneCamera wide = new(1, "Wide", TestCamera());
        SceneCamera close = new(2, "Close", TestCamera());
        StudioShot[] shots = new[]
        {
            new StudioShot("A", 1, 2, StudioShotMove.Orbit, 45.0),
            new StudioShot("B", 2, 2, StudioShotMove.Push, 0.8),
        };

        IReadOnlyList<StudioFrame> first = StudioTimeline.ResolveFrames(new[] { wide, close }, shots);
        IReadOnlyList<StudioFrame> second = StudioTimeline.ResolveFrames(new[] { wide, close }, shots);

        Assert.Equal(4, first.Count);
        Assert.Equal(new[] { 0, 0, 1, 1 }, first.Select(static f => f.ShotIndex));
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Camera.Position, second[i].Camera.Position);
    }

    [Fact]
    public void ResolveFrames_RejectsBadTimelines()
    {
        SceneCamera camera = new(1, "Wide", TestCamera());
        IReadOnlyList<StudioFrame> empty = StudioTimeline.ResolveFrames(new[] { camera }, new StudioShot[0]);
        Assert.Empty(empty);
        Assert.Throws<InvalidOperationException>(() => StudioTimeline.ResolveFrames(
            new[] { camera }, new[] { new StudioShot("Lost", 999, 2) }));
        Assert.Throws<ArgumentException>(() => StudioTimeline.ResolveFrames(
            new[] { camera }, new[] { new StudioShot("Empty", 1, 0) }));
        Assert.Throws<ArgumentException>(() => StudioTimeline.ResolveFrames(
            new[] { camera }, new[] { new StudioShot("Long", 1, 361) }));
    }

    [Fact]
    public void MotionTrack_SamplesHoldsAndEasing()
    {
        MotionTrack track = new(7);
        Assert.Null(track.Sample(0));
        track.AddKey(new MotionKeyframe(0, new Vec3(0, 0, 0), Vec3.Zero, new Vec3(1, 1, 1)));
        track.AddKey(new MotionKeyframe(10, new Vec3(10, 0, 0), Vec3.Zero, new Vec3(2, 2, 2)));

        Assert.Equal(new Vec3(0, 0, 0), track.Sample(-3)!.Position);
        Assert.Equal(new Vec3(5, 0, 0), track.Sample(5)!.Position);
        Assert.Equal(new Vec3(10, 0, 0), track.Sample(99)!.Position);
    }

    [Fact]
    public void MotionTrack_AddRemoveKeys()
    {
        MotionTrack track = new(7);
        track.AddKey(new MotionKeyframe(10, new Vec3(1, 0, 0), Vec3.Zero, new Vec3(1, 1, 1)));
        track.AddKey(new MotionKeyframe(0, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1)));
        Assert.Equal(new[] { 0, 10 }, track.Keys.Select(static k => k.Frame));
        track.AddKey(new MotionKeyframe(0, new Vec3(2, 0, 0), Vec3.Zero, new Vec3(1, 1, 1)));
        Assert.Equal(2, track.Keys.Count);
        Assert.Equal(new Vec3(2, 0, 0), track.Sample(0)!.Position);
        Assert.True(track.RemoveKey(0));
        Assert.False(track.RemoveKey(0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            track.AddKey(new MotionKeyframe(-1, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1))));
    }

    [Fact]
    public void StudioPosing_RoundTripsGroupTransforms()
    {
        Scene scene = new();
        Material material = new(new Vec3(1, 1, 1));
        scene.BeginGroup("a");
        scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), material);
        scene.EndGroup();
        scene.BeginGroup("b");
        scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1), material);
        scene.EndGroup();
        SceneObjectGroup a = scene.ObjectGroups[0];
        SceneObjectGroup b = scene.ObjectGroups[1];
        a.Position = new Vec3(1, 2, 3);

        Dictionary<int, MotionPose> snapshot = StudioPosing.Capture(scene);
        StudioPosing.ApplyPoses(scene, new Dictionary<int, MotionPose>
        {
            [a.Id] = new(new Vec3(9, 9, 9), Vec3.Zero, new Vec3(1, 1, 1)),
            [b.Id] = new(new Vec3(8, 8, 8), Vec3.Zero, new Vec3(1, 1, 1)),
            [4242] = new(new Vec3(7, 7, 7), Vec3.Zero, new Vec3(1, 1, 1)),
        });
        Assert.Equal(new Vec3(9, 9, 9), a.Position);
        StudioPosing.ApplyPoses(scene, snapshot);
        Assert.Equal(new Vec3(1, 2, 3), a.Position);
        Assert.Equal(Vec3.Zero, b.Position);
    }

    [Fact]
    public void MovieSettings_ValidatesRanges()
    {
        new MovieSettings().Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovieSettings { Fps = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovieSettings { Width = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovieSettings { Backend = RenderBackend.VulkanDiagnostic }.Validate());
        Assert.Throws<ArgumentException>(() => new MovieSettings { OutputName = "  " }.Validate());
        Assert.Throws<ArgumentException>(() => new MovieSettings { OutputName = "a\0b" }.Validate());
    }

    [Fact]
    public void BinaryScene_RoundTripsStudioStateAndGroupIds()
    {
        string directory = Directory.CreateTempSubdirectory("binary-studio-test").FullName;
        try
        {
            Scene scene = new();
            scene.BeginGroup("box");
            scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Material(new Vec3(1, 1, 1)));
            scene.EndGroup();
            int boxId = scene.ObjectGroups[0].Id;
            SceneCamera camera = scene.AddStudioCamera("Wide", TestCamera());
            scene.StudioShots.Add(new StudioShot("Arc", camera.Id, 4, StudioShotMove.Orbit, 45.0));
            MotionTrack track = scene.GetOrAddMotionTrack(boxId);
            track.AddKey(new MotionKeyframe(0, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1)));
            track.AddKey(new MotionKeyframe(3, new Vec3(1, 2, 3), new Vec3(0.1, 0.2, 0.3), new Vec3(2, 2, 2)));
            scene.MovieSettings = new MovieSettings { Fps = 24, Width = 640, Height = 480, Backend = RenderBackend.Cpu, OutputName = "take1" };

            string path = Path.Combine(directory, "studio.lscn");
            BinarySceneFile.Save(scene, path);
            using (FileStream header = File.OpenRead(path))
            using (BinaryReader reader = new(header, System.Text.Encoding.UTF8, leaveOpen: false))
            {
                Assert.Equal("LSCN", reader.ReadString());
                Assert.Equal(15, reader.ReadInt32());
            }

            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, path);

            Assert.Equal(boxId, loaded.ObjectGroups[0].Id);
            SceneCamera reloaded = Assert.Single(loaded.StudioCameras);
            Assert.Equal(camera.Id, reloaded.Id);
            Assert.Equal("Wide", reloaded.Name);
            Assert.Equal(camera.Definition.Position, reloaded.Definition.Position);
            Assert.Equal(camera.Definition.FieldOfViewDegrees, reloaded.Definition.FieldOfViewDegrees);
            StudioShot shot = Assert.Single(loaded.StudioShots);
            Assert.Equal("Arc", shot.Name);
            Assert.Equal(camera.Id, shot.CameraId);
            Assert.Equal(StudioShotMove.Orbit, shot.Move);
            MotionTrack reloadedTrack = Assert.Single(loaded.MotionTracks);
            Assert.Equal(boxId, reloadedTrack.ObjectId);
            Assert.Equal(2, reloadedTrack.Keys.Count);
            Assert.Equal(new Vec3(1, 2, 3), reloadedTrack.Keys[1].Position);
            Assert.Equal(24, loaded.MovieSettings.Fps);
            Assert.Equal("take1", loaded.MovieSettings.OutputName);
            Assert.Equal(camera.Id + 1, loaded.AddStudioCamera("Next", TestCamera()).Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BinaryScene_OldFileLoadsWithEmptyStudio()
    {
        Scene scene = new();
        BinarySceneFile.LoadIntoScene(scene, FindRepoFile("models", "MaterialShowcase", "MaterialShowcase.lscene"));
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);

        Assert.NotEmpty(scene.ObjectGroups);
        Assert.Empty(scene.StudioCameras);
        Assert.Empty(scene.StudioShots);
        Assert.Empty(scene.MotionTracks);
        Assert.Equal(12, scene.MovieSettings.Fps);
    }

    [Fact]
    public void BinaryScene_PruneMissingTrackIdsOnLoad()
    {
        string directory = Directory.CreateTempSubdirectory("binary-prune-test").FullName;
        try
        {
            Scene scene = new();
            scene.BeginGroup("box");
            scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Material(new Vec3(1, 1, 1)));
            scene.EndGroup();
            MotionTrack doomed = scene.GetOrAddMotionTrack(4242);
            doomed.AddKey(new MotionKeyframe(0, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1)));

            string path = Path.Combine(directory, "prune.lscn");
            BinarySceneFile.Save(scene, path);

            Scene loaded = new();
            BinarySceneFile.LoadIntoScene(loaded, path);
            Assert.Single(loaded.ObjectGroups);
            Assert.Empty(loaded.MotionTracks);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
