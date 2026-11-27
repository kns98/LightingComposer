// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerCameraEditCommand.cs
// Purpose: Undoable studio camera collection edits.
//
// Snapshots cameras and their shots so camera add, remove, rename, property,
// and gizmo moves participate in undo and redo.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Lightweight undo/redo command for camera-only edits. It snapshots only the
/// studio cameras and shots, avoiding a deep copy of large mesh geometry.
/// </summary>
internal sealed class CameraCollectionEditCommand : IComposerEditCommand, IFullRendererInvalidationEditCommand
{
    private readonly SceneCamera[] beforeCameras;
    private readonly StudioShot[] beforeShots;
    private readonly SceneCamera[] afterCameras;
    private readonly StudioShot[] afterShots;

    /// <summary>Snapshots the before and after camera collections for an undoable edit.</summary>
    public CameraCollectionEditCommand(
        string description,
        IEnumerable<SceneCamera> beforeCameras,
        IEnumerable<StudioShot> beforeShots,
        IEnumerable<SceneCamera> afterCameras,
        IEnumerable<StudioShot> afterShots)
    {
        Description = description;
        this.beforeCameras = beforeCameras.Select(CloneCamera).ToArray();
        this.beforeShots = beforeShots.Select(CloneShot).ToArray();
        this.afterCameras = afterCameras.Select(CloneCamera).ToArray();
        this.afterShots = afterShots.Select(CloneShot).ToArray();
    }

    public string Description { get; }
    public int? UndoSelectionId => null;
    public int? RedoSelectionId => null;

    /// <summary>Restores the camera collection to its before snapshot.</summary>
    public void Undo(Scene scene) => Restore(scene, beforeCameras, beforeShots);
    /// <summary>Restores the camera collection to its after snapshot.</summary>
    public void Redo(Scene scene) => Restore(scene, afterCameras, afterShots);

    /// <summary>Replaces the scene cameras and shots with clones of the given snapshots.</summary>
    private static void Restore(Scene scene, IReadOnlyList<SceneCamera> cameras, IReadOnlyList<StudioShot> shots)
    {
        scene.StudioCameras.Clear();
        foreach (SceneCamera camera in cameras)
            scene.StudioCameras.Add(CloneCamera(camera));
        scene.StudioShots.Clear();
        foreach (StudioShot shot in shots)
            scene.StudioShots.Add(CloneShot(shot));
    }

    /// <summary>Creates an independent copy of the given studio camera.</summary>
    internal static SceneCamera CloneCamera(SceneCamera source) =>
        new(source.Id, source.Name, source.Definition.Clone());

    /// <summary>Creates an independent copy of the given studio shot.</summary>
    internal static StudioShot CloneShot(StudioShot source) =>
        new(source.Name, source.CameraId, source.FrameCount, source.Move, source.MoveAmount);
}
