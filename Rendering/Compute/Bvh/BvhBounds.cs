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
            Vector3 triMin = Vector3.Min(triangle.A.ToVector3(), Vector3.Min(triangle.B.ToVector3(), triangle.C.ToVector3()));
            Vector3 triMax = Vector3.Max(triangle.A.ToVector3(), Vector3.Max(triangle.B.ToVector3(), triangle.C.ToVector3()));
            boundsMin = Vector3.Min(boundsMin, triMin - pad);
            boundsMax = Vector3.Max(boundsMax, triMax + pad);
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
            Vector3 triMin = Vector3.Min(tri.A.ToVector3(), Vector3.Min(tri.B.ToVector3(), tri.C.ToVector3()));
            Vector3 triMax = Vector3.Max(tri.A.ToVector3(), Vector3.Max(tri.B.ToVector3(), tri.C.ToVector3()));
            globalMin = Vector3.Min(globalMin, triMin);
            globalMax = Vector3.Max(globalMax, triMax);
        }

        float sceneDiagonal = Vector3.Distance(globalMin, globalMax);
        if (!float.IsFinite(sceneDiagonal) || sceneDiagonal <= 0.0f)
            return 1e-4f;

        return Math.Clamp(sceneDiagonal * 1e-6f, 1e-4f, 0.01f);
    }
}
