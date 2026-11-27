// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/MovieFrameRenderer.cs
// Purpose: Shared single-frame backend dispatch for still and movie renders.
//
// One dispatch table used by headless stills, turntables, timeline movies,
// and UI movie rendering, so backend behavior cannot drift between surfaces.
// -----------------------------------------------------------------------------
using LightingShowcase.CameraSystem;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

internal static class MovieFrameRenderer
{
    /// <summary>Renders one frame with the request backend at the view's field of view.</summary>
    public static RenderImage RenderStill(
        Scene scene,
        CameraDefinition view,
        RenderRequest request,
        RenderSettings renderSettings,
        CancellationToken cancellationToken,
        out string details)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (view == null) throw new ArgumentNullException(nameof(view));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (renderSettings == null) throw new ArgumentNullException(nameof(renderSettings));

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
                fieldOfViewDegrees: view.FieldOfViewDegrees),
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
                backgroundBottom: renderSettings.BackgroundBottom,
                exposure: request.Exposure),
            RenderBackend.VulkanRasterPreview => VulkanRasterRenderer.Render(
                scene,
                view.Position,
                view.ToBasis(),
                request.Width,
                request.Height,
                cancellationToken,
                out details,
                backgroundTop: renderSettings.BackgroundTop,
                backgroundBottom: renderSettings.BackgroundBottom,
                exposure: request.Exposure),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request.Backend), request.Backend, "Unsupported command-line renderer."),
        };
    }
}
