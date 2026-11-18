// -----------------------------------------------------------------------------
// File: Scene/SceneTextureResources.cs
// Purpose: Scene texture discovery.
//
// Discovers every texture referenced by scene materials for export and cache management.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Discovers every texture referenced by scene materials.</summary>
public static class SceneTextureResources
{
    /// <summary>Enumerates every distinct texture referenced by scene materials.</summary>
    public static IReadOnlyList<TextureMap> Enumerate(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        HashSet<TextureMap> seen = new(ReferenceEqualityComparer.Instance);
        List<TextureMap> textures = new();
        foreach (SceneObjectGroup root in scene.ObjectGroups)
            CollectGroup(root, seen, textures);
        return textures;
    }

    /// <summary>Collects textures from one group and its children.</summary>
    private static void CollectGroup(SceneObjectGroup group, HashSet<TextureMap> seen, List<TextureMap> textures)
    {
        CollectMaterial(group.ColorOverride, seen, textures);
        foreach (Triangle triangle in group.LocalTriangles)
            CollectMaterial(triangle.Material, seen, textures);
        foreach (SceneObjectGroup child in group.Children)
            CollectGroup(child, seen, textures);
    }

    /// <summary>Collects every texture assigned to one material.</summary>
    private static void CollectMaterial(Material? material, HashSet<TextureMap> seen, List<TextureMap> textures)
    {
        if (material == null) return;
        Add(material.Texture, seen, textures);
        Add(material.EmissiveTexture, seen, textures);
        Add(material.MetallicRoughnessTexture, seen, textures);
        Add(material.NormalTexture, seen, textures);
        Add(material.OcclusionTexture, seen, textures);
        Add(material.TransmissionTexture, seen, textures);
    }

    /// <summary>Adds a texture to the result when it was not already seen.</summary>
    private static void Add(TextureMap? texture, HashSet<TextureMap> seen, List<TextureMap> textures)
    {
        if (texture != null && seen.Add(texture))
            textures.Add(texture);
    }
}
