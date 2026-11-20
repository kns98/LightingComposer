// File: Rendering/Compute/Gpu/GpuBvhNode.cs
// Purpose: GPU bounding volume hierarchy node for traversal.

using System.Numerics;
using System.Runtime.InteropServices;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuBvhNode
{
    public readonly Vector4 BoundsMin;
    public readonly Vector4 BoundsMax;
    // x=left child or first triangle, y=triangle count (0 for internal), z=right child, w=reserved
    public readonly Vector4 Data;

    /// <summary>Initializes a GPU bounding volume hierarchy node.</summary>
    public GpuBvhNode(Vector3 boundsMin, Vector3 boundsMax, int leftOrFirst, int triangleCount, int rightChild)
    {
        BoundsMin = new Vector4(boundsMin, 0.0f);
        BoundsMax = new Vector4(boundsMax, 0.0f);
        Data = new Vector4(leftOrFirst, triangleCount, rightChild, 0.0f);
    }
}
