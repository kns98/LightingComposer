// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerSceneSession.Studio.cs
// Purpose: Animation studio side of the scene session.
//
// Camera, shot, motion-track, and movie-settings edits. These mutate scene
// metadata directly without undo steps (keyframe capture and list edits are
// additive); callers mark the document modified through their own callback.
// -----------------------------------------------------------------------------
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record StudioState(
    IReadOnlyList<SceneCamera> Cameras,
    IReadOnlyList<StudioShot> Shots,
    IReadOnlyList<MotionTrack> Tracks,
    MovieSettings Settings);

internal sealed partial class ComposerSceneSession
{
    private int? selectedCameraId;
    private bool showCameraMarkers = true;
    private SceneCamera[]? cameraMoveBeforeCameras;
    private StudioShot[]? cameraMoveBeforeShots;
    private int? cameraMoveId;

    /// <summary>Returns the selected studio camera id, if any.</summary>
    public int? SelectedCameraId
    {
        get
        {
            sceneGate.Wait();
            try { return selectedCameraId; }
            finally { sceneGate.Release(); }
        }
    }

    /// <summary>Selects a studio camera by id, or clears the selection.</summary>
    public void SetSelectedCamera(int? id)
    {
        sceneGate.Wait();
        try
        {
            selectedCameraId = id.HasValue && scene.StudioCameras.Any(camera => camera.Id == id.Value) ? id : null;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns true when camera markers are drawn in the viewport.</summary>
    public bool ShowCameraMarkers
    {
        get
        {
            sceneGate.Wait();
            try { return showCameraMarkers; }
            finally { sceneGate.Release(); }
        }
    }

    /// <summary>Shows or hides the viewport camera markers.</summary>
    public bool SetShowCameraMarkers(bool visible)
    {
        sceneGate.Wait();
        try
        {
            if (showCameraMarkers == visible)
                return false;
            showCameraMarkers = visible;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns a snapshot copy of the studio cameras, shots, tracks, and settings.</summary>
    public StudioState GetStudioState()
    {
        sceneGate.Wait();
        try
        {
            scene.PruneMotionTracks();
            return new StudioState(
                scene.StudioCameras.Select(static camera => new SceneCamera(camera.Id, camera.Name, camera.Definition.Clone())).ToArray(),
                scene.StudioShots.Select(static shot => new StudioShot(shot.Name, shot.CameraId, shot.FrameCount, shot.Move, shot.MoveAmount)).ToArray(),
                scene.MotionTracks.Select(static track =>
                {
                    MotionTrack copy = new(track.ObjectId);
                    foreach (MotionKeyframe key in track.Keys)
                        copy.AddKey(key);
                    return copy;
                }).ToArray(),
                new MovieSettings
                {
                    Fps = scene.MovieSettings.Fps,
                    Width = scene.MovieSettings.Width,
                    Height = scene.MovieSettings.Height,
                    Backend = scene.MovieSettings.Backend,
                    Samples = scene.MovieSettings.Samples,
                    Bounces = scene.MovieSettings.Bounces,
                    Exposure = scene.MovieSettings.Exposure,
                    OutputName = scene.MovieSettings.OutputName
                });
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Adds a studio camera and returns its id.</summary>
    public int AddStudioCamera(string name, CameraDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        sceneGate.Wait();
        try
        {
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            int id = scene.AddStudioCamera(name, definition.Clone()).Id;
            CommitCameraCollectionEdit("Add camera", beforeCameras, beforeShots);
            return id;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Removes a studio camera and its shots. Returns true when one existed.</summary>
    public bool RemoveStudioCamera(int id)
    {
        sceneGate.Wait();
        try
        {
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            bool removed = scene.RemoveStudioCamera(id);
            if (removed)
                CommitCameraCollectionEdit("Delete camera", beforeCameras, beforeShots);
            return removed;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Renames a studio camera. Returns true when one existed.</summary>
    public bool RenameStudioCamera(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A camera name is required.", nameof(name));
        sceneGate.Wait();
        try
        {
            SceneCamera? camera = scene.StudioCameras.FirstOrDefault(candidate => candidate.Id == id);
            if (camera == null)
                return false;
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            camera.Name = name.Trim();
            CommitCameraCollectionEdit("Rename camera", beforeCameras, beforeShots);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Replaces a studio camera definition. Returns true when one existed.</summary>
    public bool SetStudioCameraDefinition(int id, CameraDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        sceneGate.Wait();
        try
        {
            SceneCamera? camera = scene.StudioCameras.FirstOrDefault(candidate => candidate.Id == id);
            if (camera == null)
                return false;
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            camera.Definition = definition.Clone();
            CommitCameraCollectionEdit("Update camera", beforeCameras, beforeShots);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Adds a studio shot and returns its index.</summary>
    public int AddStudioShot(string name, int cameraId, int frameCount, StudioShotMove move, double moveAmount)
    {
        if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount), "Shots must have at least one frame.");
        sceneGate.Wait();
        try
        {
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            StudioShot shot = new(name, cameraId, frameCount, move, moveAmount);
            scene.StudioShots.Add(shot);
            CommitCameraCollectionEdit("Add shot", beforeCameras, beforeShots);
            return scene.StudioShots.Count - 1;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Replaces the shot at the given index. Returns true when one existed.</summary>
    public bool UpdateStudioShot(int index, string name, int cameraId, int frameCount, StudioShotMove move, double moveAmount)
    {
        if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount), "Shots must have at least one frame.");
        sceneGate.Wait();
        try
        {
            if ((uint)index >= (uint)scene.StudioShots.Count)
                return false;
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            scene.StudioShots[index] = new StudioShot(name, cameraId, frameCount, move, moveAmount);
            CommitCameraCollectionEdit("Update shot", beforeCameras, beforeShots);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Removes the shot at the given index. Returns true when one existed.</summary>
    public bool RemoveStudioShot(int index)
    {
        sceneGate.Wait();
        try
        {
            if ((uint)index >= (uint)scene.StudioShots.Count)
                return false;
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            scene.StudioShots.RemoveAt(index);
            CommitCameraCollectionEdit("Delete shot", beforeCameras, beforeShots);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Moves the shot at the given index by the signed offset, clamped into range.</summary>
    public bool MoveStudioShot(int index, int delta)
    {
        sceneGate.Wait();
        try
        {
            if ((uint)index >= (uint)scene.StudioShots.Count)
                return false;
            int target = Math.Clamp(index + delta, 0, scene.StudioShots.Count - 1);
            if (target == index)
                return true;
            SceneCamera[] beforeCameras = CloneCameras();
            StudioShot[] beforeShots = CloneShots();
            StudioShot shot = scene.StudioShots[index];
            scene.StudioShots.RemoveAt(index);
            scene.StudioShots.Insert(target, shot);
            CommitCameraCollectionEdit("Reorder shots", beforeCameras, beforeShots);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Adds a motion keyframe for the given object. Returns false when the object is missing.</summary>
    public bool AddMotionKeyframe(int objectId, MotionKeyframe key)
    {
        ArgumentNullException.ThrowIfNull(key);
        sceneGate.Wait();
        try
        {
            if (scene.GroupById(objectId) == null)
                return false;
            scene.GetOrAddMotionTrack(objectId).AddKey(key);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Captures an absolute pose as a keyframe, preserving the trajectory elsewhere.</summary>
    public bool CaptureMotionKeyframe(int objectId, int frame, MotionPose absolute)
    {
        ArgumentNullException.ThrowIfNull(absolute);
        sceneGate.Wait();
        try
        {
            if (scene.GroupById(objectId) == null)
                return false;
            scene.GetOrAddMotionTrack(objectId).CaptureAt(frame, absolute);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Removes one motion keyframe. Returns true when one existed.</summary>
    public bool RemoveMotionKeyframe(int objectId, int frame)
    {
        sceneGate.Wait();
        try
        {
            MotionTrack? track = scene.MotionTracks.FirstOrDefault(candidate => candidate.ObjectId == objectId);
            return track != null && track.RemoveKey(frame);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Removes a whole motion track. Returns true when one existed.</summary>
    public bool RemoveMotionTrack(int objectId)
    {
        sceneGate.Wait();
        try
        {
            return scene.MotionTracks.RemoveAll(track => track.ObjectId == objectId) > 0;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Captures every group transform for studio posing restore.</summary>
    public Dictionary<int, MotionPose> CaptureStudioPoses()
    {
        sceneGate.Wait();
        try
        {
            return StudioPosing.Capture(scene);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Applies studio poses directly and rebuilds world geometry. No undo step.</summary>
    public void ApplyStudioPoses(IReadOnlyDictionary<int, MotionPose> poses)
    {
        ArgumentNullException.ThrowIfNull(poses);
        if (poses.Count == 0)
            return;
        sceneGate.Wait();
        try
        {
            StudioPosing.ApplyPoses(scene, poses);
            scene.RebuildWorldGeometry();
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Runs work against the live scene under the scene gate (movie rendering).</summary>
    public T WithScene<T>(Func<Scene, T> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        sceneGate.Wait(cancellationToken);
        try
        {
            return action(scene);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Picks the nearest visible camera marker under normalized viewport coordinates.</summary>
    public int? PickCameraMarker(
        CameraDefinition camera,
        double normalizedX,
        double normalizedY,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        sceneGate.Wait();
        try
        {
            if (!showCameraMarkers || width <= 0 || height <= 0)
                return null;
            double imageX = normalizedX * width;
            double imageY = normalizedY * height;
            return ComposerOverlayRenderer.TryPickCameraMarker(
                scene.StudioCameras,
                camera,
                width,
                height,
                imageX,
                imageY,
                out int cameraId)
                ? cameraId
                : null;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns a point bounding box at the selected camera position for gizmo hit testing.</summary>
    public Aabb? GetSelectedCameraGizmoBounds()
    {
        sceneGate.Wait();
        try
        {
            if (!showCameraMarkers || selectedCameraId is not int id)
                return null;
            SceneCamera? camera = scene.StudioCameras.FirstOrDefault(candidate => candidate.Id == id);
            if (camera == null)
                return null;
            return new Aabb(camera.Definition.Position, camera.Definition.Position);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Begins a gizmo move of the selected camera, snapshotting for undo and cancel.</summary>
    public SceneCamera? BeginSelectedCameraMove()
    {
        sceneGate.Wait();
        try
        {
            if (!showCameraMarkers || selectedCameraId is not int id)
                return null;
            SceneCamera? camera = scene.StudioCameras.FirstOrDefault(candidate => candidate.Id == id);
            if (camera == null)
                return null;
            cameraMoveBeforeCameras = CloneCameras();
            cameraMoveBeforeShots = CloneShots();
            cameraMoveId = id;
            return CameraCollectionEditCommand.CloneCamera(camera);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Moves the selected camera to the given position, translating its target with it.</summary>
    public bool PreviewSelectedCameraPosition(Vec3 position)
    {
        sceneGate.Wait();
        try
        {
            if (cameraMoveId is not int id)
                return false;
            SceneCamera? camera = scene.StudioCameras.FirstOrDefault(candidate => candidate.Id == id);
            SceneCamera? before = cameraMoveBeforeCameras?.FirstOrDefault(candidate => candidate.Id == id);
            if (camera == null || before == null)
                return false;
            Vec3 delta = position - before.Definition.Position;
            camera.Definition.Position = position;
            camera.Definition.Target = before.Definition.Target + delta;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Commits the active camera move to the undo history. Returns true when anything changed.</summary>
    public bool CommitSelectedCameraMove()
    {
        sceneGate.Wait();
        try
        {
            if (cameraMoveBeforeCameras == null || cameraMoveBeforeShots == null || cameraMoveId is not int id)
                return false;
            SceneCamera[] beforeCameras = cameraMoveBeforeCameras;
            StudioShot[] beforeShots = cameraMoveBeforeShots;
            cameraMoveBeforeCameras = null;
            cameraMoveBeforeShots = null;
            cameraMoveId = null;
            if (scene.StudioCameras.All(candidate => candidate.Id != id))
                return false;
            if (SameCameras(beforeCameras, scene.StudioCameras) && SameShots(beforeShots, scene.StudioShots))
                return false;
            editHistory.PushApplied(new CameraCollectionEditCommand("Move camera", beforeCameras, beforeShots, scene.StudioCameras, scene.StudioShots));
            ScenePath = null;
            InvalidateRendererCaches();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Cancels the active camera move and restores the snapshot.</summary>
    public void CancelSelectedCameraMove()
    {
        sceneGate.Wait();
        try
        {
            if (cameraMoveBeforeCameras == null || cameraMoveBeforeShots == null)
                return;
            RestoreCameras(cameraMoveBeforeCameras, cameraMoveBeforeShots);
            cameraMoveBeforeCameras = null;
            cameraMoveBeforeShots = null;
            cameraMoveId = null;
            InvalidateRendererCaches();
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Pushes a camera collection change onto the undo history when anything differs.</summary>
    private void CommitCameraCollectionEdit(string description, SceneCamera[] beforeCameras, StudioShot[] beforeShots)
    {
        if (SameCameras(beforeCameras, scene.StudioCameras) && SameShots(beforeShots, scene.StudioShots))
            return;
        editHistory.PushApplied(new CameraCollectionEditCommand(description, beforeCameras, beforeShots, scene.StudioCameras, scene.StudioShots));
        ScenePath = null;
        InvalidateRendererCaches();
    }

    /// <summary>Returns deep copies of the current studio cameras.</summary>
    private SceneCamera[] CloneCameras() => scene.StudioCameras.Select(CameraCollectionEditCommand.CloneCamera).ToArray();

    /// <summary>Returns deep copies of the current studio shots.</summary>
    private StudioShot[] CloneShots() => scene.StudioShots.Select(CameraCollectionEditCommand.CloneShot).ToArray();

    /// <summary>Replaces the studio cameras and shots with clones and repairs the selection.</summary>
    private void RestoreCameras(IEnumerable<SceneCamera> cameras, IEnumerable<StudioShot> shots)
    {
        scene.StudioCameras.Clear();
        foreach (SceneCamera camera in cameras)
            scene.StudioCameras.Add(CameraCollectionEditCommand.CloneCamera(camera));
        scene.StudioShots.Clear();
        foreach (StudioShot shot in shots)
            scene.StudioShots.Add(CameraCollectionEditCommand.CloneShot(shot));
        if (selectedCameraId.HasValue && scene.StudioCameras.All(camera => camera.Id != selectedCameraId.Value))
            selectedCameraId = null;
    }

    /// <summary>Returns true when both camera lists hold identical cameras in the same order.</summary>
    private static bool SameCameras(IReadOnlyList<SceneCamera> a, IReadOnlyList<SceneCamera> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Id != b[i].Id || a[i].Name != b[i].Name || !SameDefinition(a[i].Definition, b[i].Definition))
                return false;
        }
        return true;
    }

    /// <summary>Returns true when both camera definitions hold identical values.</summary>
    private static bool SameDefinition(CameraDefinition a, CameraDefinition b) =>
        EqualVec(a.Position, b.Position) && EqualVec(a.Target, b.Target) && EqualVec(a.Up, b.Up) &&
        a.FieldOfViewDegrees == b.FieldOfViewDegrees && a.NearPlane == b.NearPlane && a.FarPlane == b.FarPlane;

    /// <summary>Returns true when both shot lists hold identical shots in the same order.</summary>
    private static bool SameShots(IReadOnlyList<StudioShot> a, IReadOnlyList<StudioShot> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Name != b[i].Name || a[i].CameraId != b[i].CameraId || a[i].FrameCount != b[i].FrameCount ||
                a[i].Move != b[i].Move || a[i].MoveAmount != b[i].MoveAmount)
                return false;
        }
        return true;
    }

    /// <summary>Replaces the movie settings after validation.</summary>
    public void UpdateMovieSettings(MovieSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        sceneGate.Wait();
        try
        {
            scene.MovieSettings = new MovieSettings
            {
                Fps = settings.Fps,
                Width = settings.Width,
                Height = settings.Height,
                Backend = settings.Backend,
                Samples = settings.Samples,
                Bounces = settings.Bounces,
                Exposure = settings.Exposure,
                OutputName = settings.OutputName
            };
        }
        finally
        {
            sceneGate.Release();
        }
    }
}
