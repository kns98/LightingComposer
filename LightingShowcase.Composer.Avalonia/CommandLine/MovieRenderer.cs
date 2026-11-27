// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/MovieRenderer.cs
// Purpose: Shared timeline movie loop for headless and UI rendering.
//
// Poses each frame, renders through the injected backend callback, writes a
// PNG sequence plus an animated GIF, and always restores the authored scene
// transforms afterwards.
// -----------------------------------------------------------------------------
using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

internal sealed record MovieRenderResult(
    IReadOnlyList<string> PngPaths,
    string GifPath,
    int FrameCount,
    IReadOnlyList<double> FrameTimes,
    string Details);

internal static class MovieRenderer
{
    /// <summary>Renders every timeline frame, posing the scene per frame and restoring it afterwards.</summary>
    public static MovieRenderResult RenderTimeline(
        Scene scene,
        IReadOnlyList<StudioFrame> frames,
        IReadOnlyList<MotionTrack> tracks,
        MovieSettings settings,
        string outputDirectory,
        Func<CameraDefinition, RenderImage> renderFrame,
        Action<int, int, double> progress,
        CancellationToken cancellationToken)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (frames == null) throw new ArgumentNullException(nameof(frames));
        if (tracks == null) throw new ArgumentNullException(nameof(tracks));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(renderFrame);
        ArgumentNullException.ThrowIfNull(progress);
        if (frames.Count == 0) throw new ArgumentException("The timeline has no frames. Add a shot first.");
        settings.Validate();
        Directory.CreateDirectory(outputDirectory);

        Dictionary<int, MotionPose> snapshot = StudioPosing.Capture(scene);
        try
        {
            List<RenderImage> images = new(frames.Count);
            List<string> pngPaths = new(frames.Count);
            List<double> frameTimes = new(frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StudioPosing.ApplyPoses(scene, StudioTimeline.SamplePoses(tracks, frames[i].GlobalFrame));
                scene.RebuildWorldGeometry();
                Stopwatch frame = Stopwatch.StartNew();
                RenderImage image = renderFrame(frames[i].Camera);
                frame.Stop();
                string png = Path.Combine(outputDirectory, $"{settings.OutputName}.frame-{i + 1:0000}.png");
                image.SavePng(png);
                images.Add(image);
                pngPaths.Add(png);
                frameTimes.Add(frame.Elapsed.TotalMilliseconds);
                progress(i + 1, frames.Count, frame.Elapsed.TotalMilliseconds);
            }

            string gif = Path.Combine(outputDirectory, $"{settings.OutputName}.gif");
            GifWriter.Write(gif, images, Math.Max(1, (int)Math.Round(100.0 / settings.Fps)));
            string details =
                $"movie - {frames.Count} frames {settings.Width}x{settings.Height}, " +
                $"avg={frameTimes.Average():0.0}ms min={frameTimes.Min():0.0}ms max={frameTimes.Max():0.0}ms, {settings.Fps} fps";
            return new MovieRenderResult(pngPaths, gif, frames.Count, frameTimes, details);
        }
        finally
        {
            StudioPosing.ApplyPoses(scene, snapshot);
            scene.RebuildWorldGeometry();
        }
    }
}
