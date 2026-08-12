/*
 * Camera state is kept independent of Avalonia and renderer-specific code. That lets interactive navigation,
 * scripted paths, tests, and multiple render backends use the same definitions for position, orientation,
 * projection, and interpolation.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.CameraSystem;

/// <summary>Interpolated camera state produced by sampling the demo path.</summary>
public readonly struct CameraSample
{
    public readonly Vec3 Position;
    public readonly Vec3 Target;
    public CameraSample(Vec3 position, Vec3 target)
    {
        Position = position;
        Target = target;
    }
}
