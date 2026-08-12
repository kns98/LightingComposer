/*
 * This is an extensibility seam. Callers discover capabilities through a registry/interface instead of referencing
 * every concrete format or object-library assembly, allowing plugins to be added while the core scene/editor code
 * remains unchanged.
 */
namespace LightingShowcase.SceneGraph;

// IObjectLibraryPlugin defines a capability boundary: callers depend on the contract rather than the concrete
// plugin/backend implementing it. New implementations can therefore participate without changing the core caller.
/// <summary>Provides insertable authored objects from a separately built object-library DLL.</summary>
public interface IObjectLibraryPlugin
{
    string LibraryId { get; }
    string DisplayName { get; }
    IReadOnlyList<string> ObjectNames { get; }
    bool Contains(string objectName);
    SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName);
}
