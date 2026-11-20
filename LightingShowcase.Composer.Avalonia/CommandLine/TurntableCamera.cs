// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/TurntableCamera.cs
// Purpose: Full-circle camera orbit positions for turntable animations.
//
// Rotates the base camera offset around the world up axis so frame zero
// matches the still framing and the last frame closes the loop.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;

namespace LightingShowcase.CommandLine;

internal static class TurntableCamera
{
    /// <summary>Returns one camera position per frame around a full circle.</summary>
    public static IReadOnlyList<Vec3> Orbit(CameraDefinition camera, int frameCount)
    {
        if (frameCount < 1)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be at least 1.");

        Vec3 offset = camera.Position - camera.Target;
        List<Vec3> positions = new(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            double angle = 2.0 * Math.PI * i / frameCount;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            Vec3 rotated = new(
                offset.X * cos + offset.Z * sin,
                offset.Y,
                -offset.X * sin + offset.Z * cos);
            positions.Add(camera.Target + rotated);
        }
        return positions;
    }
}
