/*
 * Object-library definitions generate scene geometry from named, authored parameters. Keeping those parameters
 * attached to the generated object is important: a cube with width/height/depth is still editable as a cube until
 * a topology edit deliberately converts it into ordinary mesh geometry.
 */
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

// BuiltInObjectLibraryPlugin is the adapter that registers this assembly’s capability with a shared registry,
// keeping discovery separate from the concrete implementation.
public sealed class BuiltInObjectLibraryPlugin : IObjectLibraryPlugin
{
    public string LibraryId => "builtin-objects";
    public string DisplayName => "Built-in Objects";
    public IReadOnlyList<string> ObjectNames => ReadyMadeObjectLibrary.Names;
    public bool Contains(string objectName) => ReadyMadeObjectLibrary.Contains(objectName);
    public SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName) => ReadyMadeObjectLibrary.Insert(scene, materials, objectName);
}
