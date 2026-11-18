// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerTransformWorkItem.cs
// Purpose: Thread-safe transform payload from UI to worker.
//
// Immutable record captured on the UI thread with no Avalonia objects, so it is safe to execute on a background worker.
// -----------------------------------------------------------------------------

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
    /// <summary>Applies the captured transform request to the given session.</summary>
    public bool Apply(ComposerSceneSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Transform.Apply(session, ObjectId, Name, Visible);
    }
}
