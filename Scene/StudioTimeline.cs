// -----------------------------------------------------------------------------
// File: Scene/StudioTimeline.cs
// Purpose: Resolves studio shots into per-frame render cameras.
//
// Pure and deterministic: the same cameras and shots always produce the same
// frame sequence, so UI scrub, headless renders, and tests agree.
// -----------------------------------------------------------------------------
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>One resolved movie frame: its global index, shot, and camera.</summary>
public sealed record StudioFrame(int GlobalFrame, int ShotIndex, CameraDefinition Camera);

/// <summary>Resolves studio shots into per-frame cameras.</summary>
public static class StudioTimeline
{
    public const int MaxTotalFrames = 360;

    /// <summary>Resolves every shot frame in order, easing moves with smoothstep.</summary>
    public static IReadOnlyList<StudioFrame> ResolveFrames(IReadOnlyList<SceneCamera> cameras, IReadOnlyList<StudioShot> shots)
    {
        if (cameras == null) throw new ArgumentNullException(nameof(cameras));
        if (shots == null) throw new ArgumentNullException(nameof(shots));

        Dictionary<int, SceneCamera> byId = cameras.ToDictionary(camera => camera.Id);
        int total = 0;
        foreach (StudioShot shot in shots)
        {
            if (shot.FrameCount < 1)
                throw new ArgumentException($"Shot '{shot.Name}' must have at least one frame.", nameof(shots));
            total += shot.FrameCount;
        }
        if (total > MaxTotalFrames)
            throw new ArgumentException($"Movies are limited to {MaxTotalFrames} frames; this timeline has {total}.", nameof(shots));

        List<StudioFrame> frames = new(total);
        int global = 0;
        for (int shotIndex = 0; shotIndex < shots.Count; shotIndex++)
        {
            StudioShot shot = shots[shotIndex];
            if (!byId.TryGetValue(shot.CameraId, out SceneCamera? camera))
                throw new InvalidOperationException($"Shot '{shot.Name}' references a missing camera.");
            for (int i = 0; i < shot.FrameCount; i++)
            {
                double t = shot.FrameCount == 1 ? 0.0 : (double)i / (shot.FrameCount - 1);
                double eased = t * t * (3.0 - 2.0 * t);
                frames.Add(new StudioFrame(global++, shotIndex, MoveCamera(camera.Definition, shot.Move, shot.MoveAmount, eased)));
            }
        }
        return frames;
    }

    /// <summary>Samples object poses for one global frame across all tracks.</summary>
    public static IReadOnlyDictionary<int, MotionPose> SamplePoses(IEnumerable<MotionTrack> tracks, int frame)
    {
        if (tracks == null) throw new ArgumentNullException(nameof(tracks));
        Dictionary<int, MotionPose> poses = new();
        foreach (MotionTrack track in tracks)
        {
            MotionPose? pose = track.Sample(frame);
            if (pose != null)
                poses[track.ObjectId] = pose;
        }
        return poses;
    }

    private static CameraDefinition MoveCamera(CameraDefinition camera, StudioShotMove move, double amount, double eased)
    {
        if (eased == 0.0)
            return camera.Clone();
        Vec3 offset = camera.Position - camera.Target;
        switch (move)
        {
            case StudioShotMove.Static:
                return camera.Clone();
            case StudioShotMove.Orbit:
            {
                double angle = amount * Math.PI / 180.0 * eased;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                Vec3 rotated = new(
                    offset.X * cos + offset.Z * sin,
                    offset.Y,
                    -offset.X * sin + offset.Z * cos);
                return WithPosition(camera, camera.Target + rotated);
            }
            case StudioShotMove.Push:
            {
                if (amount <= 0.0)
                    throw new ArgumentOutOfRangeException(nameof(amount), "Push amount must be greater than zero.");
                double radius = offset.Length();
                if (radius < 1e-8)
                    return camera.Clone();
                double scaled = radius * (1.0 + (amount - 1.0) * eased);
                return WithPosition(camera, camera.Target + offset / radius * scaled);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(move), move, "Unknown studio shot move.");
        }
    }

    private static CameraDefinition WithPosition(CameraDefinition camera, Vec3 position) => new()
    {
        Position = position,
        Target = camera.Target,
        Up = camera.Up,
        FieldOfViewDegrees = camera.FieldOfViewDegrees,
        NearPlane = camera.NearPlane,
        FarPlane = camera.FarPlane
    };
}
