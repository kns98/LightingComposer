// -----------------------------------------------------------------------------
// File: Math/Vec3.cs
// Purpose: 3D vector math.
//
// Small immutable helper for points, directions, colors, normals, and vector operations used throughout the renderer.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Math3D;

/// <summary>Immutable three-dimensional vector used for points, directions, normals, and RGB colors.</summary>
public readonly struct Vec3
{
    public readonly double X, Y, Z;
    public static Vec3 Zero => new(0, 0, 0);

    /// <summary>Initializes a new three-dimensional vector.</summary>
    public Vec3(double x, double y, double z)
    {
        X = x; Y = y; Z = z;
    }

    /// <summary>Returns the dot product of this vector and another vector.</summary>
    public double Dot(Vec3 v) => X * v.X + Y * v.Y + Z * v.Z;

    /// <summary>Returns the cross product of this vector and another vector.</summary>
    public Vec3 Cross(Vec3 v) => new(
        Y * v.Z - Z * v.Y,
        Z * v.X - X * v.Z,
        X * v.Y - Y * v.X
    );

    /// <summary>Returns the Euclidean length of this vector.</summary>
    public double Length() => System.Math.Sqrt(Dot(this));

    /// <summary>Returns the unit vector in this direction, or zero when too short.</summary>
    public Vec3 Normalize()
    {
        double len = Length();
        return len < 1e-8 ? Zero : this / len;
    }

    /// <summary>Returns the component-wise product of this vector and another vector.</summary>
    public Vec3 Multiply(Vec3 v) => new(X * v.X, Y * v.Y, Z * v.Z);

    /// <summary>Interpolates between two vectors by the given factor.</summary>
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t
    );

    /// <summary>Adds two vectors component-wise.</summary>
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    /// <summary>Subtracts the second vector from the first component-wise.</summary>
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    /// <summary>Negates every component of the vector.</summary>
    public static Vec3 operator -(Vec3 value) => new(-value.X, -value.Y, -value.Z);
    /// <summary>Scales the vector by the given scalar.</summary>
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    /// <summary>Scales the vector by the given scalar.</summary>
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    /// <summary>Divides every component of the vector by the given scalar.</summary>
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
}
