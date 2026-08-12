/*
 * Camera state is kept independent of Avalonia and renderer-specific code. That lets interactive navigation,
 * scripted paths, tests, and multiple render backends use the same definitions for position, orientation,
 * projection, and interpolation.
 *
 * `CameraBasis` is a value type, so small instances can be copied without heap allocation. Its operations
 * establish shared numerical/data semantics for callers that would otherwise risk implementing subtly different
 * formulas.
 *
 * The `CameraBasis` constructor captures `forward`, `right`, `up`. Those are the dependencies/initial values the
 * instance needs for its lifetime, so callbacks and later operations use the same objects/configuration rather
 * than looking them up globally.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.CameraSystem;

/// <summary>Right/up/forward camera basis vectors used for ray generation.</summary>
public readonly struct CameraBasis
{
    public readonly Vec3 Forward;
    public readonly Vec3 Right;
    public readonly Vec3 Up;
    public CameraBasis(Vec3 forward, Vec3 right, Vec3 up)
    {
        Forward = forward;
        Right = right;
        Up = up;
    }
}
