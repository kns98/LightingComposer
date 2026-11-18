// -----------------------------------------------------------------------------
// File: LightingShowcase.ObjectLibrary.BuiltIns/BuiltInObjectLibraryPlugin.cs
// Purpose: Built-in primitive library registration.
//
// Registers the procedural primitive set with the object library registry.
// -----------------------------------------------------------------------------

using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

public sealed class BuiltInObjectLibraryPlugin : IObjectLibraryPlugin
{
    public string LibraryId => "builtin-objects";
    public string DisplayName => "Built-in Objects";
    public IReadOnlyList<string> ObjectNames => ReadyMadeObjectLibrary.Names;
    /// <summary>Returns true when the catalog holds an object with the given name.</summary>
    public bool Contains(string objectName) => ReadyMadeObjectLibrary.Contains(objectName);
    /// <summary>Inserts the named built-in object into the scene and returns its group.</summary>
    public SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName) => ReadyMadeObjectLibrary.Insert(scene, materials, objectName);
}
