// File: Rendering/Raster/FrameRenderer.cs
// Purpose: Orchestrates one hardware-rasterized frame from caches to readback.

using System.Diagnostics;
using System.Numerics;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class FrameRenderer
{
    private readonly RasterDeviceFactory devices;
    private readonly RasterTargetCache targets;
    private readonly RasterPipelineFactory pipelines;
    private readonly RasterShadowFactory shadows;
    private readonly PreparedSceneCache scenes;
    private readonly RasterStageLog log;
    private readonly RasterOptions options;

    public FrameRenderer(
        RasterDeviceFactory devices, RasterTargetCache targets, RasterPipelineFactory pipelines,
        RasterShadowFactory shadows, PreparedSceneCache scenes, RasterStageLog log, RasterOptions options)
    {
        this.devices = devices;
        this.targets = targets;
        this.pipelines = pipelines;
        this.shadows = shadows;
        this.scenes = scenes;
        this.log = log;
        this.options = options;
    }

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    public RenderImage Render(FrameRequest request, out string details)
    {
        ArgumentNullException.ThrowIfNull(request.Scene);
        request = request with { Width = Math.Max(1, request.Width), Height = Math.Max(1, request.Height) };

        Stopwatch total = Stopwatch.StartNew();
        Stopwatch phase = Stopwatch.StartNew();
        log.Reset(request.Width, request.Height);
        log.ThrowIfCancellationRequested(request.Token, "create Vulkan device");
        GraphicsDevice gd = devices.GetOrCreate();
        long deviceMs = phase.ElapsedMilliseconds;

        phase.Restart();
        RasterTargets rasterTargets = targets.GetOrCreate(gd, request.Width, request.Height);
        SharedRasterResources resources = pipelines.GetOrCreate(gd, rasterTargets.Framebuffer.OutputDescription);
        RasterShadowResources shadow = shadows.GetOrCreate(gd);
        long targetMs = phase.ElapsedMilliseconds;

        phase.Restart();
        PreparedRasterScene prepared = scenes.GetOrCreate(gd, resources, shadow, request.Scene, request.Token);
        long prepareMs = phase.ElapsedMilliseconds;

        phase.Restart();
        new MeshEditPreviewApplier(gd, log).Apply(prepared, request.MeshEditPreview, request.Token);
        long meshEditUploadMs = phase.ElapsedMilliseconds;

        phase.Restart();
        double cameraFar = CameraPlanes.CachedFarPlane(prepared.Stats.BoundingCenter, prepared.Stats.BoundingRadius, request.CameraPosition);
        gd.UpdateBuffer(prepared.Buffers.Camera, 0, new RasterCameraConstants(
            request.CameraPosition, request.Basis, request.Width, request.Height,
            prepared.Stats.LightCount, prepared.Stats.TextureCount, options.RasterDebugMode, prepared.Stats.MaterialCount, cameraFar, request.Exposure));
        PreviewRangeSet? previewRanges = null;
        if (request.TransformPreview != null && !request.TransformPreview.IsIdentity && request.TransformPreview.GroupIds.Count > 0)
        {
            gd.UpdateBuffer(prepared.Buffers.PreviewTransform, 0, new RasterTransformConstants(request.TransformPreview));
            previewRanges = prepared.PreviewCache.GetOrCreate(request.TransformPreview, prepared.GroupRanges);
        }
        gd.UpdateBuffer(resources.BackgroundConstants, 0, new RasterBackgroundConstants(request.BackgroundTop, request.BackgroundBottom, request.Exposure));
        if (!request.Wireframe && prepared.ShadowLights.Count > 0)
            UpdateShadowCameras(gd, shadow, prepared);
        long uniformMs = phase.ElapsedMilliseconds;

        log.ThrowIfCancellationRequested(request.Token, "record Vulkan raster command list");
        phase.Restart();
        new FramePassRecorder().Record(
            rasterTargets.CommandList, rasterTargets, resources, shadow, prepared, previewRanges, request.Wireframe, request.Width, request.Height);
        long recordMs = phase.ElapsedMilliseconds;

        log.ThrowIfCancellationRequested(request.Token, "submit Vulkan raster command list");
        phase.Restart();
        gd.ResetFence(rasterTargets.Fence);
        gd.SubmitCommands(rasterTargets.CommandList, rasterTargets.Fence);
        gd.WaitForFence(rasterTargets.Fence);
        long gpuWaitMs = phase.ElapsedMilliseconds;

        log.ThrowIfCancellationRequested(request.Token, "read back Vulkan raster texture");
        phase.Restart();
        RenderImage image = StagingReadback.Read(gd, rasterTargets.StagingTexture, request.Width, request.Height);
        long readbackMs = phase.ElapsedMilliseconds;
        total.Stop();

        FrameTimings timings = new(deviceMs, targetMs, prepareMs, meshEditUploadMs, uniformMs, recordMs, gpuWaitMs, readbackMs);
        details = FrameDetails.Build(request, prepared, timings, total.ElapsedMilliseconds);
        log.Stage(details);
        return image;
    }

    /// <summary>Uploads per-frame shadow camera rows to the shared shadow buffers.</summary>
    private static void UpdateShadowCameras(GraphicsDevice gd, RasterShadowResources shadow, PreparedRasterScene prepared)
    {
        Vector4[] rows = ShadowCameraBuilder.BuildFrameRows(prepared.ShadowLights, prepared.Stats.BoundingCenter, prepared.Stats.BoundingRadius);
        gd.UpdateBuffer(shadow.ShadowMatrices, 0, rows);
        for (int layer = 0; layer < RasterLimits.MaxShadowLights; layer++)
            gd.UpdateBuffer(shadow.LayerUniforms[layer], 0, rows.AsSpan(layer * 5, 5).ToArray());
    }
}
