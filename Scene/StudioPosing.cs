// -----------------------------------------------------------------------------
// File: Scene/StudioPosing.cs
// Purpose: Applies sampled motion poses to scene groups for one frame.
//
// Posing writes plain group transforms (which render live without baking) and
// is always paired with a restore, so scrubbing and rendering never leave the
// authored scene posed.
// -----------------------------------------------------------------------------
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Captures, applies, and restores per-object transform poses.</summary>
public static class StudioPosing
{
    /// <summary>Captures the current transform of every group in the scene.</summary>
    public static Dictionary<int, MotionPose> Capture(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        Dictionary<int, MotionPose> snapshot = new();
        foreach (SceneObjectGroup group in scene.ObjectGroups.SelectMany(static g => g.SelfAndDescendants()))
            snapshot[group.Id] = new MotionPose(group.Position, group.Rotation, group.Scale);
        return snapshot;
    }

    /// <summary>Applies poses by object id, skipping ids with no group.</summary>
    public static void ApplyPoses(Scene scene, IReadOnlyDictionary<int, MotionPose> poses)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (poses == null) throw new ArgumentNullException(nameof(poses));
        foreach ((int id, MotionPose pose) in poses)
        {
            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null)
                continue;
            group.Position = pose.Position;
            group.Rotation = pose.Rotation;
            group.Scale = pose.Scale;
        }
    }
}
