// File: Rendering/Raster/VulkanRasterRenderer.cs
// Purpose: Public façade for the Vulkan graphics-pipeline raster preview.
//
// This renderer is intentionally separate from VulkanSceneComputeRenderer. It
// uses Vulkan's normal vertex/fragment raster pipeline for fast preview frames:
// vertex buffer -> depth-tested triangle rasterization -> fragment lighting ->
// off-screen color texture -> staging readback -> cross-platform RGBA image.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

/// <summary>Vulkan hardware rasterizer for the Render tab preview.</summary>
public static class VulkanRasterRenderer
{
    private static readonly RasterEngine Engine = new();

    public static string StageLogPath => RasterStageLog.LogPath;

    /// <summary>Releases the shared Vulkan graphics device and compiled raster pipeline.</summary>
    public static void DisposeSharedDevice() => Engine.DisposeShared();

    /// <summary>
    /// Releases scene-sized Vulkan buffers while keeping the device, pipelines,
    /// and small render-target cache alive. Call this before replacing a large
    /// scene so the old GPU allocation does not overlap the new scene load.
    /// </summary>
    public static void ReleasePreparedScene() => Engine.ReleaseScene();

    /// <summary>
    /// Reuploads only vertex positions, normals, UVs and material indices into an
    /// already allocated Vulkan scene. This is the fast path for baked transforms:
    /// textures, pipelines, descriptors, render targets and buffer allocations stay hot.
    /// </summary>
    public static bool TryRefreshPreparedGeometry(Scene scene, CancellationToken cancellationToken, out string details) =>
        Engine.TryRefresh(scene, cancellationToken, out details);

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    public static RenderImage Render(
        Scene scene, Vec3 cameraPosition, CameraBasis basis, int width, int height,
        CancellationToken cancellationToken, out string details) =>
        Render(scene, cameraPosition, basis, width, height, null, null, cancellationToken, out details);

    /// <summary>
    /// Renders with an optional editor-only transform. The preview updates one small
    /// uniform buffer and changes draw ranges; it does not rebuild or upload vertices.
    /// </summary>
    public static RenderImage Render(
        Scene scene, Vec3 cameraPosition, CameraBasis basis, int width, int height,
        VulkanRasterTransformPreview? preview, CancellationToken cancellationToken, out string details) =>
        Render(scene, cameraPosition, basis, width, height, preview, null, cancellationToken, out details);

    /// <summary>
    /// Renders with optional whole-object and mesh-component editor previews.
    /// Component moves patch only affected vertices in the existing GPU buffers.
    /// </summary>
    public static RenderImage Render(
        Scene scene, Vec3 cameraPosition, CameraBasis basis, int width, int height,
        VulkanRasterTransformPreview? preview, VulkanRasterMeshEditPreview? meshEditPreview,
        CancellationToken cancellationToken, out string details, bool wireframe = false) =>
        Engine.Render(new FrameRequest(scene, cameraPosition, basis, width, height, preview, meshEditPreview, cancellationToken, wireframe), out details);
}
