// File: Rendering/Raster/PreparedRasterScene.cs
// Purpose: Cached GPU scene: buffers, atlas, descriptors, ranges, and stats.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class PreparedRasterScene : IDisposable
{
    public required Scene Scene { get; init; }
    public required SceneCacheStamp CacheStamp { get; set; }
    public required IReadOnlyDictionary<Material, int> MaterialIds { get; init; }
    public required SceneGpuBuffers Buffers { get; init; }
    public required SceneAtlas Atlas { get; init; }
    public required SceneDescriptorSets Descriptors { get; init; }
    public required IReadOnlyDictionary<int, PreviewRangeSet> GroupRanges { get; init; }
    public PreviewRangeCache PreviewCache { get; } = new();
    public MeshEditPatchState? ActiveMeshEdit { get; set; }
    public required PreparedSceneStats Stats { get; init; }

    /// <summary>Disposes the cached scene buffers, atlas, and resource sets.</summary>
    public void Dispose()
    {
        GpuDisposal.Dispose(Descriptors, Atlas, Buffers);
    }
}
