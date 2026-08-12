/*
 * Camera state is kept independent of Avalonia and renderer-specific code. That lets interactive navigation,
 * scripted paths, tests, and multiple render backends use the same definitions for position, orientation,
 * projection, and interpolation.
 *
 * `CameraSample` is a value type, so small instances can be copied without heap allocation. Its operations
 * establish shared numerical/data semantics for callers that would otherwise risk implementing subtly different
 * formulas.
 *
 * The `CameraSample` constructor captures `position`, `target`. Those are the dependencies/initial values the
 * instance needs for its lifetime, so callbacks and later operations use the same objects/configuration rather
 * than looking them up globally.
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
