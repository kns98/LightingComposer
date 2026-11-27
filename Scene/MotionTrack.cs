// -----------------------------------------------------------------------------
// File: Scene/MotionTrack.cs
// Purpose: Per-object transform keyframe track on the global movie timeline.
//
// Keyframes hold full transform snapshots at global frames; sampling holds
// outside the key range and smoothstep-interpolates between keys.
// -----------------------------------------------------------------------------
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Full transform snapshot at one global frame.</summary>
public sealed record MotionKeyframe(int Frame, Vec3 Position, Vec3 Rotation, Vec3 Scale);

/// <summary>Sampled transform pose for one frame.</summary>
public sealed record MotionPose(Vec3 Position, Vec3 Rotation, Vec3 Scale);

/// <summary>Transform keyframe track for one object id.</summary>
public sealed class MotionTrack
{
    public int ObjectId { get; }
    public List<MotionKeyframe> Keys { get; } = new();

    /// <summary>Creates an empty track for the given object id.</summary>
    public MotionTrack(int objectId)
    {
        ObjectId = objectId;
    }

    /// <summary>Adds a keyframe, replacing any key at the same frame and keeping frame order.</summary>
    public void AddKey(MotionKeyframe key)
    {
        if (key.Frame < 0) throw new ArgumentOutOfRangeException(nameof(key), "Keyframe frames must be zero or greater.");
        Keys.RemoveAll(existing => existing.Frame == key.Frame);
        Keys.Add(key);
        Keys.Sort(static (a, b) => a.Frame.CompareTo(b.Frame));
    }

    /// <summary>Removes the keyframe at the given frame. Returns true when one existed.</summary>
    public bool RemoveKey(int frame) => Keys.RemoveAll(existing => existing.Frame == frame) > 0;

    /// <summary>Samples the pose at the given global frame, or null when the track is empty.</summary>
    public MotionPose? Sample(int frame)
    {
        if (Keys.Count == 0)
            return null;
        if (frame <= Keys[0].Frame)
            return PoseOf(Keys[0]);
        if (frame >= Keys[^1].Frame)
            return PoseOf(Keys[^1]);
        for (int i = 0; i < Keys.Count - 1; i++)
        {
            MotionKeyframe a = Keys[i];
            MotionKeyframe b = Keys[i + 1];
            if (frame < a.Frame || frame > b.Frame)
                continue;
            if (b.Frame == a.Frame)
                return PoseOf(b);
            double t = (double)(frame - a.Frame) / (b.Frame - a.Frame);
            double s = t * t * (3.0 - 2.0 * t);
            return new MotionPose(
                Vec3.Lerp(a.Position, b.Position, s),
                Vec3.Lerp(a.Rotation, b.Rotation, s),
                Vec3.Lerp(a.Scale, b.Scale, s));
        }
        return PoseOf(Keys[^1]);
    }

    private static MotionPose PoseOf(MotionKeyframe key) => new(key.Position, key.Rotation, key.Scale);
}
