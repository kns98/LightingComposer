/*
 * Camera state is kept independent of Avalonia and renderer-specific code. That lets interactive navigation,
 * scripted paths, tests, and multiple render backends use the same definitions for position, orientation,
 * projection, and interpolation.
 *
 * `CameraKey` is a value type, so small instances can be copied without heap allocation. Its operations establish
 * shared numerical/data semantics for callers that would otherwise risk implementing subtly different formulas.
 *
 * The `CameraKey` constructor captures `time`, `position`, `target`. Those are the dependencies/initial values
 * the instance needs for its lifetime, so callbacks and later operations use the same objects/configuration
 * rather than looking them up globally.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.CameraSystem;

/// <summary>One editable keyframe in the demo camera path.</summary>
public readonly struct CameraKey
{
    public readonly double Time;
    public readonly Vec3 Position;
    public readonly Vec3 Target;
    public CameraKey(double time, Vec3 position, Vec3 target)
    {
        Time = time;
        Position = position;
        Target = target;
    }
}
