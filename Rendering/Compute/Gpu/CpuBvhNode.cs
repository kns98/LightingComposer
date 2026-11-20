// File: Rendering/Compute/Gpu/CpuBvhNode.cs
// Purpose: CPU bounding volume hierarchy node with GPU conversion.

using System.Numerics;
using System.Runtime.InteropServices;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CpuBvhNode
{
    public readonly Vector3 BoundsMin;
    public readonly Vector3 BoundsMax;
    public readonly int LeftOrFirst;
    public readonly int TriangleCount;
    public readonly int RightChild;

    /// <summary>Initializes a CPU bounding volume hierarchy node.</summary>
    public CpuBvhNode(Vector3 boundsMin, Vector3 boundsMax, int leftOrFirst, int triangleCount, int rightChild)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        LeftOrFirst = leftOrFirst;
        TriangleCount = triangleCount;
        RightChild = rightChild;
    }

    /// <summary>Converts this CPU node into its GPU representation.</summary>
    public GpuBvhNode ToGpu() => new(BoundsMin, BoundsMax, LeftOrFirst, TriangleCount, RightChild);
}
