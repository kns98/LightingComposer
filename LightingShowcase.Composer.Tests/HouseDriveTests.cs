using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class HouseDriveTests
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

    private static Scene LoadMovie()
    {
        Scene scene = new();
        BinarySceneFile.LoadIntoScene(scene, FindRepoFile("models", "HouseDrive", "HouseDrive.lscene"));
        scene.RebuildWorldGeometry(buildAccelerationStructure: false);
        return scene;
    }

    [Fact]
    public void HouseDrive_HasTimelineAndTrack()
    {
        Scene scene = LoadMovie();

        Assert.Equal(new[] { "Establishing", "Tracking", "Arrival" }, scene.StudioCameras.Select(static c => c.Name));
        Assert.Equal(3, scene.StudioShots.Count);
        Assert.Equal(48, scene.StudioShots.Sum(static s => s.FrameCount));
        Assert.Equal(12, scene.MovieSettings.Fps);
        Assert.Equal("housedrive", scene.MovieSettings.OutputName);

        MotionTrack track = Assert.Single(scene.MotionTracks);
        SceneObjectGroup car = scene.GroupById(track.ObjectId)!;
        Assert.NotNull(car);
        Assert.Equal("Car", car.Name);
        Assert.True(car.Children.Count >= 2);
        Assert.Equal(4, track.Keys.Count);
        Assert.DoesNotContain(scene.ObjectGroups.SelectMany(static g => g.SelfAndDescendants()),
            static g => g.Name.IndexOf("fabric", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void HouseDrive_MotionSpansFrames()
    {
        Scene scene = LoadMovie();

        IReadOnlyList<StudioFrame> frames = StudioTimeline.ResolveFrames(scene.StudioCameras, scene.StudioShots);
        Assert.Equal(48, frames.Count);

        MotionTrack track = Assert.Single(scene.MotionTracks);
        MotionPose? start = track.Sample(0);
        MotionPose? end = track.Sample(47);
        Assert.NotNull(start);
        Assert.NotNull(end);
        Assert.True(Math.Abs(start.Position.X - end.Position.X) > 5.0);
        Assert.Equal(Math.PI, start.Rotation.Y, precision: 9);
        Assert.Equal(Math.PI / 2.0, end.Rotation.Y, precision: 9);
    }

    [Fact]
    public void HouseDrive_TrackStoresRelativeDeltas()
    {
        Scene scene = LoadMovie();

        MotionTrack track = Assert.Single(scene.MotionTracks);
        Assert.Equal(4, track.Keys.Count);
        Assert.Equal(new Vec3(-11, track.Keys[0].Position.Y, 8), track.Keys[0].Position);
        Assert.Equal(new Vec3(0, 0, -19), track.Keys[1].Position);
        Assert.Equal(Vec3.Zero, track.Keys[1].Rotation);
        Assert.Equal(-Math.PI / 2.0, track.Keys[2].Rotation.Y, precision: 9);
        Assert.Equal(new Vec3(11, 0, 0), track.Keys[3].Position);
    }
}
