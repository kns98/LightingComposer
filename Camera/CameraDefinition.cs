/*
 * Camera state is kept independent of Avalonia and renderer-specific code. That lets interactive navigation,
 * scripted paths, tests, and multiple render backends use the same definitions for position, orientation,
 * projection, and interpolation.
 */
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CameraSystem;

// CameraDefinition is the canonical mutable look-at/projection state shared by all backends. UI code may change it
// continuously, while render jobs can Clone it to obtain a stable snapshot without sharing those mutations.
/// <summary>Canonical orbit/look-at camera state used by all render adapters.</summary>
public sealed class CameraDefinition
{
    public Vec3 Position { get; set; } = new(0.0, 0.55, -2.25);
    public Vec3 Target { get; set; } = new(0.0, 0.55, 0.0);
    public Vec3 Up { get; set; } = TransformConverter.WorldUp;
    public double FieldOfViewDegrees { get; set; } = 72.0;
    public double NearPlane { get; set; } = 0.05;
    public double FarPlane { get; set; } = 5000.0;

    public CameraDefinition Clone() => new()
    {
        Position = Position,
        Target = Target,
        Up = Up,
        FieldOfViewDegrees = FieldOfViewDegrees,
        NearPlane = NearPlane,
        FarPlane = FarPlane
    };

    // The look-at vectors are rebuilt as an orthonormal basis. Degenerate forward/up combinations fall back to the
    // engine’s canonical axes so ray generation never receives zero-length directions.
    public CameraBasis ToBasis()
    {
        Vec3 forward = (Target - Position).Normalize();
        if (forward.Length() < 1e-8)
            forward = TransformConverter.WorldForward;
        Vec3 right = Up.Cross(forward).Normalize();
        if (right.Length() < 1e-8)
            right = TransformConverter.WorldRight;
        Vec3 correctedUp = forward.Cross(right).Normalize();
        return new CameraBasis(forward, right, correctedUp);
    }
}
