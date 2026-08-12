namespace LightingShowcase.SceneGraph;

/// <summary>Discovers every texture referenced by scene materials.</summary>
public static class SceneTextureResources
{
    public static IReadOnlyList<TextureMap> Enumerate(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        HashSet<TextureMap> seen = new(ReferenceEqualityComparer.Instance);
        List<TextureMap> textures = new();
        foreach (SceneObjectGroup root in scene.ObjectGroups)
            CollectGroup(root, seen, textures);
        return textures;
    }

    private static void CollectGroup(SceneObjectGroup group, HashSet<TextureMap> seen, List<TextureMap> textures)
    {
        CollectMaterial(group.ColorOverride, seen, textures);
        foreach (Triangle triangle in group.LocalTriangles)
            CollectMaterial(triangle.Material, seen, textures);
        foreach (SceneObjectGroup child in group.Children)
            CollectGroup(child, seen, textures);
    }

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

    private static void Add(TextureMap? texture, HashSet<TextureMap> seen, List<TextureMap> textures)
    {
        if (texture != null && seen.Add(texture))
            textures.Add(texture);
    }
}
