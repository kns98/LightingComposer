/*
 * Object-library definitions generate scene geometry from named, authored parameters. Keeping those parameters
 * attached to the generated object is important: a cube with width/height/depth is still editable as a cube until
 * a topology edit deliberately converts it into ordinary mesh geometry.
 *
 * `BuiltInObjectLibraryPlugin` is the adapter that registers this assembly’s capability with a shared registry,
 * keeping discovery separate from the concrete implementation.
 *
 * `ObjectNames` is derived rather than separately stored: it evaluates `ReadyMadeObjectLibrary.Names`. Keeping
 * the value computed from its source fields prevents a second cached flag/value from drifting out of sync.
 */
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
