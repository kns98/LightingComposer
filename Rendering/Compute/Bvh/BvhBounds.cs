// File: Rendering/Compute/Bvh/BvhBounds.cs
// Purpose: Padded leaf, centroid, and scene bounds math for BVH construction.

using System.Numerics;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal static class BvhBounds
{
    /// <summary>Computes padded bounds for a leaf triangle range.</summary>
    public static void LeafBounds(
        IReadOnlyList<Triangle> triangles, int[] triangleIndices, int start, int count,
        Vector3 pad, out Vector3 boundsMin, out Vector3 boundsMax)
    {
        boundsMin = new Vector3(float.PositiveInfinity);
        boundsMax = new Vector3(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            Triangle triangle = triangles[triangleIndices[i]];
            Vector3 a = triangle.A.ToVector3();
            Vector3 b = triangle.B.ToVector3();
            Vector3 c = triangle.C.ToVector3();
            boundsMin = Vector3.Min(boundsMin, Vector3.Min(a, Vector3.Min(b, c)) - pad);
            boundsMax = Vector3.Max(boundsMax, Vector3.Max(a, Vector3.Max(b, c)) + pad);
        }
    }

    /// <summary>Computes centroid bounds for a triangle range.</summary>
    public static void CentroidBounds(
        IReadOnlyList<Triangle> triangles, int[] triangleIndices, int start, int count,
        out Vector3 centroidMin, out Vector3 centroidMax)
    {
        centroidMin = new Vector3(float.PositiveInfinity);
        centroidMax = new Vector3(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            Vector3 centroid = triangles[triangleIndices[i]].Centroid.ToVector3();
            centroidMin = Vector3.Min(centroidMin, centroid);
            centroidMax = Vector3.Max(centroidMax, centroid);
        }
    }

    /// <summary>Computes the conservative bounds padding for the scene.</summary>
    public static float Padding(Scene scene)
    {
        if (scene.Triangles.Count == 0)
            return 1e-4f;

        Vector3 globalMin = new(float.PositiveInfinity);
        Vector3 globalMax = new(float.NegativeInfinity);
        foreach (Triangle tri in scene.Triangles)
        {
            Vector3 a = new((float)tri.A.X, (float)tri.A.Y, (float)tri.A.Z);
            Vector3 b = new((float)tri.B.X, (float)tri.B.Y, (float)tri.B.Z);
            Vector3 c = new((float)tri.C.X, (float)tri.C.Y, (float)tri.C.Z);
            globalMin = Vector3.Min(globalMin, Vector3.Min(a, Vector3.Min(b, c)));
            globalMax = Vector3.Max(globalMax, Vector3.Max(a, Vector3.Max(b, c)));
        }

        float sceneDiagonal = Vector3.Distance(globalMin, globalMax);
        if (!float.IsFinite(sceneDiagonal) || sceneDiagonal <= 0.0f)
            return 1e-4f;

        return Math.Clamp(sceneDiagonal * 1e-6f, 1e-4f, 0.01f);
    }
}
