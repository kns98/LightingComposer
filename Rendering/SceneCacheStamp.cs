using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

/// <summary>
/// Identifies the exact mutable scene state used to prepare a CPU or GPU cache.
/// A Scene reference alone is not sufficient because transforms rebuild geometry
/// in place without replacing the Scene instance.
/// </summary>
public readonly record struct SceneCacheStamp(Scene Scene, long Revision)
{
    public static SceneCacheStamp Capture(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new SceneCacheStamp(scene, scene.Revision);
    }

    public bool Matches(Scene scene) =>
        ReferenceEquals(Scene, scene) && Revision == scene.Revision;
}
