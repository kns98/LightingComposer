// -----------------------------------------------------------------------------
// File: Scene/StudioShot.cs
// Purpose: One timeline shot: a camera plus a move over a frame span.
// -----------------------------------------------------------------------------
namespace LightingShowcase.SceneGraph;

/// <summary>Camera move applied across a shot's frames.</summary>
public enum StudioShotMove
{
    Static = 0,
    Orbit = 1,
    Push = 2
}

/// <summary>One movie shot: a camera, a duration, and a camera move.</summary>
public sealed class StudioShot
{
    public string Name { get; set; }
    public int CameraId { get; set; }
    public int FrameCount { get; set; }
    public StudioShotMove Move { get; set; }

    /// <summary>
    /// Move magnitude: total yaw degrees for Orbit, end-radius fraction of the
    /// start radius for Push (0.6 pushes to 60%). Unused for Static.
    /// </summary>
    public double MoveAmount { get; set; }

    /// <summary>Creates a shot with the given name, camera, duration, and move.</summary>
    public StudioShot(string name, int cameraId, int frameCount, StudioShotMove move = StudioShotMove.Static, double moveAmount = 0.0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A shot name is required.", nameof(name));
        Name = name.Trim();
        CameraId = cameraId;
        FrameCount = frameCount;
        Move = move;
        MoveAmount = moveAmount;
    }

    /// <summary>Returns the shot name for list display.</summary>
    public override string ToString() => Name;
}
