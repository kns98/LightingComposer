/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
namespace LightingShowcase.Composer;

/// <summary>
/// Immutable transform payload captured from Avalonia controls on the UI thread.
/// It intentionally contains no Avalonia objects, so it is safe to execute on a
/// background worker without violating Avalonia thread affinity.
/// </summary>
internal sealed record ComposerTransformWorkItem(
    int ObjectId,
    string Name,
    bool Visible,
    ComposerTransformRequest Transform)
{
    public bool Apply(ComposerSceneSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Transform.Apply(session, ObjectId, Name, Visible);
    }
}
