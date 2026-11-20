// File: Rendering/Raster/RasterDeviceFactory.cs
// Purpose: Shared Vulkan graphics device with one-time child-process preflight.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class RasterDeviceFactory
{
    private readonly RasterStageLog log;
    private readonly RasterOptions options;
    private readonly object deviceLock;
    private GraphicsDevice? device;
    private bool preflightCompleted;

    public RasterDeviceFactory(RasterStageLog log, RasterOptions options, object deviceLock)
    {
        this.log = log;
        this.options = options;
        this.deviceLock = deviceLock;
    }

    /// <summary>Returns the currently cached device, if any. The caller must hold the engine locks.</summary>
    public GraphicsDevice? Current => device;

    /// <summary>Returns the shared Vulkan graphics device, creating it after a child-process preflight.</summary>
    public GraphicsDevice GetOrCreate()
    {
        lock (deviceLock)
        {
            if (device != null)
            {
                log.Stage("Reuse shared Veldrid Vulkan raster GraphicsDevice");
                return device;
            }

            if (!preflightCompleted)
            {
                log.Stage("Preflight Veldrid Vulkan raster GraphicsDevice in child process");
                VeldridVulkanDevicePreflight.VerifyInChildProcess(log.Stage);
                preflightCompleted = true;
            }

            log.Stage("Create shared Veldrid Vulkan raster GraphicsDevice");
            GraphicsDevice created = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            {
                Debug = options.IsVulkanDebugEnabled,
                // Use Vulkan-native clip-space Y and flip Y explicitly in the vertex shader.
                // This avoids depending on backend-specific viewport normalization while
                // keeping the projection math easy to audit.
                PreferStandardClipSpaceYDirection = false,
                PreferDepthRangeZeroToOne = true,
                SyncToVerticalBlank = false
            });

            if (created.BackendType != GraphicsBackend.Vulkan)
            {
                created.Dispose();
                throw new InvalidOperationException("Veldrid did not create a Vulkan graphics device for the raster backend.");
            }

            device = created;
            return created;
        }
    }

    /// <summary>Drains the GPU without disposing, so cached buffers can be released safely.</summary>
    public void Drain()
    {
        try { device?.WaitForIdle(); }
        catch { }
    }

    /// <summary>Disposes the cached shared graphics device, if any.</summary>
    public void DisposeCurrent()
    {
        GraphicsDevice? owned = device;
        device = null;
        if (owned == null)
            return;

        try { log.Stage("Dispose shared Vulkan raster GraphicsDevice"); owned.Dispose(); }
        catch { }
    }
}
