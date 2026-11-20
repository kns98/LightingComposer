// File: Rendering/Compute/Gpu/GpuTextureInfo.cs
// Purpose: GPU texture location metadata for the packed pixel buffer.

using System.Numerics;
using System.Runtime.InteropServices;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuTextureInfo
{
    public readonly Vector4 Data; // x=pixel offset, y=width, z=height, w=reserved

    /// <summary>Initializes GPU texture location metadata.</summary>
    public GpuTextureInfo(int pixelOffset, int width, int height)
    {
        Data = new Vector4(pixelOffset, width, height, 0.0f);
    }
}
