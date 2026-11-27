using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class MovieRendererTests
{
    private static Scene TestScene(out int boxId)
    {
        Scene scene = new();
        scene.BeginGroup("box");
        scene.AddTriangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Material(new Vec3(1, 1, 1)));
        scene.EndGroup();
        boxId = scene.ObjectGroups[0].Id;
        return scene;
    }

    private static (IReadOnlyList<StudioFrame> Frames, MovieSettings Settings) TestTimeline(Scene scene, int boxId)
    {
        SceneCamera camera = scene.AddStudioCamera("Wide", new CameraDefinition
        {
            Position = new Vec3(4, 2, 0),
            Target = new Vec3(0, 1, 0)
        });
        scene.StudioShots.Add(new StudioShot("A", camera.Id, 2));
        scene.StudioShots.Add(new StudioShot("B", camera.Id, 2, StudioShotMove.Push, 0.5));
        MotionTrack track = scene.GetOrAddMotionTrack(boxId);
        track.AddKey(new MotionKeyframe(0, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1)));
        track.AddKey(new MotionKeyframe(3, new Vec3(1, 0, 0), Vec3.Zero, new Vec3(1, 1, 1)));
        MovieSettings settings = new() { Fps = 12, Width = 32, Height = 24, OutputName = "take" };
        return (StudioTimeline.ResolveFrames(scene.StudioCameras, scene.StudioShots), settings);
    }

    [Fact]
    public void RenderTimeline_WritesPngsAndGifAndRestores()
    {
        string directory = Directory.CreateTempSubdirectory("movie-test").FullName;
        try
        {
            Scene scene = TestScene(out int boxId);
            (IReadOnlyList<StudioFrame> frames, MovieSettings settings) = TestTimeline(scene, boxId);
            int progressCalls = 0;
            RenderImage RenderStub(CameraDefinition view) => new(32, 24, new uint[32 * 24]);

            MovieRenderResult result = MovieRenderer.RenderTimeline(
                scene, frames, scene.MotionTracks, settings, directory,
                RenderStub,
                (done, total, _) => { progressCalls++; Assert.Equal(4, total); },
                CancellationToken.None);

            Assert.Equal(4, result.FrameCount);
            Assert.Equal(4, result.PngPaths.Count);
            Assert.True(result.GifPath.EndsWith("take.gif", StringComparison.Ordinal));
            foreach (string png in result.PngPaths)
                Assert.True(File.Exists(png));
            Assert.True(File.Exists(result.GifPath));
            Assert.Equal(4, progressCalls);
            Assert.Equal(Vec3.Zero, scene.GroupById(boxId)!.Position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RenderTimeline_RestoresAfterCancel()
    {
        string directory = Directory.CreateTempSubdirectory("movie-cancel-test").FullName;
        try
        {
            Scene scene = TestScene(out int boxId);
            (IReadOnlyList<StudioFrame> frames, MovieSettings settings) = TestTimeline(scene, boxId);
            using CancellationTokenSource cts = new();
            RenderImage RenderStub(CameraDefinition view)
            {
                cts.Cancel();
                return new RenderImage(32, 24, new uint[32 * 24]);
            }

            Assert.Throws<OperationCanceledException>(() => MovieRenderer.RenderTimeline(
                scene, frames, scene.MotionTracks, settings, directory,
                RenderStub, (_, _, _) => { }, cts.Token));
            Assert.Equal(Vec3.Zero, scene.GroupById(boxId)!.Position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RenderTimeline_RejectsEmptyTimeline()
    {
        Scene scene = TestScene(out _);
        Assert.Throws<ArgumentException>(() => MovieRenderer.RenderTimeline(
            scene, Array.Empty<StudioFrame>(), scene.MotionTracks, new MovieSettings(), ".",
            _ => new RenderImage(32, 24, new uint[32 * 24]), (_, _, _) => { }, CancellationToken.None));
    }

    [Fact]
    public void TimelineFlag_ParsesAndRejectsConflicts()
    {
        RenderRequest request = RenderRequest.FromCommandLine(CommandLineArguments.Parse(new[] { "--timeline", "scene.lscn" }));
        Assert.True(request.Timeline);
        Assert.Equal("scene.lscn", request.Input);

        Assert.Throws<ArgumentException>(() => RenderRequest.FromCommandLine(
            CommandLineArguments.Parse(new[] { "--timeline", "--frames", "5", "scene.lscn" })));
        Assert.Throws<ArgumentException>(() => RenderRequest.FromCommandLine(
            CommandLineArguments.Parse(new[] { "--timeline", "--fps=24", "scene.lscn" })));
        Assert.Throws<ArgumentException>(() => RenderRequest.FromCommandLine(
            CommandLineArguments.Parse(new[] { "--timeline", "--camera-position=1,2,3", "scene.lscn" })));
        Assert.Throws<ArgumentException>(() => RenderRequest.FromCommandLine(
            CommandLineArguments.Parse(new[] { "--timeline", "--output=movie.png", "scene.lscn" })));
    }
}
