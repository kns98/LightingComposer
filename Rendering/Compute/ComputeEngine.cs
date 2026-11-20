// File: Rendering/Compute/ComputeEngine.cs
// Purpose: Owns compute device, pipeline, and scene caches behind render locks.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class ComputeEngine
{
    private readonly object renderSync = new();
    private readonly object deviceSync = new();
    private readonly ComputeStageLog log;
    private readonly ComputeDeviceFactory devices;
    private readonly ComputePipelineFactory pipelines;
    private readonly ComputeSceneCache scenes;
    private readonly ComputeFrameRenderer frames;

    public ComputeEngine()
    {
        log = new ComputeStageLog();
        devices = new ComputeDeviceFactory(log, deviceSync);
        pipelines = new ComputePipelineFactory(log, deviceSync);
        scenes = new ComputeSceneCache(log, deviceSync);
        frames = new ComputeFrameRenderer(devices, scenes, pipelines, log);
    }

    /// <summary>Renders one frame, serialized against disposal and renderer switches.</summary>
    public RenderImage Render(ComputeFrameRequest request, out string details)
    {
        lock (renderSync)
        {
            return frames.Render(request, out details);
        }
    }

    /// <summary>Initializes the shared Vulkan device and returns its backend name.</summary>
    public string EnsureReady() => devices.EnsureReady();

    /// <summary>Releases scene-sized Vulkan buffers while preserving the shared device and pipeline.</summary>
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

    /// <summary>Disposes the shared Vulkan device and its cached resources.</summary>
    public void DisposeShared()
    {
        lock (renderSync)
        {
            lock (deviceSync)
            {
                if (devices.Current == null)
                {
                    scenes.Release();
                    pipelines.Release();
                    return;
                }

                DisposeStep("Dispose shared Vulkan GraphicsDevice: WaitForIdle", devices.WaitForIdle);
                DisposeStep("Dispose prepared Vulkan compute scene", scenes.Release);
                DisposeStep("Dispose shared Vulkan compute resources", pipelines.Release);
                DisposeStep("Dispose shared Vulkan GraphicsDevice", devices.DisposeCurrent);
            }
        }
    }

    private void DisposeStep(string label, Action step)
    {
        log.Stage(label);
        try
        {
            step();
        }
        catch (Exception ex)
        {
            log.Stage($"{label} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
