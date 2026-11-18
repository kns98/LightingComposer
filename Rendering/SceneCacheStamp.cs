// -----------------------------------------------------------------------------
// File: Rendering/SceneCacheStamp.cs
// Purpose: Scene revision stamp for render caches.
//
// Pairs a Scene reference with its revision so CPU and GPU caches detect in-place transform rebuilds.
// -----------------------------------------------------------------------------

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

/// <summary>
/// Identifies the exact mutable scene state used to prepare a CPU or GPU cache.
/// A Scene reference alone is not sufficient because transforms rebuild geometry
/// in place without replacing the Scene instance.
/// </summary>
public readonly record struct SceneCacheStamp(Scene Scene, long Revision)
{
    /// <summary>Captures the revision stamp for the given scene.</summary>
    public static SceneCacheStamp Capture(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new SceneCacheStamp(scene, scene.Revision);
    }

    /// <summary>Returns true when the stamp holds the same scene at its current revision.</summary>
    public bool Matches(Scene scene) =>
        ReferenceEquals(Scene, scene) && Revision == scene.Revision;
}
