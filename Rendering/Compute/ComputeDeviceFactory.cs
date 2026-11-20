// File: Rendering/Compute/ComputeDeviceFactory.cs
// Purpose: Shared Vulkan compute device with one-time child-process preflight.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class ComputeDeviceFactory
{
    // Veldrid/Vulkan device creation is expensive and, on some drivers, unsafe
    // when repeated rapidly during progressive sampling. Keep one device alive
    // for the app lifetime and only recreate it after an explicit cleanup.
    private readonly ComputeStageLog log;
    private readonly object deviceLock;
    private GraphicsDevice? device;
    private bool preflightCompleted;

    public ComputeDeviceFactory(ComputeStageLog log, object deviceLock)
    {
        this.log = log;
        this.deviceLock = deviceLock;
    }

    /// <summary>Returns the currently cached device, if any. The caller must hold the engine locks.</summary>
    public GraphicsDevice? Current => device;

    /// <summary>Returns the shared Vulkan graphics device, creating it when needed.</summary>
    public GraphicsDevice GetOrCreate()
    {
        lock (deviceLock)
        {
            if (device != null)
            {
                log.Stage("Reuse shared Veldrid Vulkan GraphicsDevice");
                return device;
            }

            if (!preflightCompleted)
            {
                log.Stage("Preflight Veldrid Vulkan GraphicsDevice in child process");
                VeldridVulkanDevicePreflight.VerifyInChildProcess(log.Stage);
                preflightCompleted = true;
            }

            log.Stage("Create shared Veldrid Vulkan GraphicsDevice in main process");
            GraphicsDevice created = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            {
                Debug = false,
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                SyncToVerticalBlank = false
            });

            log.Stage($"Shared GraphicsDevice created: backend={created.BackendType}");
            if (created.BackendType != GraphicsBackend.Vulkan)
            {
                created.Dispose();
                throw new InvalidOperationException("Veldrid did not create a Vulkan graphics device.");
            }

            device = created;
            return created;
        }
    }

    /// <summary>Creates the device if needed and returns its backend name.</summary>
    public string EnsureReady() => GetOrCreate().BackendType.ToString();

    /// <summary>Drains the GPU without disposing, so cached buffers can be released safely.</summary>
    public void Drain()
    {
        try { device?.WaitForIdle(); }
        catch { }
    }

    /// <summary>Waits for GPU idle, letting failures propagate for staged logging.</summary>
    public void WaitForIdle() => device?.WaitForIdle();

    /// <summary>Disposes the cached shared graphics device, if any.</summary>
    public void DisposeCurrent()
    {
        GraphicsDevice? owned = device;
        device = null;
        owned?.Dispose();
    }
}
