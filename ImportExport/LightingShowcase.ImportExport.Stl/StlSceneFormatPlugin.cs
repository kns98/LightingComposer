// -----------------------------------------------------------------------------
// File: ImportExport/LightingShowcase.ImportExport.Stl/StlSceneFormatPlugin.cs
// Purpose: STL format plugin registration.
//
// Registers the STL importer and exporter with the scene format registry.
// -----------------------------------------------------------------------------

using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Stl;

public sealed class StlSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "stl";
    public string DisplayName => "STL";
    public IReadOnlyList<string> Extensions => new[] { ".stl" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => new[] { "binary", "ascii" };

    /// <summary>Imports STL geometry into the scene with the given load options.</summary>
    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        StlSceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress);

    /// <summary>Exports the scene to STL in binary or ASCII form.</summary>
    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        StlSceneSaver.Save(scene, filePath, binary: !string.Equals(options.Variant, "ascii", StringComparison.OrdinalIgnoreCase));
}
