// File: Rendering/Raster/RasterLimits.cs
// Purpose: Fixed GPU and camera limits shared by the rasterizer.

namespace LightingShowcase.Rendering;

internal static class RasterLimits
{
    internal const int MaxGpuLights = 32;
    internal const double CameraFovDegrees = 72.0;
    internal const double CameraNear = 0.035;
    internal const double CameraFar = 5000.0;
}
