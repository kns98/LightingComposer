// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/RenderJobRunner.cs
// Purpose: Headless render job execution.
//
// Resolves the scene input, runs the render request, and returns a JSON-serializable result record.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

public sealed class RenderJobRunner
{
    private static readonly SemaphoreSlim RenderGate = new(1, 1);

    /// <summary>Runs the Vulkan device preflight check and returns true when the renderer child argument is present.</summary>
    public static bool TryHandleRendererProcessArgument(string[] args, out int exitCode)
    {
        if (args.Length == 1 && string.Equals(args[0], VeldridVulkanDevicePreflight.ChildArgument, StringComparison.Ordinal))
        {
            exitCode = VeldridVulkanDevicePreflight.RunChildDeviceCreationTest();
            return true;
        }

        exitCode = 0;
        return false;
    }

    /// <summary>Disposes the shared Vulkan compute and raster renderer devices.</summary>
    public static void DisposeSharedResources()
    {
        VulkanSceneComputeRenderer.DisposeSharedDevice();
        VulkanRasterRenderer.DisposeSharedDevice();
    }

    /// <summary>Validates the request and runs the render job while holding the exclusive render gate.</summary>
    public async Task<RenderJobResult> RunAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        await RenderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return RunExclusive(request, cancellationToken);
        }
        finally
        {
            RenderGate.Release();
        }
    }

    /// <summary>Loads the scene, builds the camera, renders with the requested backend, and saves the output image.</summary>
    private static RenderJobResult RunExclusive(RenderRequest request, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ResolvedSceneInput input = SceneInputResolver.Resolve(request.Input!);
        TextureMap.ConfigureAssetRoots([input.AssetDirectory]);

        Scene scene = LoadScene(input.ScenePath);
        if (scene.Triangles.Count == 0)
            throw new InvalidDataException("The scene contains no renderable triangles.");

        CameraDefinition camera = BuildCamera(scene, request);
        HeadlessLighting.Resolve(scene, camera, request);
        RenderSettings renderSettings = request.ToRenderSettings();
        int triangleCount = scene.Triangles.Count;
        int lightCount = scene.Lights.Count;
        string rendererName = RenderRequest.BackendName(request.Backend);
        string outputPath = ResolveOutputPath(request.Output, input.ScenePath, request.Frames > 1);
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        Console.WriteLine(
            $"Rendering with {rendererName}: {triangleCount:N0} triangles, {lightCount:N0} lights at " +
            $"{request.Width}x{request.Height}; samples={request.Samples}, bounces={request.Bounces}, " +
            $"fov={request.FieldOfViewDegrees:0.##}, assets={input.AssetDirectory}.");

        if (request.Backend == RenderBackend.VulkanGpu)
        {
            // The editable hierarchy is redundant after the Vulkan compute
            // renderer has packed the world triangles for a headless render.
            scene.ReleaseEditorGeometryForHeadlessRender();
        }

        string details;
        List<double>? frameTimes = null;
        if (request.Frames > 1)
        {
            (details, frameTimes) = RenderTurntableGif(camera, request, outputPath, RenderFrame, cancellationToken);
        }
        else
        {
            RenderImage image = RenderFrame(camera);
            image.SavePng(outputPath);
        }

        RenderImage RenderFrame(CameraDefinition view)
        {
            return request.Backend switch
            {
                RenderBackend.VulkanGpu => VulkanSceneComputeRenderer.Render(
                    scene,
                    view.Position,
                    view.ToBasis(),
                    request.Width,
                    request.Height,
                    request.Bounces,
                    0,
                    request.Samples,
                    cancellationToken,
                    out details,
                    progressCallback: null,
                    settings: renderSettings,
                    fieldOfViewDegrees: request.FieldOfViewDegrees),
                RenderBackend.Cpu => CpuCommandLineRenderer.Render(
                    scene, view, request, cancellationToken, out details),
                RenderBackend.ShadowRasterPreview => ShadowRasterRenderer.Render(
                    scene,
                    view.Position,
                    view.ToBasis(),
                    request.Width,
                    request.Height,
                    cancellationToken,
                    out details,
                    backgroundTop: renderSettings.BackgroundTop,
                    backgroundBottom: renderSettings.BackgroundBottom),
                RenderBackend.VulkanRasterPreview => VulkanRasterRenderer.Render(
                    scene,
                    view.Position,
                    view.ToBasis(),
                    request.Width,
                    request.Height,
                    cancellationToken,
                    out details,
                    backgroundTop: renderSettings.BackgroundTop,
                    backgroundBottom: renderSettings.BackgroundBottom),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request.Backend), request.Backend, "Unsupported command-line renderer."),
            };
        }

        stopwatch.Stop();
        return new RenderJobResult(
            rendererName,
            Path.GetFileName(input.ScenePath),
            input.AssetDirectory,
            request.Width,
            request.Height,
            request.Samples,
            request.Bounces,
            request.FieldOfViewDegrees,
            request.Exposure,
            request.AmbientStrength,
            request.UseShadows,
            triangleCount,
            lightCount,
            stopwatch.Elapsed.TotalSeconds,
            details,
            outputPath,
            frameTimes);
    }

    /// <summary>Renders one full camera orbit as an animated GIF with per-frame timings.</summary>
    private static (string Details, List<double> FrameTimes) RenderTurntableGif(
        CameraDefinition camera, RenderRequest request, string outputPath,
        Func<CameraDefinition, RenderImage> renderFrame, CancellationToken cancellationToken)
    {
        IReadOnlyList<Vec3> positions = TurntableCamera.Orbit(camera, request.Frames);
        List<RenderImage> frames = new(positions.Count);
        List<double> frameTimes = new(positions.Count);
        for (int i = 0; i < positions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CameraDefinition frameCamera = new()
            {
                Position = positions[i],
                Target = camera.Target,
                Up = camera.Up,
                FieldOfViewDegrees = camera.FieldOfViewDegrees
            };
            Stopwatch frame = Stopwatch.StartNew();
            frames.Add(renderFrame(frameCamera));
            frame.Stop();
            frameTimes.Add(frame.Elapsed.TotalMilliseconds);
            Console.WriteLine($"Frame {i + 1}/{positions.Count}: {frame.Elapsed.TotalMilliseconds:0} ms");
        }

        int delayCentiseconds = Math.Max(1, (int)Math.Round(100.0 / request.Fps));
        GifWriter.Write(outputPath, frames, delayCentiseconds);
        string details =
            $"{RenderRequest.BackendName(request.Backend)} turntable - {frames.Count} frames {request.Width}x{request.Height}, " +
            $"avg={frameTimes.Average():0.0}ms min={frameTimes.Min():0.0}ms max={frameTimes.Max():0.0}ms, {request.Fps} fps";
        return (details, frameTimes);
    }

    /// <summary>Loads a scene from a binary scene file or an importable model file.</summary>
    private static Scene LoadScene(string scenePath)
    {
        PluginBootstrap.EnsureLoaded();
        Scene scene = new();
        if (SupportedSceneFormats.IsBinaryScenePath(scenePath))
        {
            string description = BinarySceneFile.LoadIntoScene(scene, scenePath);
            scene.SetDescription(description);
            return scene;
        }

        scene.OpenModelFile(scenePath, progress =>
        {
            if (progress.Percent % 10 == 0 || progress.Percent == 100)
                Console.WriteLine($"Import {progress.Percent,3}%: {progress.Stage} ({progress.TriangleCount:N0} triangles)");
        });
        return scene;
    }

    /// <summary>Builds the render camera from explicit request vectors or scene-bounds defaults.</summary>
    private static CameraDefinition BuildCamera(Scene scene, RenderRequest request)
    {
        Aabb? bounds = ComputeTriangleBounds(scene.Triangles);
        Vec3 center = bounds.HasValue ? (bounds.Value.Min + bounds.Value.Max) * 0.5 : new Vec3(0, 0.55, 0);
        Vec3 extent = bounds.HasValue ? bounds.Value.Max - bounds.Value.Min : new Vec3(2, 2, 2);
        double radius = Math.Max(0.25, extent.Length() * 0.5);

        Vec3 target = RenderRequest.ParseVector(request.CameraTarget, nameof(request.CameraTarget)) ?? center;
        Vec3 position = RenderRequest.ParseVector(request.CameraPosition, nameof(request.CameraPosition))
            ?? target + new Vec3(radius * 0.75, radius * 0.45, -radius * 2.25) / request.Zoom;
        Vec3 up = RenderRequest.ParseVector(request.CameraUp, nameof(request.CameraUp)) ?? new Vec3(0, 1, 0);

        return new CameraDefinition
        {
            Position = position,
            Target = target,
            Up = up,
            FieldOfViewDegrees = request.FieldOfViewDegrees,
            FarPlane = Math.Max(5000.0, radius * 20.0)
        };
    }

    /// <summary>Computes the bounding box of the given triangles, or null when there are none.</summary>
    private static Aabb? ComputeTriangleBounds(IReadOnlyList<Triangle> triangles)
    {
        if (triangles.Count == 0) return null;

        Vec3 first = triangles[0].A;
        double minX = first.X, minY = first.Y, minZ = first.Z;
        double maxX = first.X, maxY = first.Y, maxZ = first.Z;
        foreach (Triangle triangle in triangles)
        {
            Expand(triangle.A);
            Expand(triangle.B);
            Expand(triangle.C);
        }
        return new Aabb(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));

        /// <summary>Expands the running bounding box to include the given point.</summary>
        void Expand(Vec3 value)
        {
            minX = Math.Min(minX, value.X);
            minY = Math.Min(minY, value.Y);
            minZ = Math.Min(minZ, value.Z);
            maxX = Math.Max(maxX, value.X);
            maxY = Math.Max(maxY, value.Y);
            maxZ = Math.Max(maxZ, value.Z);
        }
    }

    /// <summary>Resolves the render output path, defaulting to a render image beside the scene file.</summary>
    private static string ResolveOutputPath(string? requested, string scenePath, bool animated)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return Path.GetFullPath(requested);

        string directory = Path.GetDirectoryName(scenePath) ?? Environment.CurrentDirectory;
        string fileName = Path.GetFileNameWithoutExtension(scenePath);
        if (scenePath.EndsWith(".prop.xml", StringComparison.OrdinalIgnoreCase))
            fileName = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directory, animated ? $"{fileName}-render.gif" : $"{fileName}-render.png");
    }
}
