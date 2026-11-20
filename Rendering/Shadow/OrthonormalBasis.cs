// File: Rendering/Shadow/OrthonormalBasis.cs
// Purpose: Builds an orthonormal basis from one forward vector.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class OrthonormalBasis
{
    /// <summary>Builds an orthonormal basis from one forward vector.</summary>
    public static void Create(Vec3 forward, out Vec3 f, out Vec3 r, out Vec3 u)
    {
        f = forward.Normalize();
        if (f.Length() < 1e-8)
            f = new Vec3(0, -1, 0);
        Vec3 helper = Math.Abs(f.Y) < 0.92 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
        r = helper.Cross(f).Normalize();
        if (r.Length() < 1e-8)
            r = new Vec3(1, 0, 0);
        u = f.Cross(r).Normalize();
        if (u.Length() < 1e-8)
            u = new Vec3(0, 1, 0);
    }
}
