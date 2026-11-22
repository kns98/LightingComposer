// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/CameraViews.cs
// Purpose: Named hero view rotations for headless auto-framing.
//
// Rotates the default camera offset around the target so stills can frame a
// model's best side without hand-computed coordinates.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.CommandLine;

internal static class CameraViews
{
    /// <summary>Returns true when the view name is a known hero view.</summary>
    public static bool IsKnownView(string? view) =>
        Normalize(view) is "auto" or "iso" or "front" or "left" or "right" or "top";

    /// <summary>Returns the camera offset for a named view, rotated from the default.</summary>
    public static Vec3 ApplyView(Vec3 offset, string? view)
    {
        return Normalize(view) switch
        {
            "auto" or "iso" => offset,
            "front" => RotateY(offset, Math.PI),
            "left" => RotateY(offset, Math.PI / 2.0),
            "right" => RotateY(offset, -Math.PI / 2.0),
            "top" => TopOffset(offset),
            _ => throw new ArgumentException($"Unknown view '{view}'. Use auto, front, left, right, or top.")
        };
    }

    private static string Normalize(string? view) =>
        string.IsNullOrWhiteSpace(view) ? "auto" : view.Trim().ToLowerInvariant();

    private static Vec3 RotateY(Vec3 offset, double angle)
    {
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        return new Vec3(
            offset.X * cos + offset.Z * sin,
            offset.Y,
            -offset.X * sin + offset.Z * cos);
    }

    private static Vec3 TopOffset(Vec3 offset)
    {
        double distance = Math.Max(0.5, offset.Length());
        Vec3 direction = new(0.3, 1.0, -0.3);
        return direction * (distance / direction.Length());
    }
}
