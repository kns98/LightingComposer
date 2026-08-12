/*
 * These math primitives are intentionally tiny and allocation-free because they sit on geometry and rendering hot
 * paths. Their formulas establish shared conventions for vector arithmetic, interpolation, normalization, and
 * component-wise operations; handling degenerate numeric cases here prevents subtle differences between callers.
 *
 * `Vec2` is a value type, so small instances can be copied without heap allocation. Its operations establish
 * shared numerical/data semantics for callers that would otherwise risk implementing subtly different formulas.
 *
 * `Zero` is derived rather than separately stored: it evaluates `new(0, 0)`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * The `Vec2` constructor stores its coordinate components directly. Because the struct is immutable, those
 * component values completely define the vector for the rest of its lifetime.
 */
namespace LightingShowcase.Math3D;

/// <summary>Immutable two-dimensional vector used primarily for UV texture coordinates.</summary>
public readonly struct Vec2
{
    public readonly double U;
    public readonly double V;
    public Vec2(double u, double v)
    {
        U = u;
        V = v;
    }

    public static Vec2 Zero => new(0, 0);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.U + b.U, a.V + b.V);
    public static Vec2 operator *(Vec2 a, double s) => new(a.U * s, a.V * s);
}
