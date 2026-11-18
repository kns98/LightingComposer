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
    public bool Contains(string objectName) => ReadyMadeObjectLibrary.Contains(objectName);
    public SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName) => ReadyMadeObjectLibrary.Insert(scene, materials, objectName);
}
