// File: Rendering/Compute/ComputeFrameRenderer.cs
// Purpose: Orchestrates one compute trace frame from caches to readback.

using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class ComputeFrameRenderer
{
    private readonly ComputeDeviceFactory devices;
    private readonly ComputeSceneCache scenes;
    private readonly ComputePipelineFactory pipelines;
    private readonly ComputeStageLog log;

    public ComputeFrameRenderer(
        ComputeDeviceFactory devices, ComputeSceneCache scenes,
        ComputePipelineFactory pipelines, ComputeStageLog log)
    {
        this.devices = devices;
        this.scenes = scenes;
        this.pipelines = pipelines;
        this.log = log;
    }

    /// <summary>Renders the scene with Vulkan compute under the render lock.</summary>
    public RenderImage Render(ComputeFrameRequest request, out string details)
    {
        if (request.Width <= 0 || request.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Width), "Vulkan render target dimensions must be positive.");
        if (request.SampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.SampleCount), "Vulkan sample count must be positive.");

        request = request with
        {
            SampleCount = Math.Clamp(request.SampleCount, 1, 4096),
            Settings = request.Settings ?? new RenderSettings
            {
                Width = request.Width,
                Height = request.Height,
                PathBounceCount = request.BounceCount,
                Backend = RenderBackend.VulkanGpu
            },
            FieldOfViewDegrees = Math.Clamp(request.FieldOfViewDegrees, 1.0, 179.0)
        };

        log.Reset(request.Width, request.Height, request.BounceCount, request.SampleIndex);
        log.Stage($"Sample batch: start={request.SampleIndex}, count={request.SampleCount}");
        log.Stage("Render entered");
        log.Memory("start");

        log.ThrowIfCancellationRequested(request.Token, "before shared Vulkan device");
        GraphicsDevice gd = devices.GetOrCreate();
        ResourceFactory factory = gd.ResourceFactory;

        log.Stage("Get or build cached Vulkan compute scene");
        PreparedComputeScene prepared = scenes.GetOrCreate(gd, request.Scene, request.Token);

        log.Stage($"Cached scene buffers={prepared.SceneBufferBytes / (1024.0 * 1024.0):F1} MB; per-frame output+readback={(FrameResources.OutputBytesFor(request.Width, request.Height) * 2.0) / (1024.0 * 1024.0):F1} MB");
        using FrameResources frame = new(gd, request.Width, request.Height, log, request.Token);

        log.Stage("Initialize output buffer");
        new FrameOutput(log).Zero(gd, frame.Output, frame.OutputBytes);

        log.Stage("Get shared compute pipeline");
        log.ThrowIfCancellationRequested(request.Token, "Get shared compute pipeline");
        SharedComputeResources computeResources = pipelines.GetOrCreate(gd);
        frame.CreateSet(log, factory, computeResources.Layout, prepared);

        // Submit the compute shader in row tiles. When no per-tile preview is
        // needed, batch all tile dispatches into one command-list submission and
        // wait only once; waiting after every tile serializes the GPU.
        int tileRows = TileScheduler.ChooseTileRows(request.Width, request.Height, prepared.TriangleCount, request.BounceCount, prepared.LightCount, request.SampleCount);
        log.Stage($"Dispatch compute shader in row tiles: tileRows={tileRows}");

        if (request.ProgressCallback == null)
        {
            new BatchedTileDispatcher(log).Dispatch(
                gd, computeResources.Pipeline, computeResources.Layout, frame.Output,
                prepared, request, tileRows, frame.ConstantBytes);
        }
        else
        {
            new PreviewTileDispatcher(log).Dispatch(
                gd, computeResources.Pipeline, frame.Set, frame.Constants, frame.Output, frame.Staging, frame.OutputBytes,
                prepared, request, tileRows, request.ProgressCallback);
        }

        log.Stage("All compute tiles completed");
        log.Stage("Copy output buffer to staging buffer");
        RenderImage image = new FrameOutput(log).ReadBack(
            gd, factory, frame.Output, frame.Staging, frame.OutputBytes,
            request.Width, request.Height, request.Token, "final readback");
        details = ComputeFrameDetails.Build(request, prepared, tileRows);
        log.Stage("Render completed successfully");
        return image;
    }
}
