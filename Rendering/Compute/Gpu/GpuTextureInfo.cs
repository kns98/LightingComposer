// File: Rendering/Compute/Gpu/GpuTextureInfo.cs
// Purpose: GPU texture location metadata for the packed pixel buffer.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuTextureInfo
{
    public readonly Vector4 Data; // x=pixel offset, y=width, z=height, w=reserved
    public readonly Vector4 Transform; // xy=offset, zw=scale
    public readonly Vector4 Address; // x=wrapU, y=wrapV, z=rotation, w=reserved

    /// <summary>Initializes GPU texture location, transform, and addressing metadata.</summary>
    public GpuTextureInfo(int pixelOffset, int width, int height, TextureMap? texture = null)
    {
        Data = new Vector4(pixelOffset, width, height, 0.0f);
        Transform = texture == null
            ? new Vector4(0.0f, 0.0f, 1.0f, 1.0f)
            : new Vector4((float)texture.OffsetU, (float)texture.OffsetV, (float)texture.ScaleU, (float)texture.ScaleV);
        Address = texture == null
            ? new Vector4(0.0f, 0.0f, 0.0f, 0.0f)
            : new Vector4(
                RasterTexturePlacement.AddressModeCode(texture.WrapU),
                RasterTexturePlacement.AddressModeCode(texture.WrapV),
                (float)texture.Rotation,
                0.0f);
    }
}
