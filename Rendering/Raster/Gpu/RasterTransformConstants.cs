// File: Rendering/Raster/Gpu/RasterTransformConstants.cs
// Purpose: Gizmo transform-preview uniform block for the raster vertex shader.

using System.Numerics;
using System.Runtime.InteropServices;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RasterTransformConstants
{
    public readonly Vector4 PivotEnabled;
    public readonly Vector4 Translation;
    public readonly Vector4 Rotation;
    public readonly Vector4 Scale;

    /// <summary>Initializes transform preview constants, using identity when no preview applies.</summary>
    public RasterTransformConstants(VulkanRasterTransformPreview? preview)
    {
        if (preview == null || preview.IsIdentity)
        {
            PivotEnabled = Vector4.Zero;
            Translation = Vector4.Zero;
            Rotation = Vector4.Zero;
            Scale = Vector4.One;
            return;
        }

        PivotEnabled = preview.Pivot.ToVector4(1.0f);
        Translation = preview.Position.ToVector4(0.0f);
        Rotation = preview.Rotation.ToVector4(0.0f);
        Scale = preview.Scale.ToVector4(0.0f);
    }
}
