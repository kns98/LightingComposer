// File: Rendering/Compute/VulkanSceneComputeRenderer.cs
// Purpose: Public facade for the Vulkan compute scene ray/path tracer.
//
// This renderer uses Vulkan compute through Veldrid rather than Vulkan RT
// BLAS/TLAS. It traces the current Scene.Triangles buffer on the GPU, applies
// scene lights, simple material properties, hard shadows, emissive surfaces, and
// optional stochastic bounces selected from the Render pane.

using System.IO;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

/// <summary>Vulkan compute ray/path tracer for the current triangle scene.</summary>
public static class VulkanSceneComputeRenderer
{
    private static readonly ComputeEngine Engine = new();

    public static string StageLogPath => ComputeStageLog.LogPath;

    /// <summary>Disposes the shared Vulkan device and its cached resources.</summary>
    public static void DisposeSharedDevice() => Engine.DisposeShared();

    /// <summary>
    /// Releases scene-sized Vulkan buffers while preserving the shared device
    /// and pipeline. This avoids overlapping the old GPU scene with a newly
    /// loaded model.
    /// </summary>
    public static void ReleasePreparedScene() => Engine.ReleaseScene();

    /// <summary>Initializes and validates the shared Vulkan device for command-line readiness checks.</summary>
    public static string EnsureDeviceReady() => Engine.EnsureReady();

    /// <summary>Tries to render the scene with Vulkan compute. Throws on unsupported systems; this renderer never falls back to CPU rendering.</summary>
    public static RenderImage Render(
        Scene scene,
        Vec3 cameraPosition,
        CameraBasis basis,
        int width,
        int height,
        int bounceCount,
        int sampleIndex,
        int sampleCount,
        CancellationToken cancellationToken,
        out string details,
        Action<RenderImage, string>? progressCallback = null,
        RenderSettings? settings = null,
        double fieldOfViewDegrees = 72.0) =>
        Engine.Render(
            new ComputeFrameRequest(
                scene, cameraPosition, basis, width, height, bounceCount, sampleIndex, sampleCount,
                cancellationToken, progressCallback, settings, fieldOfViewDegrees),
            out details);
}
