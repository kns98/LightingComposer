// -----------------------------------------------------------------------------
// File: ImportExport/LightingShowcase.ImportExport.Gltf/GltfSceneFormatPlugin.cs
// Purpose: glTF format plugin registration.
//
// Registers the glTF and GLB importer and exporter with the scene format registry.
// -----------------------------------------------------------------------------

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

    /// <summary>Imports a glTF or GLB file into the scene with the given load options.</summary>
    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        GltfSceneIO.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress, options.SimplifyKeepFraction);

    /// <summary>Exports the scene to glTF or GLB based on the file extension or variant.</summary>
    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        GltfSceneIO.Save(scene, filePath, binary: string.Equals(Path.GetExtension(filePath), ".glb", StringComparison.OrdinalIgnoreCase) || string.Equals(options.Variant, "glb", StringComparison.OrdinalIgnoreCase), options: options);
}
