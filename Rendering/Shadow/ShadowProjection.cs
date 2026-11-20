// File: Rendering/Shadow/ShadowProjection.cs
// Purpose: Orthographic or perspective light-space shadow projection.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal readonly struct ShadowProjection
{
    private readonly Vec3 origin;
    private readonly Vec3 forward;
    private readonly Vec3 right;
    private readonly Vec3 up;
    private readonly bool perspective;
    private readonly double halfExtent;
    private readonly double tanHalfFov;
    private readonly double farPlane;
    public double Bias { get; }

    /// <summary>Initializes a shadow projection with its basis and clipping planes.</summary>
    private ShadowProjection(Vec3 origin, Vec3 forward, Vec3 right, Vec3 up, bool perspective, double halfExtent, double tanHalfFov, double farPlane, double bias)
    {
        this.origin = origin;
        this.forward = forward;
        this.right = right;
        this.up = up;
        this.perspective = perspective;
        this.halfExtent = Math.Max(1e-5, halfExtent);
        this.tanHalfFov = Math.Max(1e-5, tanHalfFov);
        this.farPlane = Math.Max(1e-5, farPlane);
        Bias = Math.Max(0.0001, bias);
    }

    /// <summary>Creates an orthographic shadow projection.</summary>
    public static ShadowProjection CreateOrthographic(Vec3 origin, Vec3 forward, double halfExtent, double farPlane, double bias)
    {
        OrthonormalBasis.Create(forward, out Vec3 f, out Vec3 r, out Vec3 u);
        return new ShadowProjection(origin, f, r, u, false, halfExtent, 1.0, farPlane, bias);
    }

    /// <summary>Creates a perspective shadow projection.</summary>
    public static ShadowProjection CreatePerspective(Vec3 origin, Vec3 forward, double fovRadians, double farPlane, double bias)
    {
        OrthonormalBasis.Create(forward, out Vec3 f, out Vec3 r, out Vec3 u);
        return new ShadowProjection(origin, f, r, u, true, 1.0, Math.Tan(fovRadians * 0.5), farPlane, bias);
    }

    /// <summary>Projects one world point into shadow map coordinates.</summary>
    public bool TryProject(Vec3 point, int width, int height, out ProjectedVertex projected)
    {
        Vec3 rel = point - origin;
        double z = rel.Dot(forward);
        if (!double.IsFinite(z) || z <= ShadowLimits.CameraNear || z > farPlane)
        {
            projected = default;
            return false;
        }

        double xComponent = rel.Dot(right);
        double yComponent = rel.Dot(up);
        double normalizedX;
        double normalizedY;
        if (perspective)
        {
            normalizedX = xComponent / (z * tanHalfFov);
            normalizedY = yComponent / (z * tanHalfFov);
        }
        else
        {
            normalizedX = xComponent / halfExtent;
            normalizedY = yComponent / halfExtent;
        }
        if (normalizedX < -1.12 || normalizedX > 1.12 || normalizedY < -1.12 || normalizedY > 1.12)
        {
            projected = default;
            return false;
        }

        double x = (normalizedX * 0.5 + 0.5) * (width - 1);
        double y = (0.5 - normalizedY * 0.5) * (height - 1);
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            projected = default;
            return false;
        }

        projected = new ProjectedVertex(x, y, z);
        return true;
    }
}
