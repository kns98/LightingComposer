// File: Rendering/Compute/Bvh/TriangleCentroidComparer.cs
// Purpose: Sorts triangle indices by centroid along the active split axis.

using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class TriangleCentroidComparer : IComparer<int>
{
    private readonly IReadOnlyList<Triangle> triangles;

    public int Axis { get; set; }

    /// <summary>Initializes the comparer with the scene triangles.</summary>
    public TriangleCentroidComparer(IReadOnlyList<Triangle> triangles)
    {
        this.triangles = triangles;
    }

    /// <summary>Compares two triangles by centroid along the active axis.</summary>
    public int Compare(int leftIndex, int rightIndex)
    {
        Vec3 left = triangles[leftIndex].Centroid;
        Vec3 right = triangles[rightIndex].Centroid;
        double leftValue = Axis == 0 ? left.X : Axis == 1 ? left.Y : left.Z;
        double rightValue = Axis == 0 ? right.X : Axis == 1 ? right.Y : right.Z;
        return leftValue.CompareTo(rightValue);
    }
}
