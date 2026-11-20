// File: Rendering/Raster/CameraPlanes.cs
// Purpose: Camera far-plane derivation from cached scene bounds.

using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal static class CameraPlanes
{
    // The far plane is picked from the current camera view (instead of a fixed
    // 5000 units) so depth precision stays close to the CPU shadow rasterizer,
    // which stores camera-space depth directly in a double z-buffer.
    /// <summary>Derives the camera far plane from cached bounds and camera distance.</summary>
    public static double CachedFarPlane(Vec3 boundingCenter, double boundingRadius, Vec3 cameraPosition) =>
        Math.Clamp((boundingCenter - cameraPosition).Length() + boundingRadius * 1.1 + 1.0, 10.0, RasterLimits.CameraFar);
}
