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
            return scene.AddStudioCamera(name, definition.Clone()).Id;
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
            return scene.RemoveStudioCamera(id);
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
            camera.Name = name.Trim();
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
            camera.Definition = definition.Clone();
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
            StudioShot shot = new(name, cameraId, frameCount, move, moveAmount);
            scene.StudioShots.Add(shot);
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
            scene.StudioShots[index] = new StudioShot(name, cameraId, frameCount, move, moveAmount);
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
            scene.StudioShots.RemoveAt(index);
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
            StudioShot shot = scene.StudioShots[index];
            scene.StudioShots.RemoveAt(index);
            scene.StudioShots.Insert(target, shot);
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
