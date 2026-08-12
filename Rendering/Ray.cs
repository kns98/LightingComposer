/*
 * The code here converts renderer-neutral scene/camera data into pixels or backend-ready state. Dimensions, cache
 * identity, data packing, and deterministic conversion are treated as part of the rendering contract so
 * interactive UI code does not need to know backend details.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

/// <summary>World-space ray with an origin and normalized direction.</summary>
public readonly struct Ray
{
    public readonly Vec3 Origin;
    public readonly Vec3 Direction;
    public Ray(Vec3 origin, Vec3 direction)
    {
        Origin = origin;
        Direction = direction.Normalize();
    }
}
