// -----------------------------------------------------------------------------
// File: Rendering/VulkanRasterRenderer.cs
// Purpose: Vulkan graphics-pipeline raster preview.
//
// This renderer is intentionally separate from VulkanSceneComputeRenderer.  It
// uses Vulkan's normal vertex/fragment raster pipeline for fast preview frames:
// vertex buffer -> depth-tested triangle rasterization -> fragment lighting ->
// off-screen color texture -> staging readback -> cross-platform RGBA image.
// -----------------------------------------------------------------------------

// Import diagnostics for stopwatch timing
using System.Diagnostics;
// Import vector math types
using System.Numerics;
// Import struct layout attributes
using System.Runtime.InteropServices;
// Import string building utilities
using System.Text;
// Import cancellation token support
using System.Threading;
// Import camera basis types
using LightingShowcase.CameraSystem;
// Import scene light types
using LightingShowcase.Lighting;
// Import Vec3 math types
using LightingShowcase.Math3D;
// Import scene graph types
using LightingShowcase.SceneGraph;
// Import Veldrid graphics API
using Veldrid;
// Import SPIRV shader compilation
using Veldrid.SPIRV;
// Import CPU preparation helpers
using static LightingShowcase.Rendering.VulkanRasterCpu;
// Import transform helpers
using static LightingShowcase.Rendering.VulkanRasterTransform;

// Declare rendering namespace
namespace LightingShowcase.Rendering;

