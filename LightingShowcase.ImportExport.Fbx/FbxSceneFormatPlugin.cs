/*
 * This adapter registers FBX with the format registry. The registry sees a uniform `SceneFormat` capability,
 * while this assembly remains responsible for constructing the actual FBX loader/saver; that keeps the core scene
 * layer free of hard-coded format dependencies.
 *
 * `FbxSceneFormatPlugin` is the adapter that registers this assembly’s capability with a shared registry, keeping
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
 * `ExportVariants` is derived rather than separately stored: it evaluates `new[] { , }`. Keeping the value
 * computed from its source fields prevents a second cached flag/value from drifting out of sync.
 */
using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Fbx;

public sealed class FbxSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "fbx";
    public string DisplayName => "FBX";
    public IReadOnlyList<string> Extensions => new[] { ".fbx" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => new[] { "binary", "ascii" };

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        FbxSceneIO.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) => FbxSceneIO.Save(scene, filePath, options.Variant);
}
