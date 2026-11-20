// File: Rendering/Shadow/ShadowMapBuilder.cs
// Purpose: Builds one shadow map per shadow-casting light.

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class ShadowMapBuilder
{
    private const int ShadowMapSize = 512;
    private const int MaxShadowCastingLights = 4;

    /// <summary>Builds one shadow map per shadow-casting light.</summary>
    public List<ShadowMap> Build(Scene scene, CancellationToken cancellationToken)
    {
        Aabb bounds = scene.GetSceneBounds() ?? new Aabb(new Vec3(-2, -2, -2), new Vec3(2, 2, 2));
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        double radius = Math.Max(0.35, (bounds.Max - bounds.Min).Length() * 0.5);

        List<ShadowMap> maps = new();
        foreach (SceneLight light in scene.Lights.Where(l => l.Enabled && l.CastsShadow).Take(MaxShadowCastingLights))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShadowProjection projection = ShadowProjectionSelector.Create(light, center, radius);
            ShadowMap map = new(light.Id, ShadowMapSize, projection);
            foreach (Triangle tri in scene.Triangles)
                ShadowTriangleRasterizer.Rasterize(map, tri);
            maps.Add(map);
        }

        return maps;
    }
}
