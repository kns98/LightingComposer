// File: Rendering/Raster/TriangleCuller.cs
// Purpose: Pure per-triangle validity and near-plane tests for the rasterizer.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal static class TriangleCuller
{
    /// <summary>Returns true when a triangle has finite data and sits fully in front of the camera.</summary>
    public static bool IsRenderable(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
    {
        if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C))
            return false;
        if (!IsFinite(triangle.Normal))
            return false;

        // Avoid sending wildly distant coordinates or invalid camera-space values
        // into the Vulkan clipper. The CPU rasterizer naturally rejects these via
        // TryProjectCamera; doing it on the CPU side keeps the GPU path from ever
        // seeing NaN/Inf/overflow-prone vertices.
        return IsInFrontOfCamera(triangle, cameraPosition, basis);
    }

    /// <summary>Returns true when all three vector components are finite.</summary>
    public static bool IsFinite(Vec3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    /// <summary>Returns true when all three triangle corners are beyond the near plane.</summary>
    public static bool IsInFrontOfCamera(Triangle triangle, Vec3 cameraPosition, CameraBasis basis) =>
        CameraDepth(triangle.A, cameraPosition, basis) > RasterLimits.CameraNear &&
        CameraDepth(triangle.B, cameraPosition, basis) > RasterLimits.CameraNear &&
        CameraDepth(triangle.C, cameraPosition, basis) > RasterLimits.CameraNear;

    /// <summary>Computes a point depth along the camera forward axis.</summary>
    public static double CameraDepth(Vec3 point, Vec3 cameraPosition, CameraBasis basis)
    {
        double depth = (point - cameraPosition).Dot(basis.Forward);
        return double.IsFinite(depth) ? depth : double.NegativeInfinity;
    }
}
