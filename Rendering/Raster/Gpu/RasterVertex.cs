// File: Rendering/Raster/Gpu/RasterVertex.cs
// Purpose: GPU vertex layout for the Vulkan rasterizer.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RasterVertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Normal;
    public readonly Vector2 Uv;
    public readonly float MaterialIndex;

    /// <summary>Initializes a raster vertex with position, normalized normal, UV, and material index.</summary>
    public RasterVertex(Vec3 position, Vec3 normal, Vec2 uv, int materialIndex)
    {
        Position = position.ToVector3();
        Normal = normal.Normalize().ToVector3();
        Uv = new Vector2((float)uv.U, (float)uv.V);
        MaterialIndex = materialIndex;
    }
}
