// File: Rendering/Shadow/CameraProjector.cs
// Purpose: World-to-raster projection for the preview camera.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class CameraProjector
{
    /// <summary>Projects one world point to raster coordinates.</summary>
    public static bool TryProject(Vec3 p, Vec3 cameraPosition, CameraBasis basis, int width, int height, out ProjectedVertex projected)
    {
        Vec3 rel = p - cameraPosition;
        double z = rel.Dot(basis.Forward);
        if (!double.IsFinite(z) || z <= ShadowLimits.CameraNear)
        {
            projected = default;
            return false;
        }

        double aspect = width / (double)Math.Max(1, height);
        double fov = Math.Tan((ShadowLimits.CameraFovDegrees * Math.PI / 180.0) * 0.5);
        double u = rel.Dot(basis.Right) / z;
        double v = rel.Dot(basis.Up) / z;
        double x = (1.0 - u / (aspect * fov)) * 0.5 * width;
        double y = (1.0 - v / fov) * 0.5 * height;
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            projected = default;
            return false;
        }

        projected = new ProjectedVertex(x, y, z);
        return true;
    }
}
