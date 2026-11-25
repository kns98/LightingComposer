// -----------------------------------------------------------------------------
// File: Scene/SceneMaterialEntry.cs
// Purpose: Named material stored in the scene's material library.
//
// User-saved materials (values and base color, no texture assignments) that
// travel with the binary scene file and can be re-applied to any object.
// -----------------------------------------------------------------------------
namespace LightingShowcase.SceneGraph;

/// <summary>Named textureless material saved in the scene material library.</summary>
public sealed class SceneMaterialEntry
{
    public string Name { get; }
    public Material Material { get; }

    /// <summary>Creates a library entry with the given name and material.</summary>
    public SceneMaterialEntry(string name, Material material)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A material name is required.", nameof(name));
        Name = name.Trim();
        Material = material ?? throw new ArgumentNullException(nameof(material));
    }

    /// <summary>Returns the entry name for list display.</summary>
    public override string ToString() => Name;
}
