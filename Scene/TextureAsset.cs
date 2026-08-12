/*
 * Surface appearance is normalized here so importers, editor controls, and every renderer use the same meaning for
 * colors, PBR values, alpha behavior, texture slots, UV transforms, and resource identity. That shared model is
 * what makes a material edited in the UI render consistently across backends.
 */
namespace LightingShowcase.SceneGraph;

/// <summary>Texture metadata independent of any renderer-specific bitmap/cache object.</summary>
public sealed class TextureAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Texture";
    public string? SourcePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    // IsGenerated is a read-only predicate over the object’s existing state; it exists so callers share one exact
    // condition when enabling commands or deciding whether an operation is applicable.
    public bool IsGenerated { get; set; }

    public static TextureAsset FromTextureMap(TextureMap texture)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        return new TextureAsset
        {
            Id = string.IsNullOrWhiteSpace(texture.Name) ? Guid.NewGuid().ToString("N") : texture.Name,
            Name = string.IsNullOrWhiteSpace(texture.Name) ? "Texture" : texture.Name,
            SourcePath = texture.SourcePath,
            Width = texture.Width,
            Height = texture.Height,
            IsGenerated = texture.IsBuiltInChecker
        };
    }
}
