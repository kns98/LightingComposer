// File: Rendering/Raster/PreparedSceneCache.cs
// Purpose: Cached GPU scene with staleness rebuild and fast refresh.

using LightingShowcase.SceneGraph;
using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class PreparedSceneCache
{
    private readonly RasterStageLog log;
    private readonly RasterOptions options;
    private PreparedRasterScene? prepared;

    public PreparedSceneCache(RasterStageLog log, RasterOptions options)
    {
        this.log = log;
        this.options = options;
    }

    /// <summary>Returns the cached GPU scene, rebuilding buffers, atlas, and descriptors when stale.</summary>
    public PreparedRasterScene GetOrCreate(GraphicsDevice gd, SharedRasterResources resources, Scene scene, CancellationToken token)
    {
        bool requested = options.UseGpuTextureSampling;
        if (prepared != null &&
            prepared.CacheStamp.Matches(scene) &&
            prepared.Stats.GpuTextureSamplingRequested == requested)
            return prepared;

        prepared?.Dispose();
        prepared = null;
        log.ThrowIfCancellationRequested(token, "build cached Vulkan scene");
        PreparedRasterScene built = new PreparedSceneBuilder(log).Build(gd, resources.Layout, scene, token, requested);
        prepared = built;
        return built;
    }

    public bool TryRefresh(GraphicsDevice? gd, Scene scene, CancellationToken token, out string details) =>
        new FastGeometryRefresh(log).TryRefresh(gd, prepared, scene, token, out details);

    public void Release()
    {
        PreparedRasterScene? owned = prepared;
        prepared = null;
        GpuDisposal.Dispose(owned);
    }
}
