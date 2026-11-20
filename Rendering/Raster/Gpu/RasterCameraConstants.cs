// File: Rendering/Raster/Gpu/RasterCameraConstants.cs
// Purpose: Per-frame camera uniform block for the raster shaders.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RasterCameraConstants
{
    public readonly Vector4 CameraPosition;
    public readonly Vector4 CameraForward;
    public readonly Vector4 CameraRight;
    public readonly Vector4 CameraUp;
    public readonly Vector4 Projection; // x=aspect, y=tanHalfFov, z=near, w=far
    public readonly Vector4 Counts;     // x=lightCount, y=textureCount, z=debugMode, w=materialCount

    /// <summary>Initializes camera projection constants from position, basis, resolution, and counts.</summary>
    public RasterCameraConstants(
        Vec3 position, CameraBasis basis, int width, int height,
        int lightCount, int textureCount, int debugMode, int materialCount, double farPlane)
    {
        CameraPosition = position.ToVector4(1.0f);
        CameraForward = basis.Forward.ToVector4(0.0f);
        CameraRight = basis.Right.ToVector4(0.0f);
        CameraUp = basis.Up.ToVector4(0.0f);
        float aspect = width / (float)Math.Max(1, height);
        float tanHalfFov = (float)Math.Tan((RasterLimits.CameraFovDegrees * Math.PI / 180.0) * 0.5);
        Projection = new Vector4(aspect, tanHalfFov, (float)RasterLimits.CameraNear,
            (float)Math.Clamp(farPlane, RasterLimits.CameraNear + 1.0, RasterLimits.CameraFar));
        Counts = new Vector4(
            Math.Clamp(lightCount, 0, RasterLimits.MaxGpuLights),
            Math.Max(0, textureCount),
            Math.Max(0, debugMode),
            Math.Max(0, materialCount));
    }
}
