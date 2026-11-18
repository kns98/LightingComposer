// -----------------------------------------------------------------------------
// File: Scene/ObjectLibraryPlugin.cs
// Purpose: Discoverable object-library plugin contract.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Provides insertable authored objects from a separately built object-library DLL.</summary>
public interface IObjectLibraryPlugin
{
    string LibraryId { get; }
    string DisplayName { get; }
    IReadOnlyList<string> ObjectNames { get; }
    /// <summary>Returns true when the library holds an object with the given name.</summary>
    bool Contains(string objectName);
    /// <summary>Inserts the named library object into the scene as a new group.</summary>
    SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName);
}
