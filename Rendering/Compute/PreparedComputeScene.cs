// File: Rendering/Compute/PreparedComputeScene.cs
// Purpose: Cached GPU scene buffers, stamp, and counts.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class PreparedComputeScene : IDisposable
{
    public required Scene Scene { get; init; }
    public required SceneCacheStamp CacheStamp { get; init; }
    public required ComputeSceneBuffers Buffers { get; init; }
    public required int SourceTriangleCount { get; init; }
    public required int TriangleCount { get; init; }
    public required int BvhNodeCount { get; init; }
    public required int SourceLightCount { get; init; }
    public required int LightCount { get; init; }
    public required int TextureCount { get; init; }
    public required long SceneBufferBytes { get; init; }

    /// <summary>Disposes the prepared scene GPU buffers.</summary>
    public void Dispose()
    {
        GpuDisposal.Dispose(Buffers);
    }
}
