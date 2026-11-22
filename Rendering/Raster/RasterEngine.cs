// File: Rendering/Raster/RasterEngine.cs
// Purpose: Owns raster device, pipelines, scene, and target caches behind render locks.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class RasterEngine
{
    private readonly object renderSync = new();
    private readonly object deviceSync = new();
    private readonly RasterStageLog log;
    private readonly RasterOptions options;
    private readonly RasterDeviceFactory devices;
    private readonly RasterPipelineFactory pipelines;
    private readonly RasterShadowFactory shadows;
    private readonly PreparedSceneCache scenes;
    private readonly RasterTargetCache targets;
    private readonly FrameRenderer frames;

    public RasterEngine()
    {
        options = new RasterOptions();
        log = new RasterStageLog(options);
        devices = new RasterDeviceFactory(log, options, deviceSync);
        pipelines = new RasterPipelineFactory(log, deviceSync);
        shadows = new RasterShadowFactory(log, deviceSync);
        scenes = new PreparedSceneCache(log, options);
        targets = new RasterTargetCache(log);
        frames = new FrameRenderer(devices, targets, pipelines, shadows, scenes, log, options);
    }

    /// <summary>Renders one frame, serialized against disposal and renderer switches.</summary>
    public RenderImage Render(FrameRequest request, out string details)
    {
        // Serialize the Veldrid/Vulkan raster path against disposal and resize/backend
        // switches. Without this, closing the form or switching renderers while a
        // preview frame is submitting can dispose the shared device underneath the
        // worker thread and crash inside the graphics driver instead of raising a
        // managed exception.
        lock (renderSync)
        {
            return frames.Render(request, out details);
        }
    }

    /// <summary>Refreshes cached geometry in place when only baked vertices changed.</summary>
    public bool TryRefresh(Scene scene, CancellationToken token, out string details)
    {
        lock (renderSync)
        {
            lock (deviceSync)
            {
                return scenes.TryRefresh(devices.Current, scene, token, out details);
            }
        }
    }

    /// <summary>Releases scene-sized Vulkan buffers while keeping the device and pipelines alive.</summary>
    public void ReleaseScene()
    {
        lock (renderSync)
        {
            lock (deviceSync)
            {
                devices.Drain();
                scenes.Release();
            }
        }
    }

    /// <summary>Releases the shared Vulkan graphics device and compiled raster pipeline.</summary>
    public void DisposeShared()
    {
        // Disposal must not race an in-flight render. Veldrid object disposal during
        // command submission/readback can produce native driver crashes that bypass
        // normal C# exception handling.
        lock (renderSync)
        {
            lock (deviceSync)
            {
                log.Stage("Dispose shared Vulkan raster GraphicsDevice: WaitForIdle");
                devices.Drain();
                targets.DisposeAll();
                scenes.Release();
                pipelines.Release();
                shadows.Release();
                devices.DisposeCurrent();
            }
        }
    }
}
