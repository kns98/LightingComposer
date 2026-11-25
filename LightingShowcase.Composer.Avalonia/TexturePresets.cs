// Built-in CC0 texture library for the Textures tab: one albedo-centered entry
// per shipped material with linked normal/roughness maps. Hosts pass the
// resolved file path into the existing SetObjectTexture path so decode, undo,
// and cache invalidation are reused untouched.
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record TexturePreset(
    string Name,
    string Category,
    string AlbedoFile,
    MaterialTextureSlot DefaultSlot,
    string? NormalFile,
    string? RoughnessFile,
    string Summary);

internal static class TexturePresetLibrary
{
    public static readonly IReadOnlyList<TexturePreset> All = new TexturePreset[]
    {
        new("Red Brick Wall", "Brick", "textures/brick_wall_001_albedo_1k.jpg", MaterialTextureSlot.BaseColor, "textures/brick_wall_001_normal_1k.jpg", "textures/brick_wall_001_roughness_1k.jpg", "Clean red brick wall; Poly Haven Brick Wall 001 (CC0)."),
        new("Concrete Wall", "Concrete", "textures/concrete_wall_001_albedo_1k.jpg", MaterialTextureSlot.BaseColor, "textures/concrete_wall_001_normal_1k.jpg", "textures/concrete_wall_001_roughness_1k.jpg", "Plain cast concrete wall; Poly Haven Concrete Wall 001 (CC0)."),
        new("Wood Planks", "Wood", "textures/wood_planks_albedo_1k.jpg", MaterialTextureSlot.BaseColor, "textures/wood_planks_normal_1k.jpg", "textures/wood_planks_roughness_1k.jpg", "Rustic wood plank floor; Poly Haven Wood Planks (CC0)."),
        new("White Marble", "Stone", "textures/marble_01_albedo_1k.jpg", MaterialTextureSlot.BaseColor, "textures/marble_01_normal_1k.jpg", "textures/marble_01_roughness_1k.jpg", "Polished white marble; Poly Haven Marble 01 (CC0)."),
        new("Denim Fabric", "Fabric", "textures/denim_fabric_albedo_1k.jpg", MaterialTextureSlot.BaseColor, "textures/denim_fabric_normal_1k.jpg", "textures/denim_fabric_roughness_1k.jpg", "Woven blue denim; Poly Haven Denim Fabric (CC0)."),
        new("Metal Plate", "Metal", "textures/metal_plate_albedo_1k.jpg", MaterialTextureSlot.BaseColor, "textures/metal_plate_normal_1k.jpg", "textures/metal_plate_roughness_1k.jpg", "Brushed metal plate; Poly Haven Metal Plate (CC0)."),
    };

    /// <summary>Resolves a library-relative path against the working directory, the app directory, or the repo root.</summary>
    public static string? ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        string? direct = TextureMap.ResolveFilePath(relativePath);
        if (direct != null)
            return direct;

        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory != null; i++)
        {
            string candidate = Path.Combine(directory, relativePath);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }
}
