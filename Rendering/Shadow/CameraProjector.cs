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
        if (!TryGetCameraSpace(p, cameraPosition, basis, out double vx, out double vy, out double vz) || vz <= ShadowLimits.CameraNear)
        {
            projected = default;
            return false;
        }

        if (!ProjectCameraSpace(vx, vy, vz, width, height, out double x, out double y))
        {
            projected = default;
            return false;
        }

        projected = new ProjectedVertex(x, y, vz);
        return true;
    }

    /// <summary>Transforms one world point into camera-space coordinates.</summary>
    public static bool TryGetCameraSpace(Vec3 p, Vec3 cameraPosition, CameraBasis basis, out double vx, out double vy, out double vz)
    {
        Vec3 rel = p - cameraPosition;
        vx = rel.Dot(basis.Right);
        vy = rel.Dot(basis.Up);
        vz = rel.Dot(basis.Forward);
        return double.IsFinite(vx) && double.IsFinite(vy) && double.IsFinite(vz);
    }

    /// <summary>Projects camera-space coordinates to raster coordinates.</summary>
    public static bool ProjectCameraSpace(double vx, double vy, double vz, int width, int height, out double x, out double y)
    {
        double aspect = width / (double)Math.Max(1, height);
        double fov = Math.Tan((ShadowLimits.CameraFovDegrees * Math.PI / 180.0) * 0.5);
        double u = vx / vz;
        double v = vy / vz;
        x = (1.0 - u / (aspect * fov)) * 0.5 * width;
        y = (1.0 - v / fov) * 0.5 * height;
        return double.IsFinite(x) && double.IsFinite(y);
    }
}
