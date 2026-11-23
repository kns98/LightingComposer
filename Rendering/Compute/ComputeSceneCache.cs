// File: Rendering/Compute/ComputeSceneCache.cs
// Purpose: Cached GPU scene buffers with staleness rebuild.

using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class ComputeSceneCache
{
    private readonly ComputeStageLog log;
    private readonly object deviceLock;
    private PreparedComputeScene? prepared;

    public ComputeSceneCache(ComputeStageLog log, object deviceLock)
    {
        this.log = log;
        this.deviceLock = deviceLock;
    }

    /// <summary>Returns the cached GPU scene buffers, rebuilding them when stale.</summary>
    public PreparedComputeScene GetOrCreate(GraphicsDevice gd, Scene scene, CancellationToken token)
    {
        lock (deviceLock)
        {
            if (prepared != null && prepared.CacheStamp.Matches(scene))
            {
                log.Stage("Reuse cached Vulkan compute scene buffers");
                return prepared;
            }

            if (prepared != null)
            {
                try { gd.WaitForIdle(); }
                catch { }
                try { prepared.Dispose(); }
                catch { }
                prepared = null;
            }

            prepared = new PreparedComputeSceneBuilder(log).Build(gd, scene, token);
            return prepared;
        }
    }

    public void Release()
    {
        PreparedComputeScene? owned = prepared;
        prepared = null;
        GpuDisposal.Dispose(owned);
    }
}
