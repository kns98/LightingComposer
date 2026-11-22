// File: Rendering/Raster/Gpu/RasterBackgroundConstants.cs
// Purpose: Top and bottom background gradient colors for the background pass.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RasterBackgroundConstants
{
    public readonly Vector4 Top;
    public readonly Vector4 Bottom;
    public readonly Vector4 Exposure; // x=exposure

    /// <summary>Initializes background gradient constants from top and bottom colors.</summary>
    public RasterBackgroundConstants(Vec3 top, Vec3 bottom, double exposure = 1.0)
    {
        Top = top.ToVector4(1.0f);
        Bottom = bottom.ToVector4(1.0f);
        Exposure = new Vector4((float)Math.Max(0.0, exposure), 0.0f, 0.0f, 0.0f);
    }
}
