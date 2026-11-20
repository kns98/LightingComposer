// File: Rendering/Raster/Vec3Extensions.cs
// Purpose: Double-precision Vec3 conversion to single-precision GPU vectors.

using System.Numerics;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class Vec3Extensions
{
    /// <summary>Converts a Vec3 to a single-precision Vector3.</summary>
    public static Vector3 ToVector3(this Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);

    /// <summary>Converts a Vec3 and w component to a single-precision Vector4.</summary>
    public static Vector4 ToVector4(this Vec3 value, float w) => new((float)value.X, (float)value.Y, (float)value.Z, w);
}
