/*
 * These math primitives are intentionally tiny and allocation-free because they sit on geometry and rendering hot
 * paths. Their formulas establish shared conventions for vector arithmetic, interpolation, normalization, and
 * component-wise operations; handling degenerate numeric cases here prevents subtle differences between callers.
 *
 * `Vec3` is a value type, so small instances can be copied without heap allocation. Its operations establish
 * shared numerical/data semantics for callers that would otherwise risk implementing subtly different formulas.
 *
 * `Zero` is derived rather than separately stored: it evaluates `new(0, 0, 0)`. Keeping the value computed from
 * its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * The `Vec3` constructor stores its coordinate components directly. Because the struct is immutable, those
 * component values completely define the vector for the rest of its lifetime.
 *
 * `Dot` computes the scalar dot product `X*v.X + Y*v.Y + Z*v.Z`. Besides measuring directional alignment, the
 * class reuses it to obtain squared magnitude without duplicating the component formula.
 *
 * `Cross` returns the right-handed cross product, a vector perpendicular to both inputs. Geometry code uses this
 * operation to derive surface/axis directions and orientation.
 *
 * `Length` computes Euclidean magnitude as `sqrt(Dot(this))`, deliberately reusing the dot-product definition so
 * the vector norm is consistent with the rest of the type.
 *
 * `Normalize` converts the vector to unit length by dividing by its magnitude, but returns `Zero` when the
 * magnitude is below `1e-8`. That guard avoids division by an effectively zero number and prevents noise from
 * being magnified into an arbitrary direction.
 *
 * `Multiply` multiplies corresponding components rather than taking a dot product. This is useful for RGB
 * modulation and per-axis scaling where each channel/axis must remain independent.
 *
 * `Lerp` performs component-wise linear interpolation `a + (b-a)*t`. Values of `t` between 0 and 1 blend between
 * endpoints, while values outside that range naturally extrapolate.
 */
namespace LightingShowcase.Math3D;

/// <summary>Immutable three-dimensional vector used for points, directions, normals, and RGB colors.</summary>
public readonly struct Vec3
{
    public readonly double X, Y, Z;
    public static Vec3 Zero => new(0, 0, 0);
    public Vec3(double x, double y, double z)
    {
        X = x; Y = y; Z = z;
    }
    public double Dot(Vec3 v) => X * v.X + Y * v.Y + Z * v.Z;

    public Vec3 Cross(Vec3 v) => new(
        Y * v.Z - Z * v.Y,
        Z * v.X - X * v.Z,
        X * v.Y - Y * v.X
    );
    public double Length() => System.Math.Sqrt(Dot(this));

    public Vec3 Normalize()
    {
        double len = Length();
        return len < 1e-8 ? Zero : this / len;
    }
    public Vec3 Multiply(Vec3 v) => new(X * v.X, Y * v.Y, Z * v.Z);

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t
    );

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 value) => new(-value.X, -value.Y, -value.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
}
