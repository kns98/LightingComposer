/*
 * This adapter registers GLTF with the format registry. The registry sees a uniform `SceneFormat` capability,
 * while this assembly remains responsible for constructing the actual GLTF loader/saver; that keeps the core
 * scene layer free of hard-coded format dependencies.
 *
 * `GltfSceneFormatPlugin` is the adapter that registers this assembly’s capability with a shared registry,
 * keeping discovery separate from the concrete implementation.
 *
 * `Extensions` is derived rather than separately stored: it evaluates `new[] { , }`. Keeping the value computed
 * from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CanImport` is derived rather than separately stored: it evaluates `true`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CanExport` is derived rather than separately stored: it evaluates `true`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CarriesLights` is derived rather than separately stored: it evaluates `true`. Keeping the value computed from
 * its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `ExportVariants` is derived rather than separately stored: it evaluates `new[] { , }`. Keeping the value
 * computed from its source fields prevents a second cached flag/value from drifting out of sync.
 */
using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Gltf;

public sealed class GltfSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "gltf-glb";
    public string DisplayName => "glTF/GLB";
    public IReadOnlyList<string> Extensions => new[] { ".gltf", ".glb" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => true;
    public IReadOnlyList<string> ExportVariants => new[] { "gltf", "glb" };

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        GltfSceneIO.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress, options.SimplifyKeepFraction);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        GltfSceneIO.Save(scene, filePath, binary: string.Equals(Path.GetExtension(filePath), ".glb", StringComparison.OrdinalIgnoreCase) || string.Equals(options.Variant, "glb", StringComparison.OrdinalIgnoreCase), options: options);
}
