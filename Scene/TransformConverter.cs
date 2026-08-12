/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

// TransformConverter centralizes the coordinate conventions used by importers, editing code, and renderers. Keeping
// these operations here prevents one subsystem from silently using a different axis, pivot, rotation order, or
// scale rule.
/// <summary>Centralized transform helpers so importers, preview, and final render share one convention.</summary>
public static class TransformConverter
{
    public static readonly Vec3 WorldUp = new(0, 1, 0);
    public static readonly Vec3 WorldForward = new(0, 0, 1);
    public static readonly Vec3 WorldRight = new(1, 0, 0);

    // A zero scale cannot be inverted later. Values that are effectively zero are therefore replaced with 1.0 so
    // inverse transforms and normal transforms stay finite instead of producing infinities or NaNs.
    public static Vec3 SanitizeScale(Vec3 scale)
    {
        return new Vec3(
            Math.Abs(scale.X) < 1e-8 ? 1.0 : scale.X,
            Math.Abs(scale.Y) < 1e-8 ? 1.0 : scale.Y,
            Math.Abs(scale.Z) < 1e-8 ? 1.0 : scale.Z);
    }

    // Euler rotation is applied in X, then Y, then Z order. The inverse routine deliberately undoes those axes in
    // the opposite order; changing this order would change every authored object transform.
    public static Vec3 RotateEuler(Vec3 point, Vec3 rotation)
    {
        double cx = Math.Cos(rotation.X), sx = Math.Sin(rotation.X);
        double cy = Math.Cos(rotation.Y), sy = Math.Sin(rotation.Y);
        double cz = Math.Cos(rotation.Z), sz = Math.Sin(rotation.Z);

        Vec3 p = point;
        p = new Vec3(p.X, p.Y * cx - p.Z * sx, p.Y * sx + p.Z * cx);
        p = new Vec3(p.X * cy + p.Z * sy, p.Y, -p.X * sy + p.Z * cy);
        p = new Vec3(p.X * cz - p.Y * sz, p.X * sz + p.Y * cz, p.Z);
        return p;
    }

    // ApplySrt maps a local point into transformed space around a fixed pivot. It subtracts the pivot, applies
    // scale, then Euler rotation, and finally restores the pivot plus translation; that order is the convention the
    // rest of the scene code relies on.
    public static Vec3 ApplySrt(Vec3 point, Vec3 pivot, Vec3 position, Vec3 rotation, Vec3 scale)
    {
        Vec3 q = point - pivot;
        Vec3 safeScale = SanitizeScale(scale);
        q = new Vec3(q.X * safeScale.X, q.Y * safeScale.Y, q.Z * safeScale.Z);
        q = RotateEuler(q, rotation);
        return pivot + position + q;
    }

    // Normals cannot be transformed exactly like positions when scale is non-uniform. This method applies the
    // inverse scale before rotation—the SRT inverse-transpose for a normal—and renormalizes the result so lighting
    // dot products remain meaningful.
    /// <summary>Transforms a normal by the inverse-transpose of an SRT transform.</summary>
    public static Vec3 ApplySrtNormal(Vec3 normal, Vec3 rotation, Vec3 scale)
    {
        Vec3 safeScale = SanitizeScale(scale);
        Vec3 transformed = new(normal.X / safeScale.X, normal.Y / safeScale.Y, normal.Z / safeScale.Z);
        transformed = RotateEuler(transformed, rotation);
        double length = transformed.Length();
        return double.IsFinite(length) && length > 1e-12 ? transformed / length : normal.Normalize();
    }


    // ApplyInverseSrt reverses the point transform using the same pivot: remove pivot and translation, undo the
    // Euler rotations in reverse order, divide by scale, then restore the pivot. Mesh-edit code uses this to turn
    // world-space gestures back into local vertex coordinates.
    /// <summary>Applies the inverse of <see cref="ApplySrt"/> using the same fixed pivot.</summary>
    public static Vec3 ApplyInverseSrt(Vec3 point, Vec3 pivot, Vec3 position, Vec3 rotation, Vec3 scale)
    {
        Vec3 safeScale = SanitizeScale(scale);
        Vec3 q = point - pivot - position;
        q = RotateEulerInverse(q, rotation);
        q = new Vec3(q.X / safeScale.X, q.Y / safeScale.Y, q.Z / safeScale.Z);
        return pivot + q;
    }

    // This is the inverse of the normal transform. Rotation is undone first, scale is reapplied, and the vector is
    // normalized again so converting normals between spaces does not change their direction because of vector
    // length.
    /// <summary>Applies the inverse of the normal transform used by <see cref="ApplySrtNormal"/>.</summary>
    public static Vec3 ApplyInverseSrtNormal(Vec3 normal, Vec3 rotation, Vec3 scale)
    {
        Vec3 safeScale = SanitizeScale(scale);
        Vec3 transformed = RotateEulerInverse(normal, rotation);
        transformed = new Vec3(
            transformed.X * safeScale.X,
            transformed.Y * safeScale.Y,
            transformed.Z * safeScale.Z);
        double length = transformed.Length();
        return double.IsFinite(length) && length > 1e-12 ? transformed / length : normal.Normalize();
    }

    /// <summary>Reverses <see cref="RotateEuler"/> by undoing Z, then Y, then X rotation.</summary>
    public static Vec3 RotateEulerInverse(Vec3 point, Vec3 rotation)
    {
        double cx = Math.Cos(rotation.X), sx = Math.Sin(rotation.X);
        double cy = Math.Cos(rotation.Y), sy = Math.Sin(rotation.Y);
        double cz = Math.Cos(rotation.Z), sz = Math.Sin(rotation.Z);

        Vec3 p = point;
        p = new Vec3(p.X * cz + p.Y * sz, -p.X * sz + p.Y * cz, p.Z);
        p = new Vec3(p.X * cy - p.Z * sy, p.Y, p.X * sy + p.Z * cy);
        p = new Vec3(p.X, p.Y * cx + p.Z * sx, -p.Y * sx + p.Z * cx);
        return p;
    }

    public static Vec3 FromRightHandedZForwardToCanonical(Vec3 value) => new(value.X, value.Y, value.Z);
    // Z-up input is converted to the engine’s Y-up convention by swapping Y/Z and negating the new Z. The sign flip
    // preserves handedness rather than mirroring imported geometry.
    public static Vec3 FromZUpToCanonicalYUp(Vec3 value) => new(value.X, value.Z, -value.Y);
    public static Vec3 MirrorX(Vec3 value) => new(-value.X, value.Y, value.Z);
}