/// <summary>Vulkan hardware rasterizer for the Render tab preview.</summary>
// Declare Vulkan raster renderer class
public static class VulkanRasterRenderer
// Begin renderer class
{
    // Preserve public log path API surface
    public static string StageLogPath => VulkanRasterCpu.StageLogPath;
    // Guard shared graphics device
    private static readonly object DeviceSync = new();
    // Guard render operations
    private static readonly object RenderSync = new();
    // Cache shared graphics device
    private static GraphicsDevice? sharedGraphicsDevice;
    // Cache shared raster pipelines
    private static SharedRasterResources? sharedRasterResources;
    // Cache prepared scene buffers
    private static PreparedRasterScene? preparedScene;
    // Interactive and settled frames use different resolutions. Keep a small
    // LRU so orbit/release does not recreate color, depth, staging and sync
    // resources every time the viewer switches between those sizes.
    // Limit cached target sizes
    private const int MaxCachedTargetSizes = 2;
    // Cache render targets by size
    private static readonly Dictionary<(int Width, int Height), RasterTargets> sharedTargets = new();
    // Track target use order
    private static long targetUseSerial;
    // Flag preflight completion
    private static bool preflightCompleted;

    /// <summary>Releases the shared Vulkan graphics device and compiled raster pipeline.</summary>
    // Dispose shared Vulkan device
    public static void DisposeSharedDevice()
    // Begin DisposeSharedDevice body
    {
        // Disposal must not race an in-flight render. Veldrid object disposal during
        // command submission/readback can produce native driver crashes that bypass
        // normal C# exception handling.
        // Lock render operations
        lock (RenderSync)
        // Begin render lock
        {
            // Lock graphics device
            lock (DeviceSync)
            // Begin device lock
            {
                // Capture shared device
                GraphicsDevice? device = sharedGraphicsDevice;
                // Clear shared device
                sharedGraphicsDevice = null;
                // Capture shared resources
                SharedRasterResources? resources = sharedRasterResources;
                // Clear shared resources
                sharedRasterResources = null;
                // Capture prepared scene
                PreparedRasterScene? sceneResources = preparedScene;
                // Clear prepared scene
                preparedScene = null;
                // Snapshot cached targets
                RasterTargets[] targets = sharedTargets.Values.ToArray();
                // Clear target cache
                sharedTargets.Clear();

                // Check device exists
                if (device != null)
                // Begin device null check
                {
                    // Wait for GPU idle
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice: WaitForIdle"); device.WaitForIdle(); } catch { }
                // End device null check
                }

                // Loop over cached targets
                foreach (RasterTargets target in targets)
                // Begin target loop
                {
                    // Dispose one target safely
                    try { target.Dispose(); } catch { }
                // End target loop
                }
                // Dispose scene resources safely
                try { sceneResources?.Dispose(); } catch { }
                // Dispose shared resources safely
                try { resources?.Dispose(); } catch { }
                // Check device exists
                if (device != null)
                // Begin device null check
                {
                    // Dispose shared device with staging log
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice"); device.Dispose(); } catch { }
                // End device null check
                }
            // End DeviceSync lock
            }
        // End RenderSync lock
        }
    // End DisposeSharedDevice method
    }

    /// <summary>
    /// Releases scene-sized Vulkan buffers while keeping the device, pipelines,
    /// and small render-target cache alive. Call this before replacing a large
    /// scene so the old GPU allocation does not overlap the new scene load.
    /// </summary>
    // Release cached scene GPU buffers
    public static void ReleasePreparedScene()
    // Begin ReleasePreparedScene method
    {
        // Serialize against concurrent renders
        lock (RenderSync)
        // Begin RenderSync lock
        {
            // Serialize against device lifecycle changes
            lock (DeviceSync)
            // Begin DeviceSync lock
            {
                // Snapshot cached scene resources
                PreparedRasterScene? sceneResources = preparedScene;
                // Clear cached scene reference
                preparedScene = null;
                // Skip release when cache empty
                if (sceneResources == null)
                    // Exit without cached scene
                    return;

                // Drain GPU before releasing buffers
                try { sharedGraphicsDevice?.WaitForIdle(); } catch { }
                // Dispose scene GPU buffers
                try { sceneResources.Dispose(); } catch { }
            // End DeviceSync lock
            }
        // End RenderSync lock
        }
    // End ReleasePreparedScene method
    }

    /// <summary>
    /// Reuploads only vertex positions, normals, UVs and material indices into an
    /// already allocated Vulkan scene. This is the fast path for baked transforms:
    /// textures, pipelines, descriptors, render targets and buffer allocations stay hot.
    /// </summary>
    // Refresh cached geometry in place
    public static bool TryRefreshPreparedGeometry(
        // Scene providing updated geometry
        Scene scene,
        // Cancellation token for refresh
        CancellationToken cancellationToken,
        // Output refresh status message
        out string details)
    // Begin TryRefreshPreparedGeometry method
    {
        // Reject null scene argument
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        // Serialize against concurrent renders
        lock (RenderSync)
        // Begin RenderSync lock
        {
            // Serialize against device lifecycle changes
            lock (DeviceSync)
            // Begin DeviceSync lock
            {
                // Snapshot cached scene
                PreparedRasterScene? prepared = preparedScene;
                // Snapshot shared graphics device
                GraphicsDevice? gd = sharedGraphicsDevice;
                // Require matching cached scene
                if (prepared == null || gd == null || !ReferenceEquals(prepared.Scene, scene))
                // Begin cache mismatch branch
                {
                    // Report missing compatible cache
                    details = "No compatible Vulkan raster scene cache is active.";
                    // Fail without compatible cache
                    return false;
                // End cache mismatch branch
                }
                // Skip refresh when cache current
                if (prepared.CacheStamp.Matches(scene))
                // Begin cache current branch
                {
                    // Report cache already current
                    details = "Vulkan raster geometry cache is already current.";
                    // Succeed without reuploading
                    return true;
                // End cache current branch
                }

                // Count opaque triangles
                int opaqueTriangles = 0;
                // Count transparent triangles
                int transparentTriangles = 0;
                // Scan scene triangles for changes
                foreach (Triangle triangle in scene.Triangles)
                // Begin per-triangle loop
                {
                    // Honor refresh cancellation
                    cancellationToken.ThrowIfCancellationRequested();
                    // Skip non-finite triangles
                    if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
                        // Skip invalid triangle
                        continue;
                    // Detect new or changed material
                    if (!prepared.MaterialIds.ContainsKey(triangle.Material))
                    // Begin material mismatch branch
                    {
                        // Report material-driven rebuild need
                        details = "A material changed, so the Vulkan raster scene requires a full rebuild.";
                        // Require full rebuild for materials
                        return false;
                    // End material mismatch branch
                    }

                    // Classify triangle transparency
                    if (IsTransparentMaterial(triangle.Material))
                        // Tally transparent triangle
                        transparentTriangles++;
                    // Handle opaque triangle case
                    else
                        // Tally opaque triangle
                        opaqueTriangles++;
                // End per-triangle loop
                }

                // Detect topology count mismatch
                if (opaqueTriangles * 3 != prepared.OpaqueVertexCount ||
                    // Check transparent vertex count
                    transparentTriangles * 3 != prepared.TransparentVertexCount)
                // Begin topology mismatch branch
                {
                    // Report topology-driven rebuild need
                    details = "Scene topology or transparency classification changed, so the Vulkan raster scene requires a full rebuild.";
                    // Require full rebuild for topology
                    return false;
                // End topology mismatch branch
                }

                // Time geometry reupload
                Stopwatch stopwatch = Stopwatch.StartNew();
                // Reupload vertex buffers in place
                UploadRasterGeometry(
                    // Target graphics device
                    gd,
                    // Opaque vertex buffer destination
                    prepared.OpaqueVertexBuffer,
                    // Transparent vertex buffer destination
                    prepared.TransparentVertexBuffer,
                    // Source scene geometry
                    scene,
                    // Material index lookup
                    prepared.MaterialIds,
                    // Cancellation token for upload
                    cancellationToken);
                // Recompute scene bounding sphere
                ComputeSceneBounds(scene, out Vec3 center, out double radius);
                // Store refreshed bounding center
                prepared.BoundingCenter = center;
                // Store refreshed bounding radius
                prepared.BoundingRadius = radius;
                // Stamp cache as current
                prepared.CacheStamp = SceneCacheStamp.Capture(scene);
                // Invalidate cached preview ranges
                prepared.PreviewRanges.Clear();
                // Clear stale mesh edit state
                prepared.ActiveMeshEdit = null;
                // Stop refresh timer
                stopwatch.Stop();
                // Report in-place refresh timing
                details = $"Vulkan raster geometry refreshed in-place in {stopwatch.ElapsedMilliseconds} ms; allocations and textures were reused.";
                // Log refresh stage message
                Stage(details);
                // Succeed after geometry refresh
                return true;
            // End DeviceSync lock
            }
        // End RenderSync lock
        }
    // End TryRefreshPreparedGeometry method
    }

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    // Render frame without previews
    public static RenderImage Render(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details) =>
        // Forward to full render overload
        Render(scene, cameraPosition, basis, width, height, null, null, cancellationToken, out details);

    /// <summary>
    /// Renders with an optional editor-only transform. The preview updates one small
    /// uniform buffer and changes draw ranges; it does not rebuild or upload vertices.
    /// </summary>
    // Render frame with transform preview
    public static RenderImage Render(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Optional live transform preview
        VulkanRasterTransformPreview? preview,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details) =>
        // Forward to full render overload
        Render(scene, cameraPosition, basis, width, height, preview, null, cancellationToken, out details);

    /// <summary>
    /// Renders with optional whole-object and mesh-component editor previews.
    /// Component moves patch only affected vertices in the existing GPU buffers.
    /// </summary>
    // Render frame with both previews
    public static RenderImage Render(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Optional live transform preview
        VulkanRasterTransformPreview? preview,
        // Optional live mesh edit preview
        VulkanRasterMeshEditPreview? meshEditPreview,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details,
        // Render wireframe view flag
        bool wireframe = false)
    // Begin Render method
    {
        // Serialize the Veldrid/Vulkan raster path against disposal and resize/backend
        // switches. Without this, closing the form or switching renderers while a
        // preview frame is submitting can dispose the shared device underneath the
        // worker thread and crash inside the graphics driver instead of raising a
        // managed exception.
        // Serialize against disposal and resize
        lock (RenderSync)
        // Begin RenderSync lock
        {
            // Delegate to locked render path
            return RenderLocked(
                // Forward scene and preview arguments
                scene, cameraPosition, basis, width, height, preview, meshEditPreview,
                // Forward cancellation and details
                cancellationToken, out details, wireframe);
        // End RenderSync lock
        }
    // End Render method
    }

    /// <summary>Returns the cached GPU scene, rebuilding buffers, atlas, and descriptors when stale.</summary>
    // Declare cached scene accessor
    private static PreparedRasterScene GetOrCreatePreparedScene(GraphicsDevice gd, SharedRasterResources resources, Scene scene, CancellationToken cancellationToken)
    // Begin GetOrCreatePreparedScene method
    {
        // Read texture sampling setting
        bool gpuTextureSamplingRequested = UseGpuTextureSampling;
        // Check cached scene exists
        if (preparedScene != null &&
            // Check scene cache still valid
            preparedScene.CacheStamp.Matches(scene) &&
            // Check sampling mode unchanged
            preparedScene.GpuTextureSamplingRequested == gpuTextureSamplingRequested)
            // Reuse cached prepared scene
            return preparedScene;
        // Dispose stale prepared scene
        preparedScene?.Dispose();
        // Clear stale scene reference
        preparedScene = null;
        // Abort if rebuild cancelled
        ThrowIfCancellationRequested(cancellationToken, "build cached Vulkan scene");
        // Build fresh prepared scene
        PreparedRasterScene built = BuildPreparedScene(gd, resources, scene, cancellationToken, gpuTextureSamplingRequested);
        // Cache newly prepared scene
        preparedScene = built;
        // Return prepared scene
        return built;
    // End GetOrCreatePreparedScene method
    }

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    // Render frame under render lock
    private static RenderImage RenderLocked(
        // Scene to render
        Scene scene,
        // Camera position
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Optional live transform preview
        VulkanRasterTransformPreview? preview,
        // Optional live mesh edit preview
        VulkanRasterMeshEditPreview? meshEditPreview,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Output render details
        out string details,
        // Render wireframe view flag
        bool wireframe = false)
    // Begin RenderLocked method
    {
        // Reject null scene argument
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        // Clamp width to valid range
        width = Math.Max(1, width);
        // Clamp height to valid range
        height = Math.Max(1, height);

        // Time total frame
        Stopwatch total = Stopwatch.StartNew();
        // Time individual render phases
        Stopwatch phase = Stopwatch.StartNew();
        // Reset per-frame stage log
        ResetStageLog(width, height);
        // Abort before device creation
        ThrowIfCancellationRequested(cancellationToken, "create Vulkan device");
        // Acquire shared graphics device
        GraphicsDevice gd = GetOrCreateSharedDevice();
        // Record device setup time
        long deviceMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Acquire sized render targets
        RasterTargets targets = GetOrCreateTargets(gd, width, height);
        // Acquire shared raster pipelines
        SharedRasterResources resources = GetOrCreateSharedRasterResources(gd, targets.Framebuffer.OutputDescription);
        // Record target setup time
        long targetMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Acquire prepared GPU scene
        PreparedRasterScene prepared = GetOrCreatePreparedScene(gd, resources, scene, cancellationToken);
        // Record scene prepare time
        long prepareMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Detect active mesh edit preview
        bool hasMeshEditPreview = meshEditPreview != null && !meshEditPreview.IsIdentity;
        // Patch edited vertices on GPU
        UpdateMeshEditPreview(gd, prepared, meshEditPreview, cancellationToken);
        // Record mesh edit upload time
        long meshEditUploadMs = phase.ElapsedMilliseconds;

        // Restart phase timer
        phase.Restart();
        // Compute far plane from bounds
        double cameraFar = ComputeCachedCameraFarPlane(prepared.BoundingCenter, prepared.BoundingRadius, cameraPosition);
        // Upload camera constants
        gd.UpdateBuffer(prepared.CameraBuffer, 0, new RasterCameraConstants(
            // Camera and scene count constants
            cameraPosition, basis, width, height, prepared.LightCount, prepared.TextureCount,
            // Debug mode and far plane
            RasterDebugMode, prepared.MaterialCount, cameraFar));
        // Detect active transform preview
        bool hasPreview = preview != null && !preview.IsIdentity && preview.GroupIds.Count > 0;
        // Default to full-buffer draw
        PreviewRangeSet? previewRanges = null;
        // Upload transform when previewing
        if (hasPreview)
        // Begin preview branch
        {
            // Upload preview transform constants
            gd.UpdateBuffer(prepared.PreviewTransformBuffer, 0, new RasterTransformConstants(preview));
            // Resolve preview vertex ranges
            previewRanges = GetOrCreatePreviewRanges(prepared, preview!);
        // End preview branch
        }
        // Record uniform upload time
        long uniformMs = phase.ElapsedMilliseconds;

        // Abort before command recording
        ThrowIfCancellationRequested(cancellationToken, "record Vulkan raster command list");
        // Restart phase timer
        phase.Restart();
        // Reuse cached command list
        CommandList commandList = targets.CommandList;
        // Begin command recording
        commandList.Begin();
        // Bind render targets
        commandList.SetFramebuffer(targets.Framebuffer);
        // Clear color to dark background
        commandList.ClearColorTarget(0, new RgbaFloat(0.035f, 0.040f, 0.050f, 1.0f));
        // Clear depth buffer
        commandList.ClearDepthStencil(1.0f);
        // Branch to wireframe passes
        if (wireframe)
        // Begin wireframe pass branch
        {
            // Fill depth with filled prepass
            if (prepared.OpaqueVertexCount > 0)
            // Begin prepass opaque branch
            {
                // Bind depth prepass pipeline
                commandList.SetPipeline(resources.DepthPrepassPipeline);
                // Bind opaque vertex buffer
                commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
                // Draw opaque prepass vertices
                DrawVertexRanges(commandList, (uint)prepared.OpaqueVertexCount, previewRanges?.Opaque, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End prepass opaque branch
            }
            // Fill depth with transparent prepass
            if (prepared.TransparentVertexCount > 0)
            // Begin prepass transparent branch
            {
                // Bind depth prepass pipeline
                commandList.SetPipeline(resources.DepthPrepassPipeline);
                // Bind transparent vertex buffer
                commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
                // Draw transparent prepass vertices
                DrawVertexRanges(commandList, (uint)prepared.TransparentVertexCount, previewRanges?.Transparent, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End prepass transparent branch
            }
            // Restore background over prepass
            commandList.ClearColorTarget(0, new RgbaFloat(0.035f, 0.040f, 0.050f, 1.0f));
            // Draw solid lines over depth
            if (prepared.OpaqueVertexCount > 0)
            // Begin solid opaque branch
            {
                // Bind solid wireframe pipeline
                commandList.SetPipeline(resources.WireframePipeline);
                // Bind opaque vertex buffer
                commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
                // Draw opaque wireframe vertices
                DrawVertexRanges(commandList, (uint)prepared.OpaqueVertexCount, previewRanges?.Opaque, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End solid opaque branch
            }
            // Draw transparent solid lines
            if (prepared.TransparentVertexCount > 0)
            // Begin solid transparent branch
            {
                // Bind solid wireframe pipeline
                commandList.SetPipeline(resources.WireframePipeline);
                // Bind transparent vertex buffer
                commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
                // Draw transparent wireframe vertices
                DrawVertexRanges(commandList, (uint)prepared.TransparentVertexCount, previewRanges?.Transparent, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End solid transparent branch
            }
            // Draw dashed lines behind depth
            if (prepared.OpaqueVertexCount > 0)
            // Begin dashed opaque branch
            {
                // Bind dashed wireframe pipeline
                commandList.SetPipeline(resources.WireframeDashedPipeline);
                // Bind opaque vertex buffer
                commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
                // Draw opaque dashed vertices
                DrawVertexRanges(commandList, (uint)prepared.OpaqueVertexCount, previewRanges?.Opaque, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End dashed opaque branch
            }
            // Draw transparent dashed lines
            if (prepared.TransparentVertexCount > 0)
            // Begin dashed transparent branch
            {
                // Bind dashed wireframe pipeline
                commandList.SetPipeline(resources.WireframeDashedPipeline);
                // Bind transparent vertex buffer
                commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
                // Draw transparent dashed vertices
                DrawVertexRanges(commandList, (uint)prepared.TransparentVertexCount, previewRanges?.Transparent, prepared.IdentityResourceSet, prepared.PreviewResourceSet);
            // End dashed transparent branch
            }
        // End wireframe pass branch
        }
        // Skip empty opaque pass
        if (!wireframe && prepared.OpaqueVertexCount > 0)
        // Begin opaque pass branch
        {
            // Bind opaque PBR pipeline
            commandList.SetPipeline(resources.OpaquePipeline);
            // Bind opaque vertex buffer
            commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
            // Draw opaque vertices by range
            DrawVertexRanges(
                // Recording command list
                commandList,
                // Total opaque vertex count
                (uint)prepared.OpaqueVertexCount,
                // Opaque preview ranges
                previewRanges?.Opaque,
                // Identity transform resource set
                prepared.IdentityResourceSet,
                // Preview transform resource set
                prepared.PreviewResourceSet);
        // End opaque pass branch
        }
        // Skip empty transparent pass
        if (!wireframe && prepared.TransparentVertexCount > 0)
        // Begin transparent pass branch
        {
            // Bind transparent PBR pipeline
            commandList.SetPipeline(resources.TransparentPipeline);
            // Bind transparent vertex buffer
            commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
            // Draw transparent vertices by range
            DrawVertexRanges(
                // Recording command list
                commandList,
                // Total transparent vertex count
                (uint)prepared.TransparentVertexCount,
                // Transparent preview ranges
                previewRanges?.Transparent,
                // Identity transform resource set
                prepared.IdentityResourceSet,
                // Preview transform resource set
                prepared.PreviewResourceSet);
        // End transparent pass branch
        }
        // Copy render target for readback
        commandList.CopyTexture(targets.ColorTexture, 0, 0, 0, 0, 0,
            // Staging texture copy extents
            targets.StagingTexture, 0, 0, 0, 0, 0, (uint)width, (uint)height, 1, 1);
        // Finish command recording
        commandList.End();
        // Record command recording time
        long recordMs = phase.ElapsedMilliseconds;

        // Abort before GPU submission
        ThrowIfCancellationRequested(cancellationToken, "submit Vulkan raster command list");
        // Restart phase timer
        phase.Restart();
        // Reset frame completion fence
        gd.ResetFence(targets.Fence);
        // Submit commands to GPU
        gd.SubmitCommands(commandList, targets.Fence);
        // Wait for GPU completion
        gd.WaitForFence(targets.Fence);
        // Record GPU wait time
        long gpuWaitMs = phase.ElapsedMilliseconds;

        // Abort before texture readback
        ThrowIfCancellationRequested(cancellationToken, "read back Vulkan raster texture");
        // Restart phase timer
        phase.Restart();
        // Read back rendered image
        RenderImage image = ReadBackImage(gd, targets.StagingTexture, width, height);
        // Record readback time
        long readbackMs = phase.ElapsedMilliseconds;
        // Stop total frame timer
        total.Stop();

        // Describe texture sampling mode
        string textureMode = prepared.UsesGpuTextureSampling
            // GPU texture sampling summary
            ? $"textures={prepared.TextureCount}, pages={prepared.TexturePageCount}"
            // Detect texture fallback case
            : prepared.GpuTextureSamplingRequested && !string.IsNullOrWhiteSpace(prepared.TextureFallbackReason)
                // Texture fallback summary
                ? $"textures=base-color fallback ({prepared.TextureFallbackReason})"
                // Default base color mode
                : "textures=base color";
        // Describe triangle count mode
        string triangleMode = prepared.NearClippedTriangleCount > 0
            // Triangle count with skipped invalid
            ? $"triangles={prepared.TotalTriangleCount}/{prepared.SourceTriangleCount} ({prepared.NearClippedTriangleCount} invalid skipped)"
            // Plain triangle count
            : $"triangles={prepared.TotalTriangleCount}";
        // Describe live transform suffix
        string previewMode = hasPreview ? $", live-transform={preview!.SelectionId}" : string.Empty;
        // Append mesh edit suffix when active
        if (hasMeshEditPreview)
            // Append mesh edit selection id
            previewMode += $", live-mesh-edit={meshEditPreview!.SelectionId}";
        // Build frame timing summary
        details = $"VULKAN RASTER PBR CACHED - {width}x{height}, revision={prepared.CacheStamp.Revision}, {triangleMode}, lights={prepared.LightCount}, {textureMode}, cache={(prepareMs == 0 ? "hot" : "ready")}{previewMode}, device={deviceMs}ms, targets={targetMs}ms, prepare={prepareMs}ms, mesh-edit-upload={meshEditUploadMs}ms, uniform={uniformMs}ms, record={recordMs}ms, gpu+wait={gpuWaitMs}ms, readback={readbackMs}ms, total={total.ElapsedMilliseconds}ms" + (wireframe ? ", wireframe" : string.Empty);
        // Log frame stage summary
        Stage(details);
        // Return rendered image
        return image;
    // End RenderLocked method
    }

    /// <summary>Draws a vertex buffer, switching between identity and preview resource sets per range.</summary>
    // Draw buffer with preview ranges
    private static void DrawVertexRanges(
        // Recording command list
        CommandList commandList,
        // Total buffer vertex count
        uint totalVertexCount,
        // Optional preview vertex ranges
        IReadOnlyList<VertexRange>? previewRanges,
        // Identity transform resource set
        ResourceSet identitySet,
        // Preview transform resource set
        ResourceSet previewSet)
    // Begin DrawVertexRanges method
    {
        // Detect full-buffer draw case
        if (previewRanges == null || previewRanges.Count == 0)
        // Begin full draw branch
        {
            // Bind identity transform set
            commandList.SetGraphicsResourceSet(0, identitySet);
            // Draw entire vertex buffer
            commandList.Draw(totalVertexCount);
            // Exit after full draw
            return;
        // End full draw branch
        }

        // Track drawn vertex offset
        uint cursor = 0;
        // Draw each preview range
        foreach (VertexRange range in previewRanges)
        // Begin per-range loop
        {
            // Detect leading identity gap
            if (range.Start > cursor)
            // Begin identity gap branch
            {
                // Bind identity transform set
                commandList.SetGraphicsResourceSet(0, identitySet);
                // Draw identity gap vertices
                commandList.Draw(range.Start - cursor, 1, cursor, 0);
            // End identity gap branch
            }

            // Skip empty preview range
            if (range.Count > 0)
            // Begin preview range branch
            {
                // Bind preview transform set
                commandList.SetGraphicsResourceSet(0, previewSet);
                // Draw preview range vertices
                commandList.Draw(range.Count, 1, range.Start, 0);
            // End preview range branch
            }
            // Advance drawn vertex cursor
            cursor = Math.Max(cursor, range.Start + range.Count);
        // End per-range loop
        }

        // Detect trailing identity vertices
        if (cursor < totalVertexCount)
        // Begin trailing gap branch
        {
            // Bind identity transform set
            commandList.SetGraphicsResourceSet(0, identitySet);
            // Draw trailing identity vertices
            commandList.Draw(totalVertexCount - cursor, 1, cursor, 0);
        // End trailing gap branch
        }
    // End DrawVertexRanges method
    }

    /// <summary>Returns cached render targets for a resolution, evicting the least recently used size.</summary>
    // Declare render target cache
    private static RasterTargets GetOrCreateTargets(GraphicsDevice gd, int width, int height)
    // Begin GetOrCreateTargets method
    {
        // Build target cache key
        (int Width, int Height) key = (width, height);
        // Look up cached render targets
        if (sharedTargets.TryGetValue(key, out RasterTargets? cached))
        // Begin cache hit block
        {
            // Refresh target use stamp
            cached.LastUseSerial = ++targetUseSerial;
            // Reuse cached render targets
            return cached;
        // End cache hit block
        }

        // Check target cache full
        if (sharedTargets.Count >= MaxCachedTargetSizes)
        // Begin cache eviction block
        {
            // Declare oldest cache entry
            KeyValuePair<(int Width, int Height), RasterTargets> oldest =
                // Find least recently used targets
                sharedTargets.MinBy(pair => pair.Value.LastUseSerial);
            // Remove oldest cache entry
            sharedTargets.Remove(oldest.Key);
            // Dispose evicted render targets
            oldest.Value.Dispose();
            // Log target cache eviction
            Stage($"Evict Vulkan raster target cache: {oldest.Key.Width}x{oldest.Key.Height}");
        // End cache eviction block
        }

        // Get Veldrid resource factory
        ResourceFactory factory = gd.ResourceFactory;
        // Create color render target
        Texture color = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        // Create depth stencil target
        Texture depth = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));
        // Create staging readback texture
        Texture staging = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        // Build new target record
        RasterTargets created = new()
        // Begin target initializer block
        {
            // Store target width
            Width = width,
            // Store target height
            Height = height,
            // Store color texture
            ColorTexture = color,
            // Store depth texture
            DepthTexture = depth,
            // Store staging texture
            StagingTexture = staging,
            // Create target framebuffer
            Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color)),
            // Create target command list
            CommandList = factory.CreateCommandList(),
            // Create target GPU fence
            Fence = factory.CreateFence(false),
            // Stamp target use serial
            LastUseSerial = ++targetUseSerial
        // End target initializer block
        };
        // Cache new render targets
        sharedTargets.Add(key, created);
        // Log target cache insert
        Stage($"Cache Vulkan raster targets: {width}x{height} ({sharedTargets.Count}/{MaxCachedTargetSizes})");
        // Return new render targets
        return created;
    // End GetOrCreateTargets method
    }

    /// <summary>Returns the shared Vulkan graphics device, creating it after a child-process preflight.</summary>
    // Declare shared device accessor
    private static GraphicsDevice GetOrCreateSharedDevice()
    // Begin GetOrCreateSharedDevice method
    {
        // Lock device creation guard
        lock (DeviceSync)
        // Begin device lock block
        {
            // Check shared device exists
            if (sharedGraphicsDevice != null)
            // Begin device reuse block
            {
                // Log device reuse
                Stage("Reuse shared Veldrid Vulkan raster GraphicsDevice");
                // Return shared graphics device
                return sharedGraphicsDevice;
            // End device reuse block
            }

            // Check preflight still pending
            if (!preflightCompleted)
            // Begin preflight block
            {
                // Log preflight start
                Stage("Preflight Veldrid Vulkan raster GraphicsDevice in child process");
                // Verify Vulkan in child process
                VeldridVulkanDevicePreflight.VerifyInChildProcess(Stage);
                // Mark preflight completed
                preflightCompleted = true;
            // End preflight block
            }

            // Log device creation
            Stage("Create shared Veldrid Vulkan raster GraphicsDevice");
            // Create Vulkan graphics device
            GraphicsDevice gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            // Begin device options block
            {
                // Set Vulkan debug flag
                Debug = IsVulkanDebugEnabled(),
                // Use Vulkan-native clip-space Y and flip Y explicitly in the vertex shader.
                // This avoids depending on backend-specific viewport normalization while
                // keeping the projection math easy to audit.
                // Use Vulkan clip-space Y
                PreferStandardClipSpaceYDirection = false,
                // Use zero-to-one depth
                PreferDepthRangeZeroToOne = true,
                // Disable vertical sync wait
                SyncToVerticalBlank = false
            // End device options block
            });

            // Verify Vulkan backend selected
            if (gd.BackendType != GraphicsBackend.Vulkan)
            // Begin backend check block
            {
                // Dispose wrong-backend device
                gd.Dispose();
                // Reject non-Vulkan device
                throw new InvalidOperationException("Veldrid did not create a Vulkan graphics device for the raster backend.");
            // End backend check block
            }

            // Cache shared graphics device
            sharedGraphicsDevice = gd;
            // Return new graphics device
            return gd;
        // End device lock block
        }
    // End GetOrCreateSharedDevice method
    }

    /// <summary>Returns the shared raster layout, shaders, and opaque and transparent pipelines.</summary>
    // Declare shared resources accessor
    private static SharedRasterResources GetOrCreateSharedRasterResources(GraphicsDevice gd, OutputDescription outputDescription)
    // Begin GetOrCreateSharedRasterResources method
    {
        // Lock resource creation guard
        lock (DeviceSync)
        // Begin resource lock block
        {
            // Check shared resources exist
            if (sharedRasterResources != null)
                // Reuse shared raster resources
                return sharedRasterResources;

            // Get Veldrid resource factory
            ResourceFactory factory = gd.ResourceFactory;
            // Log resource layout creation
            Stage("Create Vulkan raster resource layout");
            // Create shader resource layout
            ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                // Bind camera uniform slot
                new ResourceLayoutElementDescription("CameraConstants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                // Bind transform uniform slot
                new ResourceLayoutElementDescription("TransformConstants", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                // Bind light buffer slot
                new ResourceLayoutElementDescription("LightBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                // Bind material buffer slot
                new ResourceLayoutElementDescription("MaterialBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                // Bind atlas texture slot
                new ResourceLayoutElementDescription("AtlasTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                // Bind atlas sampler slot
                new ResourceLayoutElementDescription("AtlasSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Begin pipeline creation guard
            try
            // Begin pipeline creation block
            {
                // Compile shared vertex shader
                Shader vertexShader = CreateRasterVertexShader(factory);
                // Compile shaded fragment shader
                Shader fragmentShader = CreateRasterFragmentShader(factory, null);
                // Compile wireframe fragment shader
                Shader wireframeFragmentShader = CreateRasterFragmentShader(factory, "WIREFRAME_SOLID");
                // Compile dashed fragment shader
                Shader dashedFragmentShader = CreateRasterFragmentShader(factory, "WIREFRAME_DASHED");
                // Collect shaders for disposal
                Shader[] shaders = [vertexShader, fragmentShader, wireframeFragmentShader, dashedFragmentShader];
                // Begin shader use guard
                try
                // Begin shader use block
                {
                    // Declare vertex input layout
                    VertexLayoutDescription vertexLayout = new(
                        // Map position vertex element
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        // Map normal vertex element
                        new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        // Map UV vertex element
                        new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        // Map material index element
                        new VertexElementDescription("MaterialIndex", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1));

                    // Declare rasterizer state
                    RasterizerStateDescription rasterizerState = new(
                        // Disable face culling
                        cullMode: FaceCullMode.None,
                        // Use solid fill mode
                        fillMode: PolygonFillMode.Solid,
                        // Set clockwise front faces
                        frontFace: FrontFace.Clockwise,
                        // Enable depth clipping
                        depthClipEnabled: true,
                        // Disable scissor test
                        scissorTestEnabled: false);

                    // Combine layout with shaders
                    ShaderSetDescription shaderSet = new(new[] { vertexLayout }, [vertexShader, fragmentShader]);
                    // Combine layout with wireframe shaders
                    ShaderSetDescription wireframeShaderSet = new(new[] { vertexLayout }, [vertexShader, wireframeFragmentShader]);
                    // Combine layout with dashed shaders
                    ShaderSetDescription dashedShaderSet = new(new[] { vertexLayout }, [vertexShader, dashedFragmentShader]);
                    // Clone rasterizer state for wireframe
                    RasterizerStateDescription wireframeRasterizerState = rasterizerState;
                    // Switch clone to wireframe fill
                    wireframeRasterizerState.FillMode = PolygonFillMode.Wireframe;

                    // Declare opaque pipeline description
                    GraphicsPipelineDescription opaquePipelineDescription = new()
                    // Begin opaque pipeline initializer block
                    {
                        // Disable blending for opaque pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin opaque depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable opaque depth testing
                            depthTestEnabled: true,
                            // Enable opaque depth writes
                            depthWriteEnabled: true,
                            // Use less depth comparison
                            comparisonKind: ComparisonKind.Less),
                        // Assign shared rasterizer state
                        RasterizerState = rasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign raster shader set
                        ShaderSet = shaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End opaque pipeline initializer
                    };

                    // Declare transparent pipeline description
                    GraphicsPipelineDescription transparentPipelineDescription = new()
                    // Begin transparent pipeline initializer block
                    {
                        // Enable alpha blending for transparency
                        BlendState = BlendStateDescription.SingleAlphaBlend,
                        // Begin transparent depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable transparent depth testing
                            depthTestEnabled: true,
                            // Disable transparent depth writes
                            depthWriteEnabled: false,
                            // Use less depth comparison
                            comparisonKind: ComparisonKind.Less),
                        // Assign shared rasterizer state
                        RasterizerState = rasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign raster shader set
                        ShaderSet = shaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End transparent pipeline initializer
                    };

                    // Declare depth prepass pipeline description
                    GraphicsPipelineDescription depthPrepassDescription = new()
                    // Begin prepass pipeline initializer block
                    {
                        // Disable blending for prepass pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin prepass depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable prepass depth testing
                            depthTestEnabled: true,
                            // Enable prepass depth writes
                            depthWriteEnabled: true,
                            // Use less depth comparison
                            comparisonKind: ComparisonKind.Less),
                        // Assign shared rasterizer state
                        RasterizerState = rasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign wireframe shader set
                        ShaderSet = wireframeShaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End prepass pipeline initializer
                    };

                    // Declare wireframe pipeline description
                    GraphicsPipelineDescription wireframePipelineDescription = new()
                    // Begin wireframe pipeline initializer block
                    {
                        // Disable blending for wireframe pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin wireframe depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable wireframe depth testing
                            depthTestEnabled: true,
                            // Enable wireframe depth writes
                            depthWriteEnabled: true,
                            // Use less-equal depth comparison
                            comparisonKind: ComparisonKind.LessEqual),
                        // Assign wireframe rasterizer state
                        RasterizerState = wireframeRasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign wireframe shader set
                        ShaderSet = wireframeShaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End wireframe pipeline initializer
                    };

                    // Declare dashed pipeline description
                    GraphicsPipelineDescription dashedPipelineDescription = new()
                    // Begin dashed pipeline initializer block
                    {
                        // Disable blending for dashed pass
                        BlendState = BlendStateDescription.SingleDisabled,
                        // Begin dashed depth state setup
                        DepthStencilState = new DepthStencilStateDescription(
                            // Enable dashed depth testing
                            depthTestEnabled: true,
                            // Disable dashed depth writes
                            depthWriteEnabled: false,
                            // Use greater depth comparison
                            comparisonKind: ComparisonKind.Greater),
                        // Assign wireframe rasterizer state
                        RasterizerState = wireframeRasterizerState,
                        // Render triangles as list
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        // Bind shared resource layout
                        ResourceLayouts = new[] { layout },
                        // Assign dashed shader set
                        ShaderSet = dashedShaderSet,
                        // Assign pipeline output description
                        Outputs = outputDescription
                    // End dashed pipeline initializer
                    };

                    // Create opaque graphics pipeline
                    Pipeline opaquePipeline = factory.CreateGraphicsPipeline(opaquePipelineDescription);
                    // Declare transparent pipeline variable
                    Pipeline transparentPipeline;
                    // Guard transparent pipeline creation
                    try
                    // Begin pipeline creation guard
                    {
                        // Create transparent graphics pipeline
                        transparentPipeline = factory.CreateGraphicsPipeline(transparentPipelineDescription);
                    // End pipeline creation guard
                    }
                    // Handle transparent pipeline failure
                    catch
                    // Begin pipeline cleanup handler
                    {
                        // Dispose opaque pipeline on failure
                        try { opaquePipeline.Dispose(); } catch { }
                        // Rethrow pipeline creation error
                        throw;
                    // End pipeline cleanup handler
                    }

                    // Declare wireframe pipeline variables
                    Pipeline depthPrepassPipeline;
                    // Declare solid wireframe pipeline
                    Pipeline wireframePipeline;
                    // Declare dashed wireframe pipeline
                    Pipeline wireframeDashedPipeline;
                    // Guard wireframe pipeline creation
                    try
                    // Begin wireframe creation guard
                    {
                        // Create depth prepass pipeline
                        depthPrepassPipeline = factory.CreateGraphicsPipeline(depthPrepassDescription);
                        // Create solid wireframe pipeline
                        wireframePipeline = factory.CreateGraphicsPipeline(wireframePipelineDescription);
                        // Create dashed wireframe pipeline
                        wireframeDashedPipeline = factory.CreateGraphicsPipeline(dashedPipelineDescription);
                    // End wireframe creation guard
                    }
                    // Handle wireframe creation failure
                    catch
                    // Begin wireframe cleanup handler
                    {
                        // Dispose opaque pipeline on failure
                        try { opaquePipeline.Dispose(); } catch { }
                        // Dispose transparent pipeline on failure
                        try { transparentPipeline.Dispose(); } catch { }
                        // Rethrow pipeline creation error
                        throw;
                    // End wireframe cleanup handler
                    }

                    // Assign new shared raster resources
                    sharedRasterResources = new SharedRasterResources
                    // Begin shared resources block
                    {
                        // Store shared resource layout
                        Layout = layout,
                        // Store raster shaders
                        Shaders = shaders,
                        // Store opaque pipeline
                        OpaquePipeline = opaquePipeline,
                        // Store transparent pipeline
                        TransparentPipeline = transparentPipeline,
                        // Store depth prepass pipeline
                        DepthPrepassPipeline = depthPrepassPipeline,
                        // Store solid wireframe pipeline
                        WireframePipeline = wireframePipeline,
                        // Store dashed wireframe pipeline
                        WireframeDashedPipeline = wireframeDashedPipeline
                    // End shared resources initializer
                    };
                    // Log pipeline creation stage
                    Stage("Vulkan raster graphics pipelines created: CPU-raster parity opaque z-buffer path");
                    // Return shared raster resources
                    return sharedRasterResources;
                // End shader setup try block
                }
                // Handle shader setup failure
                catch
                // Begin shader cleanup handler
                {
                    // Loop over created shaders
                    foreach (Shader shader in shaders)
                    // Begin shader disposal loop
                    {
                        // Dispose shader ignoring errors
                        try { shader.Dispose(); } catch { }
                    // End shader disposal loop
                    }
                    // Rethrow shader setup error
                    throw;
                // End shader cleanup handler
                }
            // End resource try block
            }
            // Handle resource creation failure
            catch
            // Begin layout cleanup handler
            {
                // Dispose resource layout
                layout.Dispose();
                // Rethrow resource creation error
                throw;
            // End layout cleanup handler
            }
        // End device lock block
        }
    // End GetOrCreateSharedRasterResources method
    }

    /// <summary>Creates the shared raster vertex shader from the embedded source.</summary>
    // Declare raster vertex shader factory
    private static Shader CreateRasterVertexShader(ResourceFactory factory)
    // Begin CreateRasterVertexShader method
    {
        // Begin vertex shader description
        ShaderDescription vertex = new(
            // Select vertex shader stage
            ShaderStages.Vertex,
            // Encode vertex source as UTF8
            Encoding.UTF8.GetBytes(VertexShaderSource),
            // Set vertex entry point
            "main");
        // Guard vertex shader creation
        try
        // Begin vertex creation guard
        {
            // Log vertex compile stage
            Stage("Compile/create Vulkan raster vertex shader");
            // Create vertex shader from SPIRV
            return factory.CreateFromSpirv(vertex);
        // End vertex creation guard
        }
        // Handle vertex compile failure
        catch (Exception ex)
        // Begin vertex failure handler
        {
            // Log vertex compile failure
            Stage("Vulkan raster vertex shader compile/create failed");
            // Log vertex exception details
            Stage(ex.ToString());
            // Throw vertex compilation failure
            throw new InvalidOperationException("Vulkan raster vertex shader compilation failed. See " + StageLogPath + ".", ex);
        // End vertex failure handler
        }
    // End CreateRasterVertexShader method
    }

    /// <summary>Creates a raster fragment shader variant, optionally with a compile define.</summary>
    // Declare raster fragment shader factory
    private static Shader CreateRasterFragmentShader(ResourceFactory factory, string? define)
    // Begin CreateRasterFragmentShader method
    {
        // Inject compile define after version
        string source = WithFragmentDefine(FragmentShaderSource, define);
        // Begin fragment shader description
        ShaderDescription fragment = new(
            // Select fragment shader stage
            ShaderStages.Fragment,
            // Encode fragment source as UTF8
            Encoding.UTF8.GetBytes(source),
            // Set fragment entry point
            "main");
        // Guard fragment shader creation
        try
        // Begin fragment creation guard
        {
            // Log fragment compile stage
            Stage("Compile/create Vulkan raster fragment shader " + (define ?? "default"));
            // Create fragment shader from SPIRV
            return factory.CreateFromSpirv(fragment);
        // End fragment creation guard
        }
        // Handle fragment compile failure
        catch (Exception ex)
        // Begin fragment failure handler
        {
            // Log fragment compile failure
            Stage("Vulkan raster fragment shader compile/create failed");
            // Log fragment exception details
            Stage(ex.ToString());
            // Guard debug fragment dump
            try
            // Begin fragment dump guard
            {
                // Dump failed fragment shader source
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-failed.frag"), source);
            // End fragment dump guard
            }
            // Ignore fragment dump errors
            catch { }
            // Throw fragment compilation failure
            throw new InvalidOperationException("Vulkan raster fragment shader compilation failed. See " + StageLogPath + " and %TEMP%\\LightingShowcase-vulkan-raster-failed.frag.", ex);
        // End fragment failure handler
        }
    // End CreateRasterFragmentShader method
    }

    /// <summary>Inserts a compile define after the GLSL version line.</summary>
    // Declare shader define injector
    private static string WithFragmentDefine(string source, string? define)
    // Begin WithFragmentDefine method
    {
        // Return source unchanged without define
        if (string.IsNullOrEmpty(define))
            // Pass source through unchanged
            return source;
        // Scan source lines for version directive
        int lineStart = 0;
        // Walk lines until version found
        while (lineStart < source.Length)
        // Begin version search loop
        {
            // Find end of current line
            int newline = source.IndexOf('\n', lineStart);
            // Stop scanning at missing newline
            if (newline < 0)
                // Abort search at end of source
                break;
            // Read current source line
            string line = source.Substring(lineStart, newline - lineStart);
            // Insert after version directive
            if (line.TrimStart().StartsWith("#version", StringComparison.Ordinal))
                // Inject define after version line
                return source.Insert(newline + 1, "#define " + define + " 1\n");
            // Advance past current line
            lineStart = newline + 1;
        // End version search loop
        }
        // Fail loudly when version missing
        throw new InvalidOperationException("Cannot inject '" + define + "': no #version line in fragment shader source.");
    // End WithFragmentDefine method
    }

    /// <summary>Reads staging pixels into a render image, forcing alpha to opaque.</summary>
    // Declare staging readback helper
    private static unsafe RenderImage ReadBackImage(GraphicsDevice gd, Texture stagingTexture, int width, int height)
    // Begin readback block
    {
        // Allocate pixel output buffer
        uint[] pixels = new uint[checked(width * height)];
        // Map staging texture for reading
        MappedResource mapped = gd.Map(stagingTexture, MapMode.Read);
        // Begin mapped read guard
        try
        // Begin pinned copy block
        {
            // Pin pixel buffer destination
            fixed (uint* destinationBase = pixels)
            // Begin row copy scope
            {
                // Compute bytes per pixel row
                nuint rowBytes = checked((nuint)(width * sizeof(uint)));
                // Copy each image row
                for (int y = 0; y < height; y++)
                // Begin per-row copy block
                {
                    // Locate pitched source row pointer
                    byte* source = (byte*)mapped.Data + checked((nint)(y * mapped.RowPitch));
                    // Locate destination row pointer
                    uint* destination = destinationBase + y * width;
                    // Copy one row of pixels
                    Buffer.MemoryCopy(source, destination, rowBytes, rowBytes);
                    // Force every pixel fully opaque
                    for (int x = 0; x < width; x++)
                        // Set alpha channel to opaque
                        destination[x] |= 0xff000000u;
                // End per-row copy block
                }
            // End row copy scope
            }
        // End pinned copy block
        }
        // Ensure staging texture unmapped
        finally
        // Begin unmap cleanup block
        {
            // Release staging texture mapping
            gd.Unmap(stagingTexture);
        // End unmap cleanup block
        }
        // Return completed render image
        return new RenderImage(width, height, pixels);
    // End readback block
    }

    // Begin embedded vertex shader string
    private const string VertexShaderSource = @"
#version 450

// Declare vertex position input
layout(location = 0) in vec3 Position;
// Declare vertex normal input
layout(location = 1) in vec3 Normal;
// Declare vertex UV input
layout(location = 2) in vec2 Uv;
// Declare material index input
layout(location = 3) in float MaterialIndex;

// Output world position to fragment
layout(location = 0) out vec3 fsWorldPos;
// Output world normal to fragment
layout(location = 1) out vec3 fsNormal;
// Output UV to fragment stage
layout(location = 2) out vec2 fsUv;
// Output flat material index
layout(location = 3) flat out int fsMaterialIndex;

// Declare camera uniform block
layout(std140, set = 0, binding = 0) uniform CameraConstants
// Begin camera uniform block
{
    // Store camera world position
    vec4 CameraPosition;
    // Store camera forward vector
    vec4 CameraForward;
    // Store camera right vector
    vec4 CameraRight;
    // Store camera up vector
    vec4 CameraUp;
    // Store projection parameters
    vec4 Projection;
    // Store light and material counts
    vec4 Counts;
// End camera uniform block
} Camera;

// Declare transform uniform block
layout(std140, set = 0, binding = 1) uniform TransformConstants
// Begin transform uniform block
{
    // Store pivot and enable flag
    vec4 PivotEnabled;
    // Store translation offset
    vec4 Translation;
    // Store Euler rotation angles
    vec4 Rotation;
    // Store scale factors
    vec4 Scale;
// End transform uniform block
} Transform;

// Declare scale sanitizer function
float sanitizeScale(float value)
// Begin scale sanitizer block
{
    // Replace near zero scale
    return abs(value) < 0.00000001 ? 1.0 : value;
// End scale sanitizer block
}

// Declare Euler rotation helper
vec3 rotateEuler(vec3 value, vec3 rotation)
// Begin Euler rotation block
{
    // Compute X axis rotation factors
    float cx = cos(rotation.x); float sx = sin(rotation.x);
    // Apply rotation about X
    value = vec3(value.x, value.y * cx - value.z * sx, value.y * sx + value.z * cx);
    // Compute Y axis rotation factors
    float cy = cos(rotation.y); float sy = sin(rotation.y);
    // Apply rotation about Y
    value = vec3(value.x * cy + value.z * sy, value.y, -value.x * sy + value.z * cy);
    // Compute Z axis rotation factors
    float cz = cos(rotation.z); float sz = sin(rotation.z);
    // Apply rotation about Z
    return vec3(value.x * cz - value.y * sz, value.x * sz + value.y * cz, value.z);
// End Euler rotation block
}

// Declare vertex shader entry
void main()
// Begin vertex main block
{
    // Initialize world position from input
    vec3 worldPosition = Position;
    // Initialize world normal from input
    vec3 worldNormal = Normal;
    // Apply pivot transform when enabled
    if (Transform.PivotEnabled.w > 0.5)
    // Begin pivot transform block
    {
        // Start sanitized scale vector
        vec3 safeScale = vec3(
            // Sanitize X scale component
            sanitizeScale(Transform.Scale.x),
            // Sanitize Y scale component
            sanitizeScale(Transform.Scale.y),
            // Sanitize Z scale component
            sanitizeScale(Transform.Scale.z));
        // Scale position around pivot point
        vec3 local = (Position - Transform.PivotEnabled.xyz) * safeScale;
        // Rotate and translate pivot position
        worldPosition = Transform.PivotEnabled.xyz + Transform.Translation.xyz + rotateEuler(local, Transform.Rotation.xyz);
        // Rotate normal with inverse scale
        worldNormal = normalize(rotateEuler(Normal / safeScale, Transform.Rotation.xyz));
    // End pivot transform block
    }

    // Compute position relative to camera
    vec3 rel = worldPosition - Camera.CameraPosition.xyz;
    // Project onto camera right axis
    float vx = dot(rel, Camera.CameraRight.xyz);
    // Project onto camera up axis
    float vy = dot(rel, Camera.CameraUp.xyz);
    // Project onto camera forward axis
    float vz = dot(rel, Camera.CameraForward.xyz);

    // Clamp aspect ratio minimum
    float aspect = max(Camera.Projection.x, 0.0001);
    // Clamp field of view factor
    float tanHalfFov = max(Camera.Projection.y, 0.0001);
    // Clamp near plane minimum
    float nearPlane = max(Camera.Projection.z, 0.0001);
    // Ensure far plane beyond near
    float farPlane = max(Camera.Projection.w, nearPlane + 1.0);
    // Use forward depth as w
    float z = vz;
    // Normalize depth between planes
    float ndcDepth = (z - nearPlane) / (farPlane - nearPlane);

    // Start clip position constructor
    gl_Position = vec4(
        // Compute horizontal clip coordinate
        -vx / (aspect * tanHalfFov),
         // Compute vertical clip coordinate
         vy / tanHalfFov,
         // Scale depth by view distance
         ndcDepth * z,
         // Store view depth as w
         z);
    // Flip Y for Vulkan coordinates
    gl_Position.y = -gl_Position.y;

    // Pass world position onward
    fsWorldPos = worldPosition;
    // Pass world normal onward
    fsNormal = worldNormal;
    // Pass texture coordinates onward
    fsUv = Uv;
    // Pass rounded material index onward
    fsMaterialIndex = int(MaterialIndex + 0.5);
// End vertex main block
}
// End embedded vertex shader string
";

    // Begin embedded fragment shader string
    private const string FragmentShaderSource = @"
#version 450

// Define PI constant for BRDF
const float PI = 3.14159265358979323846;

// Declare raster light structure
struct RasterLight
// Begin light structure block
{
    // Store light position and kind
    vec4 PositionKind;
    // Store light direction and range
    vec4 DirectionRange;
    // Store light color and intensity
    vec4 ColorIntensity;
    // Store cone and enable flags
    vec4 ConeEnabled;
// End light structure block
};

// Declare raster material structure
struct RasterMaterial
// Begin material structure block
{
    // Store base color and alpha
    vec4 BaseColorAlpha;
    // Store emissive factor
    vec4 EmissiveFactor;
    // Store metallic roughness factors
    vec4 PbrFactors;
    // Store alpha test flags
    vec4 AlphaFlags;
    // Store clearcoat and optical factors
    vec4 OpticalFactors;
    // Store attenuation color and distance
    vec4 AttenuationColorDistance;
    // Store primary texture presence flags
    vec4 TextureFlags;
    // Store extra texture presence flags
    vec4 TextureFlags2;

    // Store base atlas transform
    vec4 BaseAtlasTransform;
    // Store base texture addressing
    vec4 BaseTextureAddress;
    // Store base UV transform
    vec4 BaseTextureTransform;

    // Store metallic roughness atlas transform
    vec4 MrAtlasTransform;
    // Store metallic roughness addressing
    vec4 MrTextureAddress;
    // Store metallic roughness UV transform
    vec4 MrTextureTransform;

    // Store normal atlas transform
    vec4 NormalAtlasTransform;
    // Store normal texture addressing
    vec4 NormalTextureAddress;
    // Store normal UV transform
    vec4 NormalTextureTransform;

    // Store emissive atlas transform
    vec4 EmissiveAtlasTransform;
    // Store emissive texture addressing
    vec4 EmissiveTextureAddress;
    // Store emissive UV transform
    vec4 EmissiveTextureTransform;

    // Store occlusion atlas transform
    vec4 OcclusionAtlasTransform;
    // Store occlusion texture addressing
    vec4 OcclusionTextureAddress;
    // Store occlusion UV transform
    vec4 OcclusionTextureTransform;

    // Store transmission atlas transform
    vec4 TransmissionAtlasTransform;
    // Store transmission texture addressing
    vec4 TransmissionTextureAddress;
    // Store transmission UV transform
    vec4 TransmissionTextureTransform;
// End material structure block
};

// Receive interpolated world position
layout(location = 0) in vec3 fsWorldPos;
// Receive interpolated world normal
layout(location = 1) in vec3 fsNormal;
// Receive interpolated texture coordinates
layout(location = 2) in vec2 fsUv;
// Receive flat material index
layout(location = 3) flat in int fsMaterialIndex;

// Declare fragment color output
layout(location = 0) out vec4 outColor;

// Declare fragment camera block
layout(std140, set = 0, binding = 0) uniform CameraConstants
// Begin fragment camera block
{
    // Store camera world position
    vec4 CameraPosition;
    // Store camera forward vector
    vec4 CameraForward;
    // Store camera right vector
    vec4 CameraRight;
    // Store camera up vector
    vec4 CameraUp;
    // Store projection parameters
    vec4 Projection;
    // Store light and material counts
    vec4 Counts;
// End fragment camera block
} Camera;

// Declare light storage buffer
layout(std430, set = 0, binding = 2) readonly buffer LightBuffer
// Begin light buffer block
{
    // Store variable light array
    RasterLight Lights[];
// End light buffer block
};

// Declare material storage buffer
layout(std430, set = 0, binding = 3) readonly buffer MaterialBuffer
// Begin material buffer block
{
    // Store variable material array
    RasterMaterial Materials[];
// End material buffer block
};

// Declare atlas texture array
layout(set = 0, binding = 4) uniform texture2DArray AtlasTexture;
// Declare atlas texture sampler
layout(set = 0, binding = 5) uniform sampler AtlasSampler;

// Declare texture addressing helper
float addressTexture(float v, float mode)
// Begin addressing helper block
{
    // Detect clamp addressing mode
    if (mode > 0.5 && mode < 1.5)
        // Clamp coordinate into range
        return clamp(v, 0.0, 1.0);
    // Detect mirrored repeat mode
    if (mode > 1.5)
    // Begin mirror addressing block
    {
        // Find integer repeat count
        float floorValue = floor(v);
        // Find fractional local coordinate
        float local = fract(v);
        // Mirror alternate repeats
        return mod(floorValue, 2.0) == 0.0 ? local : 1.0 - local;
    // End mirror addressing block
    }
    // Pass through in range coordinates
    if (v >= 0.0 && v <= 1.0)
        // Return already valid coordinate
        return v;
    // Wrap coordinate with fraction
    return fract(v);
// End addressing helper block
}

// Declare UV transform helper
vec2 addressUv(vec2 uv, vec4 textureAddress, vec4 textureTransform)
// Begin UV transform block
{
    // Scale UV by transform factor
    vec2 transformed = uv * textureTransform.zw;
    // Read texture rotation angle
    float rotation = textureAddress.z;
    // Skip rotation when nearly zero
    if (abs(rotation) > 0.000001)
    // Begin UV rotation block
    {
        // Compute rotation cosine factor
        float c = cos(rotation);
        // Compute rotation sine factor
        float s = sin(rotation);
        // Rotate scaled UV coordinates
        transformed = vec2(transformed.x * c - transformed.y * s, transformed.x * s + transformed.y * c);
    // End UV rotation block
    }
    // Add UV offset translation
    transformed += textureTransform.xy;
    // Address both transformed axes
    return vec2(addressTexture(transformed.x, textureAddress.x), addressTexture(transformed.y, textureAddress.y));
// End UV transform block
}

// Declare atlas sampling helper
vec4 sampleAtlasTexture(vec2 uv, vec4 atlasTransform, vec4 textureAddress, vec4 textureTransform)
// Begin atlas sampling block
{
    // Resolve addressed texture coordinates
    vec2 addressed = addressUv(uv, textureAddress, textureTransform);
    // Map coordinates into atlas region
    vec2 atlasUv = atlasTransform.xy + addressed * atlasTransform.zw;
    // Sample atlas array layer
    return texture(sampler2DArray(AtlasTexture, AtlasSampler), vec3(atlasUv, textureAddress.w));
// End atlas sampling block
}

// Declare sRGB to linear helper
float srgbChannelToLinear(float value)
// Begin sRGB conversion block
{
    // Convert one channel to linear
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
// End sRGB conversion block
}

// Declare vector sRGB converter
vec3 srgbToLinear(vec3 value)
// Begin vector sRGB block
{
    // Start linear color constructor
    return vec3(
        // Convert red channel value
        srgbChannelToLinear(value.r),
        // Convert green channel value
        srgbChannelToLinear(value.g),
        // Convert blue channel value
        srgbChannelToLinear(value.b));
// End vector sRGB block
}

// Declare linear to sRGB helper
float linearChannelToSrgb(float value)
// Begin linear conversion block
{
    // Clamp negative linear values
    value = max(value, 0.0);
    // Convert one channel to sRGB
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
// End linear conversion block
}

// Declare vector linear converter
vec3 linearToSrgb(vec3 value)
// Begin vector linear block
{
    // Start sRGB color constructor
    return vec3(
        // Convert red channel value
        linearChannelToSrgb(value.r),
        // Convert green channel value
        linearChannelToSrgb(value.g),
        // Convert blue channel value
        linearChannelToSrgb(value.b));
// End vector linear block
}

// Declare neutral tone mapping helper
vec3 pbrNeutralToneMap(vec3 color)
// Begin tone mapping block
{
    // Set highlight compression start
    const float startCompression = 0.76;
    // Set highlight desaturation amount
    const float desaturation = 0.15;
    // Find minimum color channel
    float x = min(color.r, min(color.g, color.b));
    // Compute shadow toe offset
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    // Subtract toe offset value
    color -= offset;

    // Find peak color channel
    float peak = max(color.r, max(color.g, color.b));
    // Skip compression below threshold
    if (peak < startCompression)
        // Return dark colors directly
        return max(color, vec3(0.0));

    // Compute remaining headroom range
    float d = 1.0 - startCompression;
    // Compress peak toward white
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    // Scale color by compressed peak
    color *= newPeak / max(peak, 0.000001);
    // Compute desaturation blend factor
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    // Return desaturated safe color
    return max(mix(color, newPeak * vec3(1.0), g), vec3(0.0));
// End tone mapping block
}

// Declare GGX distribution helper
float distributionGgx(float nDotH, float alphaRoughness)
// Begin GGX distribution block
{
    // Square roughness to alpha
    float a2 = alphaRoughness * alphaRoughness;
    // Compute denominator shape term
    float f = nDotH * nDotH * (a2 - 1.0) + 1.0;
    // Return normalized facet distribution
    return a2 / max(PI * f * f, 0.000001);
// End GGX distribution block
}

// Declare Smith visibility helper
float visibilitySmithGgxCorrelated(float nDotV, float nDotL, float alphaRoughness)
// Begin Smith visibility block
{
    // Square roughness to alpha
    float a2 = alphaRoughness * alphaRoughness;
    // Compute view masking term
    float gv = nDotL * sqrt(max(nDotV * nDotV * (1.0 - a2) + a2, 0.0));
    // Compute light shadowing term
    float gl = nDotV * sqrt(max(nDotL * nDotL * (1.0 - a2) + a2, 0.0));
    // Return correlated visibility factor
    return 0.5 / max(gv + gl, 0.000001);
// End Smith visibility block
}

// Declare Schlick fresnel helper
vec3 fresnelSchlick(float vDotH, vec3 f0)
// Begin fresnel helper block
{
    // Compute fifth power falloff
    float f = pow(1.0 - clamp(vDotH, 0.0, 1.0), 5.0);
    // Blend base reflectance upward
    return f0 + (1.0 - f0) * f;
// End fresnel helper block
}

// Declare cotangent frame helper
mat3 cotangentFrame(vec3 normal, vec3 position, vec2 uv)
// Begin cotangent frame block
{
    // Compute X position derivative
    vec3 dp1 = dFdx(position);
    // Compute Y position derivative
    vec3 dp2 = dFdy(position);
    // Compute X UV derivative
    vec2 duv1 = dFdx(uv);
    // Compute Y UV derivative
    vec2 duv2 = dFdy(uv);
    // Build first perpendicular vector
    vec3 dp2Perp = cross(dp2, normal);
    // Build second perpendicular vector
    vec3 dp1Perp = cross(normal, dp1);
    // Accumulate tangent direction
    vec3 tangent = dp2Perp * duv1.x + dp1Perp * duv2.x;
    // Accumulate bitangent direction
    vec3 bitangent = dp2Perp * duv1.y + dp1Perp * duv2.y;
    // Measure worst frame length
    float maxLength = max(dot(tangent, tangent), dot(bitangent, bitangent));
    // Handle degenerate UV mapping
    if (maxLength < 0.00000001)
    // Begin degenerate fallback block
    {
        // Pick axis away from normal
        vec3 axis = abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
        // Build orthonormal tangent fallback
        tangent = normalize(cross(axis, normal));
        // Build orthonormal bitangent fallback
        bitangent = cross(normal, tangent);
        // Return fallback tangent frame
        return mat3(tangent, bitangent, normal);
    // End degenerate fallback block
    }
    // Compute inverse frame length
    float inverseLength = inversesqrt(maxLength);
    // Return normalized tangent frame
    return mat3(tangent * inverseLength, bitangent * inverseLength, normal);
// End cotangent frame block
}

// Declare studio environment helper
vec3 studioEnvironment(vec3 direction)
// Begin environment lighting block
{
    // Normalize sample direction vector
    direction = normalize(direction);
    // Blend ground to sky factor
    float skyAmount = smoothstep(-0.25, 0.65, direction.y);
    // Define ground bounce color
    vec3 ground = vec3(0.035, 0.040, 0.050);
    // Define sky ambient color
    vec3 sky = vec3(0.32, 0.39, 0.49);
    // Mix ground and sky radiance
    vec3 radiance = mix(ground, sky, skyAmount);

    // Define key light direction
    vec3 keyDirection = normalize(vec3(-0.55, 0.62, -0.56));
    // Define rim light direction
    vec3 rimDirection = normalize(vec3(0.78, 0.28, 0.56));
    // Compute key highlight lobe
    float key = pow(max(dot(direction, keyDirection), 0.0), 96.0);
    // Compute rim highlight lobe
    float rim = pow(max(dot(direction, rimDirection), 0.0), 180.0);
    // Add key light contribution
    radiance += vec3(5.2, 4.8, 4.2) * key;
    // Add blue rim highlight to environment radiance
    radiance += vec3(2.0, 2.7, 3.8) * rim;
    // Return studio environment radiance
    return radiance;
// End studioEnvironment function
}

// Declare diffuse environment lookup function
vec3 diffuseEnvironment(vec3 normal)
// Begin diffuseEnvironment function
{
    // Blend ground to sky by normal height
    float skyAmount = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    // Return ground sky gradient color
    return mix(vec3(0.055, 0.060, 0.070), vec3(0.42, 0.47, 0.54), skyAmount);
// End diffuseEnvironment function
}

// Declare fragment shader entry point
void main()
// Begin main function
{
    // Mark solid wireframe override block
#ifdef WIREFRAME_SOLID
    // Output flat wireframe line color
    outColor = vec4(0.92, 0.96, 1.0, 1.0);
    // Exit after wireframe output
    return;
#endif
    // Mark dashed wireframe override block
#ifdef WIREFRAME_DASHED
    // Discard dashed gap fragments
    if (mod(gl_FragCoord.x + gl_FragCoord.y * 2.0, 8.0) >= 4.0) discard;
    // Output flat hidden line color
    outColor = vec4(0.42, 0.52, 0.66, 1.0);
    // Exit after dashed output
    return;
#endif
    // Read debug visualization mode selector
    int debugMode = int(Camera.Counts.z + 0.5);
    // Fetch fragment material record
    RasterMaterial material = Materials[fsMaterialIndex];

    // Flag base color texture presence
    bool hasBase = material.TextureFlags.x > 0.5;
    // Flag metallic roughness texture presence
    bool hasMr = material.TextureFlags.y > 0.5;
    // Flag normal map presence
    bool hasNormal = material.TextureFlags.z > 0.5;
    // Flag emissive texture presence
    bool hasEmissive = material.TextureFlags.w > 0.5;
    // Flag occlusion texture presence
    bool hasOcclusion = material.TextureFlags2.x > 0.5;
    // Flag transmission texture presence
    bool hasTransmissionTexture = material.TextureFlags2.y > 0.5;

    // Sample base color texel when present
    vec4 baseTexel = hasBase
        // Fetch base texel from texture atlas
        ? sampleAtlasTexture(fsUv, material.BaseAtlasTransform, material.BaseTextureAddress, material.BaseTextureTransform)
        // Default base texel to white
        : vec4(1.0);
    // Compute linear base color
    vec3 baseColor = material.BaseColorAlpha.rgb * (hasBase ? srgbToLinear(baseTexel.rgb) : vec3(1.0));
    // Combine material and texture alpha
    float sourceAlpha = material.BaseColorAlpha.a * baseTexel.a;
    // Initialize coverage alpha
    float alpha = sourceAlpha;
    // Default sampled transmission to opaque
    float sampledTransmission = 1.0;
    // Clamp material transmission factor
    float transmission = clamp(material.AlphaFlags.w, 0.0, 1.0);
    // Test for transmission texture
    if (hasTransmissionTexture)
    // Begin transmission sampling block
    {
        // Sample transmission mask red channel
        sampledTransmission = sampleAtlasTexture(
            // Pass fragment UVs to sampler
            fsUv,
            // Pass transmission atlas transform
            material.TransmissionAtlasTransform,
            // Pass transmission address mode
            material.TransmissionTextureAddress,
            // Read mask value from transform result
            material.TransmissionTextureTransform).r;
        // Modulate transmission by sampled mask
        transmission *= sampledTransmission;
    // End transmission sampling block
    }

    // Clamp index of refraction
    float ior = clamp(material.OpticalFactors.x, 1.0, 2.333);
    // Clamp volume thickness nonnegative
    float thickness = max(material.OpticalFactors.y, 0.0);
    // Clamp clearcoat layer weight
    float clearcoat = clamp(material.OpticalFactors.z, 0.0, 1.0);
    // Test for shared clearcoat mask
    if (material.TextureFlags2.z > 0.5)
        // Reuse transmission mask for clearcoat
        clearcoat *= sampledTransmission;
    // Clamp clearcoat roughness
    float clearcoatRoughness = clamp(material.OpticalFactors.w, 0.045, 1.0);
    // Decode alpha blend mode
    int alphaMode = int(material.AlphaFlags.x + 0.5);
    // Test for opaque alpha mode
    if (alphaMode == 0)
        // Force full opacity
        alpha = 1.0;
    // Test mask cutoff threshold
    else if (alphaMode == 1 && alpha < material.AlphaFlags.y)
        // Discard fragment below cutoff
        discard;

    // The shared rasterizer disables fixed-function culling so double-sided
    // materials remain possible. Respect glTF single-sided semantics here for
    // transparent surfaces; otherwise every back face is alpha blended too and
    // thin stained-glass meshes quickly accumulate toward white.
    // Flag blended or transmissive surface
    bool transparentSurface = alphaMode == 2 || transmission > 0.001;
    // Test single sided back face
    if (transparentSurface && material.AlphaFlags.z < 0.5 && !gl_FrontFacing)
        // Cull single sided back face
        discard;

    // Sample metallic roughness texel when present
    vec4 mrTexel = hasMr
        // Fetch MR texel from texture atlas
        ? sampleAtlasTexture(fsUv, material.MrAtlasTransform, material.MrTextureAddress, material.MrTextureTransform)
        // Default MR texel to white
        : vec4(1.0);
    // Compute roughness from green channel
    float roughness = clamp(material.PbrFactors.y * (hasMr ? mrTexel.g : 1.0), 0.045, 1.0);
    // Compute metallic from blue channel
    float metallic = clamp(material.PbrFactors.x * (hasMr ? mrTexel.b : 1.0), 0.0, 1.0);

    // Default occlusion to unshadowed
    float occlusion = 1.0;
    // Test for occlusion texture
    if (hasOcclusion)
    // Begin occlusion sampling block
    {
        // Sample occlusion red channel
        float sampledOcclusion = sampleAtlasTexture(
            // Fetch occlusion texel from atlas
            fsUv, material.OcclusionAtlasTransform, material.OcclusionTextureAddress, material.OcclusionTextureTransform).r;
        // Blend occlusion by material strength
        occlusion = mix(1.0, sampledOcclusion, clamp(material.PbrFactors.w, 0.0, 1.0));
    // End occlusion sampling block
    }

    // Compute view direction to camera
    vec3 viewDir = normalize(Camera.CameraPosition.xyz - fsWorldPos);
    // Normalize interpolated surface normal
    vec3 normal = normalize(fsNormal);
    // Test double sided material flag
    if (material.AlphaFlags.z > 0.5)
    // Begin double sided normal block
    {
        // Test back facing normal
        if (dot(normal, viewDir) < 0.0)
            // Flip normal toward viewer
            normal = -normal;
    // End double sided normal block
    }
    // Test single sided back face
    else if (dot(normal, viewDir) < 0.0)
    // Begin normal flip block
    {
        // Flip normal toward viewer
        normal = -normal;
    // End normal flip block
    }

    // Test for normal map
    if (hasNormal)
    // Begin normal mapping block
    {
        // Compute addressed normal map UVs
        vec2 normalUv = addressUv(fsUv, material.NormalTextureAddress, material.NormalTextureTransform);
        // Sample tangent space normal
        vec3 tangentNormal = sampleAtlasTexture(
            // Decode normal from unsigned range
            fsUv, material.NormalAtlasTransform, material.NormalTextureAddress, material.NormalTextureTransform).xyz * 2.0 - 1.0;
        // Scale normal map strength
        tangentNormal.xy *= material.PbrFactors.z;
        // Normalize perturbed tangent normal
        tangentNormal = normalize(tangentNormal);
        // Transform normal to world space
        normal = normalize(cotangentFrame(normal, fsWorldPos, normalUv) * tangentNormal);
    // End normal mapping block
    }

    // Default emissive to black
    vec3 emissive = vec3(0.0);
    // Test for emissive contribution
    if (material.EmissiveFactor.a > 0.0)
    // Begin emissive block
    {
        // Select emissive source color
        vec3 emissionSource = hasEmissive
            // Sample emissive texel from atlas
            ? srgbToLinear(sampleAtlasTexture(
                // Convert emissive texel to linear
                fsUv, material.EmissiveAtlasTransform, material.EmissiveTextureAddress, material.EmissiveTextureTransform).rgb)
            // Fall back to base color
            : baseColor;
        // Scale emissive by factor and strength
        emissive = emissionSource * material.EmissiveFactor.rgb * material.EmissiveFactor.a;
    // End emissive block
    }

    // Test UV debug view
    if (debugMode == 1)
    // Begin UV debug block
    {
        // Compute addressed base UVs
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        // Output UV gradient debug color
        outColor = vec4(uv.x, uv.y, 0.0, 1.0);
        // Exit after UV debug output
        return;
    // End UV debug block
    }
    // Test atlas debug view
    if (debugMode == 2)
    // Begin atlas debug block
    {
        // Compute addressed base UVs
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        // Map UVs into atlas region
        vec2 atlasUv = material.BaseAtlasTransform.xy + uv * material.BaseAtlasTransform.zw;
        // Output atlas grid debug color
        outColor = vec4(fract(atlasUv.x * 8.0), fract(atlasUv.y * 8.0), hasBase ? 1.0 : 0.0, 1.0);
        // Exit after atlas debug output
        return;
    // End atlas debug block
    }
    // Test base texture debug view
    if (debugMode == 3)
    // Begin base texture debug block
    {
        // Output base texel debug color
        outColor = vec4(hasBase ? baseTexel.rgb : linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        // Exit after base debug output
        return;
    // End base texture debug block
    }
    // Test base color debug view
    if (debugMode == 4)
    // Begin base color debug block
    {
        // Output base color debug swatch
        outColor = vec4(linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        // Exit after color debug output
        return;
    // End base color debug block
    }
    // Test normal debug view
    if (debugMode == 5)
    // Begin normal debug block
    {
        // Output normal vector debug color
        outColor = vec4(normal * 0.5 + 0.5, 1.0);
        // Exit after normal debug output
        return;
    // End normal debug block
    }
    // Test metallic debug view
    if (debugMode == 6)
    // Begin metallic debug block
    {
        // Output metallic debug grayscale
        outColor = vec4(vec3(metallic), 1.0);
        // Exit after metallic debug output
        return;
    // End metallic debug block
    }
    // Test roughness debug view
    if (debugMode == 7)
    // Begin roughness debug block
    {
        // Output roughness debug grayscale
        outColor = vec4(vec3(roughness), 1.0);
        // Exit after roughness debug output
        return;
    // End roughness debug block
    }
    // Test occlusion debug view
    if (debugMode == 8)
    // Begin occlusion debug block
    {
        // Output occlusion debug grayscale
        outColor = vec4(vec3(occlusion), 1.0);
        // Exit after occlusion debug output
        return;
    // End occlusion debug block
    }
    // Test emissive debug view
    if (debugMode == 9)
    // Begin emissive debug block
    {
        // Output tone mapped emissive debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(emissive)), 1.0);
        // Exit after emissive debug output
        return;
    // End emissive debug block
    }

    // Clamp normal view cosine
    float nDotV = max(dot(normal, viewDir), 0.0001);
    // Compute dielectric Fresnel reflectance
    float dielectricF0 = pow((ior - 1.0) / max(ior + 1.0, 0.0001), 2.0);
    // Blend Fresnel base by metallic
    vec3 f0 = mix(vec3(dielectricF0), baseColor, metallic);
    // Initialize direct light accumulator
    vec3 directLighting = vec3(0.0);
    // Initialize clearcoat light accumulator
    vec3 clearcoatDirect = vec3(0.0);
    // Clamp active light count
    int lightCount = min(32, int(Camera.Counts.x + 0.5));
    // Loop over active lights
    for (int i = 0; i < lightCount; i++)
    // Begin per light loop
    {
        // Fetch current light record
        RasterLight light = Lights[i];
        // Test disabled light flag
        if (light.ConeEnabled.w < 0.5)
            // Skip disabled light
            continue;

        // Read light type selector
        float kind = light.PositionKind.w;
        // Declare light direction variable
        vec3 lightDirection;
        // Default attenuation to none
        float attenuation = 1.0;
        // Default spotlight cone to full
        float cone = 1.0;

        // Test directional light type
        if (kind > 0.5 && kind < 1.5)
        // Begin directional light block
        {
            // Aim along directional light vector
            lightDirection = normalize(-light.DirectionRange.xyz);
        // End directional light block
        }
        // Handle positional light types
        else
        // Begin positional light block
        {
            // Compute vector to light
            vec3 toLight = light.PositionKind.xyz - fsWorldPos;
            // Measure distance to light
            float distanceToLight = length(toLight);
            // Test degenerate light distance
            if (distanceToLight < 0.0001)
                // Skip coincident light
                continue;

            // Normalize direction to light
            lightDirection = toLight / distanceToLight;
            // Read light range cutoff
            float range = light.DirectionRange.w;
            // Apply inverse square falloff
            attenuation = 1.0 / (1.0 + 0.11 * distanceToLight * distanceToLight);
            // Test finite range cutoff
            if (range > 0.0001)
            // Begin range fade block
            {
                // Normalize distance by range
                float normalizedDistance = clamp(distanceToLight / range, 0.0, 1.0);
                // Compute range edge fade
                float edgeFade = 1.0 - normalizedDistance * normalizedDistance;
                // Fade attenuation at range edge
                attenuation *= edgeFade * edgeFade;
            // End range fade block
            }

            // Test spotlight type
            if (kind >= 1.5)
            // Begin spotlight cone block
            {
                // Normalize spotlight forward vector
                vec3 spotForward = normalize(light.DirectionRange.xyz);
                // Measure spot alignment cosine
                float coneDot = dot(normalize(fsWorldPos - light.PositionKind.xyz), spotForward);
                // Read inner cone angle
                float inner = light.ConeEnabled.x;
                // Read outer cone angle
                float outer = light.ConeEnabled.y;
                // Interpolate cone falloff factor
                cone = clamp((coneDot - outer) / max(0.0001, inner - outer), 0.0, 1.0);
                // Soften cone falloff curve
                cone *= cone;
            // End spotlight cone block
            }
        // End positional light block
        }

        // Clamp normal light cosine
        float nDotL = max(dot(normal, lightDirection), 0.0);
        // Test back facing light
        if (nDotL <= 0.0)
            // Skip unlit contribution
            continue;

        // Compute Blinn Phong half vector
        vec3 halfVector = normalize(lightDirection + viewDir);
        // Clamp normal half cosine
        float nDotH = max(dot(normal, halfVector), 0.0);
        // Clamp view half cosine
        float vDotH = max(dot(viewDir, halfVector), 0.0);
        // Square perceptual roughness value
        float alphaRoughness = roughness * roughness;
        // Evaluate GGX normal distribution
        float distribution = distributionGgx(nDotH, alphaRoughness);
        // Evaluate Smith geometry term
        float visibility = visibilitySmithGgxCorrelated(nDotV, nDotL, alphaRoughness);
        // Evaluate Schlick Fresnel term
        vec3 fresnel = fresnelSchlick(vDotH, f0);
        // Combine specular BRDF terms
        vec3 specular = distribution * visibility * fresnel;
        // Compute energy conserving diffuse
        vec3 diffuse = (vec3(1.0) - fresnel) * (1.0 - metallic) * baseColor / PI;
        // Scale light color by falloff
        vec3 radiance = light.ColorIntensity.rgb * light.ColorIntensity.w * attenuation * cone * 0.18;
        // Accumulate diffuse plus specular lighting
        directLighting += (diffuse + specular) * radiance * nDotL;

        // KHR_materials_clearcoat adds a dielectric layer. Reuse the already
        // sampled transmission mask when the asset shares that texture, so the
        // StainedGlassLamp path adds ALU only and no extra texture fetch.
        // Test clearcoat layer presence
        if (clearcoat > 0.001)
        // Begin clearcoat direct block
        {
            // Square clearcoat roughness value
            float clearcoatAlpha = clearcoatRoughness * clearcoatRoughness;
            // Evaluate clearcoat GGX distribution
            float clearcoatDistribution = distributionGgx(nDotH, clearcoatAlpha);
            // Evaluate clearcoat geometry term
            float clearcoatVisibility = visibilitySmithGgxCorrelated(nDotV, nDotL, clearcoatAlpha);
            // Evaluate clearcoat Fresnel term
            vec3 clearcoatFresnel = fresnelSchlick(vDotH, vec3(0.04));
            // Accumulate clearcoat specular direct light
            clearcoatDirect += clearcoat * clearcoatDistribution * clearcoatVisibility *
                // Scale clearcoat by radiance and cosine
                clearcoatFresnel * radiance * nDotL;
        // End clearcoat direct block
        }
    // End per light loop
    }

    // Compute mirror reflection vector
    vec3 reflection = reflect(-viewDir, normal);
    // Approximate image based Fresnel
    vec3 fresnelIbl = f0 + (max(vec3(1.0 - roughness), f0) - f0) * pow(1.0 - nDotV, 5.0);
    // Compute diffuse image based lighting
    vec3 diffuseIbl = diffuseEnvironment(normal) * baseColor * (vec3(1.0) - fresnelIbl) * (1.0 - metallic);
    // Sample sharp environment reflection
    vec3 sharpSpecularIbl = studioEnvironment(reflection);
    // Sample broad environment reflection
    vec3 broadSpecularIbl = diffuseEnvironment(normal);
    // Blend reflections by roughness
    vec3 specularIbl = mix(sharpSpecularIbl, broadSpecularIbl, roughness * roughness) * fresnelIbl;
    // Combine indirect light with occlusion
    vec3 indirectLighting = (diffuseIbl + specularIbl) * occlusion * 0.72;

    // Test direct light debug view
    if (debugMode == 10)
    // Begin direct debug block
    {
        // Output tone mapped direct debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(directLighting)), 1.0);
        // Exit after direct debug output
        return;
    // End direct debug block
    }
    // Test indirect light debug view
    if (debugMode == 11)
    // Begin indirect debug block
    {
        // Output tone mapped indirect debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(indirectLighting)), 1.0);
        // Exit after indirect debug output
        return;
    // End indirect debug block
    }

    // Sum direct and indirect lighting
    vec3 surfaceLighting = directLighting + indirectLighting;

    // Keep the fast single-pass raster path: transmission changes the shaded
    // light, never surface coverage. This follows glTF's separation of optical
    // transmission from alpha while avoiding a framebuffer resolve, mip chain,
    // additional render pass, or any extra texture sample for StainedGlassLamp.
    // Test transmissive material
    if (transmission > 0.001)
    // Begin transmission block
    {
        // Compute Snell refraction vector
        vec3 refractedDirection = refract(-viewDir, normal, 1.0 / ior);
        // Test total internal reflection
        if (dot(refractedDirection, refractedDirection) < 0.000001)
            // Fall back to mirror reflection
            refractedDirection = reflection;

        // Square roughness for blur
        float transmissionBlur = roughness * roughness;
        // Blend transmitted environment samples
        vec3 transmittedEnvironment = mix(
            // Sample sharp refracted environment
            studioEnvironment(refractedDirection),
            // Sample boosted backside diffuse
            diffuseEnvironment(-normal) * 1.35,
            // Blur by roughness factor
            transmissionBlur);

        // Tint glass by base color
        vec3 glassTint = mix(vec3(1.0), max(baseColor, vec3(0.001)), 0.72);
        // Default volume attenuation to none
        vec3 volumeAttenuation = vec3(1.0);
        // Read attenuation distance parameter
        float attenuationDistance = material.AttenuationColorDistance.w;
        // Test volumetric absorption enabled
        if (thickness > 0.0 && attenuationDistance > 0.0)
        // Begin absorption block
        {
            // Compute view dependent path length
            float pathLength = thickness / max(abs(dot(normal, viewDir)), 0.15);
            // Apply Beer Lambert absorption
            volumeAttenuation = pow(
                // Clamp attenuation color positive
                max(material.AttenuationColorDistance.rgb, vec3(0.0001)),
                // Scale absorption by normalized path
                vec3(pathLength / attenuationDistance));
        // End absorption block
        }

        // Tint transmitted environment light
        vec3 transmittedLight = transmittedEnvironment * glassTint * volumeAttenuation;
        // Evaluate transmission Fresnel term
        vec3 transmissionFresnel = fresnelSchlick(nDotV, vec3(dielectricF0));
        // Blend transmitted and surface light
        vec3 opticalResult = mix(transmittedLight, surfaceLighting, transmissionFresnel);
        // Mix surface toward optical result
        surfaceLighting = mix(surfaceLighting, opticalResult, transmission);
    // End transmission block
    }

    // Add emissive to surface lighting
    vec3 linearColor = surfaceLighting + emissive;
    // Test clearcoat layer presence
    if (clearcoat > 0.001)
    // Begin clearcoat indirect block
    {
        // Compute view Fresnel for clearcoat
        float clearcoatFresnelView = 0.04 + 0.96 * pow(1.0 - nDotV, 5.0);
        // Blend clearcoat environment reflections
        vec3 clearcoatIbl = mix(
            // Use sharp reflection sample
            sharpSpecularIbl,
            // Use broad reflection sample
            broadSpecularIbl,
            // Weight clearcoat IBL by Fresnel
            clearcoatRoughness * clearcoatRoughness) * clearcoatFresnelView * clearcoat;
        // Attenuate base by clearcoat layer
        linearColor *= 1.0 - clearcoat * clearcoatFresnelView;
        // Add clearcoat direct and indirect
        linearColor += clearcoatDirect + clearcoatIbl;
    // End clearcoat indirect block
    }

    // Alpha remains geometric coverage. OPAQUE and MASK transmission materials
    // therefore stay visibly present instead of becoming ordinary transparency.
    // Select output alpha by mode
    float outputAlpha = alphaMode == 2 ? alpha : 1.0;

    // Tone map and encode output
    vec3 outputColor = linearToSrgb(pbrNeutralToneMap(linearColor));
    // Write final fragment color
    outColor = vec4(outputColor, outputAlpha);
// End main function
}
// End fragment shader source string
";
// End VulkanRasterRenderer class
}
// Declare shared pipeline holder
internal sealed class SharedRasterResources : IDisposable
// Begin shared resources class
{
    // Hold shader resource layout
    public required ResourceLayout Layout { get; init; }
    // Hold compiled shaders
    public required Shader[] Shaders { get; init; }
    // Hold opaque pipeline
    public required Pipeline OpaquePipeline { get; init; }
    // Hold transparent pipeline
    public required Pipeline TransparentPipeline { get; init; }
    // Hold wireframe depth prepass pipeline
    public required Pipeline DepthPrepassPipeline { get; init; }
    // Hold solid wireframe pipeline
    public required Pipeline WireframePipeline { get; init; }
    // Hold dashed wireframe pipeline
    public required Pipeline WireframeDashedPipeline { get; init; }

    /// <summary>Disposes the shared raster pipelines, shaders, and resource layout.</summary>
    // Release shared raster resources
    public void Dispose()
    // Begin Dispose body
    {
        // Dispose opaque pipeline safely
        try { OpaquePipeline.Dispose(); } catch { }
        // Dispose transparent pipeline safely
        try { TransparentPipeline.Dispose(); } catch { }
        // Dispose prepass pipeline safely
        try { DepthPrepassPipeline.Dispose(); } catch { }
        // Dispose wireframe pipeline safely
        try { WireframePipeline.Dispose(); } catch { }
        // Dispose dashed pipeline safely
        try { WireframeDashedPipeline.Dispose(); } catch { }
        // Check shaders before disposal
        if (Shaders != null)
        // Begin shader null check
        {
            // Loop over shaders
            foreach (Shader shader in Shaders)
            // Begin shader loop
            {
                // Dispose one shader safely
                try { shader.Dispose(); } catch { }
            // End shader loop
            }
        // End shader null check
        }
        // Dispose resource layout safely
        try { Layout.Dispose(); } catch { }
    // End Dispose body
    }
// End shared resources class
}


// Declare render target cache
internal sealed class RasterTargets : IDisposable
// Begin targets class
{
    // Hold target width
    public required int Width { get; init; }
    // Hold target height
    public required int Height { get; init; }
    // Hold color texture
    public required Texture ColorTexture { get; init; }
    // Hold depth texture
    public required Texture DepthTexture { get; init; }
    // Hold staging texture
    public required Texture StagingTexture { get; init; }
    // Hold framebuffer
    public required Framebuffer Framebuffer { get; init; }
    // Hold command list
    public required CommandList CommandList { get; init; }
    // Hold GPU fence
    public required Fence Fence { get; init; }
    // Track last use serial
    public long LastUseSerial { get; set; }

    /// <summary>Disposes the cached render targets, command list, and fence.</summary>
    // Release render target resources
    public void Dispose()
    // Begin Dispose body
    {
        // Dispose fence safely
        try { Fence.Dispose(); } catch { }
        // Dispose command list safely
        try { CommandList.Dispose(); } catch { }
        // Dispose framebuffer safely
        try { Framebuffer.Dispose(); } catch { }
        // Dispose staging texture safely
        try { StagingTexture.Dispose(); } catch { }
        // Dispose depth texture safely
        try { DepthTexture.Dispose(); } catch { }
        // Dispose color texture safely
        try { ColorTexture.Dispose(); } catch { }
    // End Dispose body
    }
// End targets class
}
