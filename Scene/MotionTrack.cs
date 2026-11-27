// -----------------------------------------------------------------------------
// File: Scene/MotionTrack.cs
// Purpose: Per-object transform keyframe track on the global movie timeline.
//
// Keys store deltas relative to the previous keyframe (the first key stores an
// absolute pose), so shifting an early key ripples the motion after it.
// Sampling resolves absolute poses, holds outside the key range, and
// smoothstep-interpolates between keys.
// -----------------------------------------------------------------------------
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Transform delta at one global frame (absolute for the first key).</summary>
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

    /// <summary>Adds a keyframe as stored, replacing any key at the same frame.</summary>
    public void AddKey(MotionKeyframe key)
    {
        if (key.Frame < 0) throw new ArgumentOutOfRangeException(nameof(key), "Keyframe frames must be zero or greater.");
        Keys.RemoveAll(existing => existing.Frame == key.Frame);
        Keys.Add(key);
        Keys.Sort(static (a, b) => a.Frame.CompareTo(b.Frame));
    }

    /// <summary>Removes the keyframe at the given frame, folding its delta into the next key.</summary>
    public bool RemoveKey(int frame)
    {
        int at = Keys.FindIndex(existing => existing.Frame == frame);
        if (at < 0)
            return false;
        MotionKeyframe removed = Keys[at];
        Keys.RemoveAt(at);
        if (at < Keys.Count)
        {
            MotionKeyframe next = Keys[at];
            Keys[at] = next with
            {
                Position = next.Position + removed.Position,
                Rotation = next.Rotation + removed.Rotation,
                Scale = next.Scale + removed.Scale
            };
        }
        return true;
    }

    /// <summary>Captures an absolute pose at the given frame, preserving the trajectory elsewhere.</summary>
    public void CaptureAt(int frame, MotionPose absolute)
    {
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame), "Keyframe frames must be zero or greater.");
        ArgumentNullException.ThrowIfNull(absolute);
        RemoveKey(frame);
        int at = Keys.FindIndex(existing => existing.Frame > frame);
        if (at < 0)
            at = Keys.Count;
        MotionPose previous = AbsoluteBefore(frame);
        MotionKeyframe key = new(
            frame,
            absolute.Position - previous.Position,
            absolute.Rotation - previous.Rotation,
            absolute.Scale - previous.Scale);
        Keys.Insert(at, key);
        if (at + 1 < Keys.Count)
        {
            MotionKeyframe next = Keys[at + 1];
            Keys[at + 1] = next with
            {
                Position = next.Position - key.Position,
                Rotation = next.Rotation - key.Rotation,
                Scale = next.Scale - key.Scale
            };
        }
    }

    /// <summary>Samples the absolute pose at the given global frame, or null when empty.</summary>
    public MotionPose? Sample(int frame)
    {
        if (Keys.Count == 0)
            return null;
        List<(int Frame, MotionPose Pose)> absolute = AbsolutePoses();
        if (frame <= absolute[0].Frame)
            return absolute[0].Pose;
        if (frame >= absolute[^1].Frame)
            return absolute[^1].Pose;
        for (int i = 0; i < absolute.Count - 1; i++)
        {
            (int frameA, MotionPose a) = absolute[i];
            (int frameB, MotionPose b) = absolute[i + 1];
            if (frame < frameA || frame > frameB)
                continue;
            if (frameB == frameA)
                return b;
            double t = (double)(frame - frameA) / (frameB - frameA);
            double s = t * t * (3.0 - 2.0 * t);
            return new MotionPose(
                Vec3.Lerp(a.Position, b.Position, s),
                Vec3.Lerp(a.Rotation, b.Rotation, s),
                Vec3.Lerp(a.Scale, b.Scale, s));
        }
        return absolute[^1].Pose;
    }

    /// <summary>Resolves every key to its absolute pose in frame order.</summary>
    public List<(int Frame, MotionPose Pose)> AbsolutePoses()
    {
        List<(int Frame, MotionPose Pose)> absolute = new(Keys.Count);
        Vec3 position = Vec3.Zero;
        Vec3 rotation = Vec3.Zero;
        Vec3 scale = Vec3.Zero;
        bool first = true;
        foreach (MotionKeyframe key in Keys)
        {
            if (first)
            {
                position = key.Position;
                rotation = key.Rotation;
                scale = key.Scale;
                first = false;
            }
            else
            {
                position += key.Position;
                rotation += key.Rotation;
                scale += key.Scale;
            }
            absolute.Add((key.Frame, new MotionPose(position, rotation, scale)));
        }
        return absolute;
    }

    /// <summary>Converts absolute keyframes to relative storage, first key absolute.</summary>
    public static List<MotionKeyframe> ToRelative(IReadOnlyList<MotionKeyframe> absolute)
    {
        List<MotionKeyframe> sorted = absolute.OrderBy(static key => key.Frame).ToList();
        List<MotionKeyframe> relative = new(sorted.Count);
        Vec3 position = Vec3.Zero;
        Vec3 rotation = Vec3.Zero;
        Vec3 scale = Vec3.Zero;
        bool first = true;
        foreach (MotionKeyframe key in sorted)
        {
            if (first)
            {
                relative.Add(key);
                position = key.Position;
                rotation = key.Rotation;
                scale = key.Scale;
                first = false;
            }
            else
            {
                relative.Add(new MotionKeyframe(
                    key.Frame,
                    key.Position - position,
                    key.Rotation - rotation,
                    key.Scale - scale));
                position = key.Position;
                rotation = key.Rotation;
                scale = key.Scale;
            }
        }
        return relative;
    }

    private MotionPose AbsoluteBefore(int frame)
    {
        Vec3 position = Vec3.Zero;
        Vec3 rotation = Vec3.Zero;
        Vec3 scale = Vec3.Zero;
        bool found = false;
        foreach (MotionKeyframe key in Keys)
        {
            if (key.Frame >= frame)
                break;
            if (!found)
            {
                position = key.Position;
                rotation = key.Rotation;
                scale = key.Scale;
                found = true;
            }
            else
            {
                position += key.Position;
                rotation += key.Rotation;
                scale += key.Scale;
            }
        }
        return new MotionPose(position, rotation, scale);
    }
}
