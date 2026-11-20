// File: Rendering/Raster/SceneBounds.cs
// Purpose: Bounding sphere of all scene triangle vertices.

using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed record SceneBounds(Vec3 Center, double Radius)
{
    /// <summary>Computes the scene bounding center and radius from all triangle vertices.</summary>
    public static SceneBounds Compute(Scene scene)
    {
        if (scene.Triangles.Count == 0)
            return new SceneBounds(Vec3.Zero, 10);

        Vec3 min = scene.Triangles[0].A;
        Vec3 max = min;
        foreach (Triangle triangle in scene.Triangles)
        {
            Include(triangle.A);
            Include(triangle.B);
            Include(triangle.C);
        }

        Vec3 center = (min + max) / 2.0;
        return new SceneBounds(center, Math.Max(1.0, (max - center).Length()));

        void Include(Vec3 point)
        {
            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }
    }
}
