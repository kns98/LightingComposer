// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerLightEditCommand.cs
// Purpose: Undoable light collection edits.
//
// Wraps light add, remove, and property changes so the Lighting Editor participates in undo and redo.
// -----------------------------------------------------------------------------

using LightingShowcase.Lighting;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Lightweight undo/redo command for lighting-only edits. It snapshots only the
/// light collection, avoiding a deep copy of large mesh geometry for every light
/// property or gizmo move.
/// </summary>
internal sealed class LightCollectionEditCommand : IComposerEditCommand, IFullRendererInvalidationEditCommand
{
    private readonly SceneLight[] before;
    private readonly SceneLight[] after;

    /// <summary>Snapshots the before and after light collections for an undoable edit.</summary>
    public LightCollectionEditCommand(string description, IEnumerable<SceneLight> before, IEnumerable<SceneLight> after)
    {
        Description = description;
        this.before = before.Select(Clone).ToArray();
        this.after = after.Select(Clone).ToArray();
    }

    public string Description { get; }
    public int? UndoSelectionId => null;
    public int? RedoSelectionId => null;

    /// <summary>Restores the light collection to its before snapshot.</summary>
    public void Undo(Scene scene) => Restore(scene, before);
    /// <summary>Restores the light collection to its after snapshot.</summary>
    public void Redo(Scene scene) => Restore(scene, after);

    /// <summary>Replaces the scene light collection with clones of the given snapshot.</summary>
    private static void Restore(Scene scene, IReadOnlyList<SceneLight> snapshot)
    {
        scene.Lights.Clear();
        foreach (SceneLight light in snapshot)
            scene.Lights.Add(Clone(light));
    }

    /// <summary>Creates an independent copy of the given scene light.</summary>
    internal static SceneLight Clone(SceneLight source) => new(
        source.Id,
        source.Position,
        source.Color,
        source.Intensity,
        source.Enabled,
        source.Kind,
        source.Direction,
        source.Range,
        source.InnerConeAngle,
        source.OuterConeAngle,
        source.CastsShadow,
        source.IsImported,
        source.IsDefault);
}
