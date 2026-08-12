/*
 * This adapter registers OBJ with the format registry. The registry sees a uniform `SceneFormat` capability,
 * while this assembly remains responsible for constructing the actual OBJ loader/saver; that keeps the core scene
 * layer free of hard-coded format dependencies.
 *
 * `ObjSceneFormatPlugin` is the adapter that registers this assembly’s capability with a shared registry, keeping
 * discovery separate from the concrete implementation.
 *
 * `Extensions` is derived rather than separately stored: it evaluates `new[] { }`. Keeping the value computed
 * from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CanImport` is derived rather than separately stored: it evaluates `true`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CanExport` is derived rather than separately stored: it evaluates `true`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CarriesLights` is derived rather than separately stored: it evaluates `false`. Keeping the value computed from
 * its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `ExportVariants` is derived rather than separately stored: it evaluates `Array.Empty<string>()`. Keeping the
 * value computed from its source fields prevents a second cached flag/value from drifting out of sync.
 */
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Obj;

public sealed class ObjSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "obj";
    public string DisplayName => "Wavefront OBJ";
    public IReadOnlyList<string> Extensions => new[] { ".obj" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => Array.Empty<string>();

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        ObjSceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress, options.SimplifyKeepFraction);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        ObjSceneSaver.Save(scene, filePath, options);
}
