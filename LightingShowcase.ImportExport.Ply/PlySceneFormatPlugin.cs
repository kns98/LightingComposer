// -----------------------------------------------------------------------------
// File: LightingShowcase.ImportExport.Ply/PlySceneFormatPlugin.cs
// Purpose: PLY format plugin registration.
//
// Registers the PLY importer and exporter with the scene format registry.
// -----------------------------------------------------------------------------

using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Ply;

public sealed class PlySceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "ply";
    public string DisplayName => "PLY";
    public IReadOnlyList<string> Extensions => new[] { ".ply" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => new[] { "binary", "ascii" };

    /// <summary>Imports a PLY file into the scene.</summary>
    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        PlySceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress);

    /// <summary>Exports the scene to a PLY file, using ascii output when the ascii variant is requested.</summary>
    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        PlySceneSaver.Save(scene, filePath, binary: !string.Equals(options.Variant, "ascii", StringComparison.OrdinalIgnoreCase));
}
