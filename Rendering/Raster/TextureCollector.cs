// File: Rendering/Raster/TextureCollector.cs
// Purpose: Gathers the deduplicated texture set referenced by a scene.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class TextureCollector
{
    /// <summary>Collects every texture referenced by scene materials, largest first.</summary>
    public List<TextureMap> Collect(Scene scene)
    {
        List<TextureMap> textures = new();
        HashSet<TextureMap> seen = new();
        foreach (Triangle triangle in scene.Triangles)
        {
            Add(triangle.Material.Texture);
            Add(triangle.Material.MetallicRoughnessTexture);
            Add(triangle.Material.NormalTexture);
            Add(triangle.Material.EmissiveTexture);
            Add(triangle.Material.OcclusionTexture);
            Add(triangle.Material.TransmissionTexture);
        }

        return textures
            .OrderByDescending(texture => Math.Max(texture.Width, texture.Height))
            .ThenByDescending(texture => checked((long)Math.Max(1, texture.Width) * Math.Max(1, texture.Height)))
            .ToList();

        void Add(TextureMap? texture)
        {
            if (texture != null && seen.Add(texture))
                textures.Add(texture);
        }
    }
}
