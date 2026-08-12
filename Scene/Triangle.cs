/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;

namespace LightingShowcase.SceneGraph;

/// <summary>Renderable triangle primitive with material and optional UV coordinates.</summary>
public sealed class Triangle
{
    public Vec3 A { get; }
    public Vec3 B { get; }
    public Vec3 C { get; }
    public Vec2 UvA { get; }
    public Vec2 UvB { get; }
    public Vec2 UvC { get; }
    public Material Material { get; }
    public int GroupId { get; }
    public Vec3 Normal { get; }
    public Vec3 NormalA { get; }
    public Vec3 NormalB { get; }
    public Vec3 NormalC { get; }
    public Vec3 Centroid { get; }
    public Aabb Bounds { get; }

    private readonly Vec3 edge1;
    private readonly Vec3 edge2;
    public Triangle(Vec3 a, Vec3 b, Vec3 c, Material material, int groupId = -1)
        : this(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1), material, groupId)
    {
    }
    public Triangle(Vec3 a, Vec3 b, Vec3 c, Vec2 uvA, Vec2 uvB, Vec2 uvC, Material material, int groupId = -1)
        : this(a, b, c, uvA, uvB, uvC, Vec3.Zero, Vec3.Zero, Vec3.Zero, material, groupId)
    {
    }

    /// <summary>Constructs a triangle with authored per-vertex shading normals.</summary>
    public Triangle(
        Vec3 a, Vec3 b, Vec3 c,
        Vec2 uvA, Vec2 uvB, Vec2 uvC,
        Vec3 normalA, Vec3 normalB, Vec3 normalC,
        Material material, int groupId = -1)
    {
        A = a; B = b; C = c;
        UvA = uvA; UvB = uvB; UvC = uvC;
        Material = material; GroupId = groupId;
        edge1 = b - a;
        edge2 = c - a;
        Normal = edge1.Cross(edge2).Normalize();
        NormalA = NormalizeOrFallback(normalA, Normal);
        NormalB = NormalizeOrFallback(normalB, Normal);
        NormalC = NormalizeOrFallback(normalC, Normal);
        Centroid = (a + b + c) / 3.0;
        Bounds = Aabb.Around(this);
    }

    /// <summary>Tests a ray against the primitive or bounds and returns hit information.</summary>
    public Hit? Intersect(Ray ray)
    {
        const double eps = 1e-6;
        // The ray test is the Möller–Trumbore algorithm. The determinant first rejects rays parallel to the
        // triangle plane; the following u/v barycentric tests reject hits outside the three edges without
        // constructing a plane equation.
        Vec3 h = ray.Direction.Cross(edge2);
        double det = edge1.Dot(h);
        if (System.Math.Abs(det) < eps) return null;

        double invDet = 1.0 / det;
        Vec3 s = ray.Origin - A;
        double u = invDet * s.Dot(h);
        if (u < 0.0 || u > 1.0) return null;

        Vec3 q = s.Cross(edge1);
        double v = invDet * ray.Direction.Dot(q);
        if (v < 0.0 || u + v > 1.0) return null;

        double t = invDet * edge2.Dot(q);
        if (t < eps) return null;

        // The three barycentric weights are reused to interpolate UVs and authored vertex normals at the exact hit
        // position. That keeps texture lookup and smooth shading consistent with the geometric intersection.
        double w = 1.0 - u - v;
        Vec2 uv = UvA * w + UvB * u + UvC * v;
        Vec3 shadingNormal = NormalizeOrFallback(NormalA * w + NormalB * u + NormalC * v, Normal);
        if (shadingNormal.Dot(Normal) < 0.0)
            shadingNormal = -shadingNormal;

        BuildTangentBasis(shadingNormal, out Vec3 tangent, out Vec3 bitangent);
        return new Hit(
            t,
            ray.Origin + ray.Direction * t,
            shadingNormal,
            Material,
            GroupId,
            uv.U,
            uv.V,
            tangent,
            bitangent);
    }

    // Tangent and bitangent directions are derived from the triangle edges and UV gradients, then orthogonalized
    // against the shading normal. The handedness test preserves mirrored UV orientation; degenerate UVs fall back
    // to a geometric basis.
    private void BuildTangentBasis(Vec3 shadingNormal, out Vec3 tangent, out Vec3 bitangent)
    {
        Vec2 deltaUv1 = new(UvB.U - UvA.U, UvB.V - UvA.V);
        Vec2 deltaUv2 = new(UvC.U - UvA.U, UvC.V - UvA.V);
        // The UV derivatives define how texture-space U and V map onto the triangle edges. Solving that 2x2 system
        // produces tangent/bitangent directions for normal mapping; a near-zero determinant means the UVs are
        // degenerate.
        double determinant = deltaUv1.U * deltaUv2.V - deltaUv1.V * deltaUv2.U;
        if (Math.Abs(determinant) > 1e-12)
        {
            double inverse = 1.0 / determinant;
            Vec3 rawTangent = (edge1 * deltaUv2.V - edge2 * deltaUv1.V) * inverse;
            Vec3 rawBitangent = (edge2 * deltaUv1.U - edge1 * deltaUv2.U) * inverse;
            tangent = (rawTangent - shadingNormal * shadingNormal.Dot(rawTangent)).Normalize();
            if (tangent.Length() > 1e-8)
            {
                double handedness = shadingNormal.Cross(tangent).Dot(rawBitangent) < 0.0 ? -1.0 : 1.0;
                bitangent = shadingNormal.Cross(tangent).Normalize() * handedness;
                return;
            }
        }

        // When UVs cannot provide a tangent basis, choose a world axis that is not almost parallel to the normal
        // and build an arbitrary orthonormal basis from cross products. This keeps normal mapping numerically
        // stable instead of returning zero vectors.
        Vec3 axis = Math.Abs(shadingNormal.Z) < 0.999
            ? new Vec3(0.0, 0.0, 1.0)
            : new Vec3(0.0, 1.0, 0.0);
        tangent = axis.Cross(shadingNormal).Normalize();
        bitangent = shadingNormal.Cross(tangent).Normalize();
    }

    private static Vec3 NormalizeOrFallback(Vec3 value, Vec3 fallback)
    {
        double length = value.Length();
        return double.IsFinite(length) && length > 1e-12 ? value / length : fallback;
    }
}
