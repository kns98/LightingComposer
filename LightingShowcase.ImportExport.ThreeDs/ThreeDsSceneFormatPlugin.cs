// -----------------------------------------------------------------------------
// File: LightingShowcase.ImportExport.ThreeDs/ThreeDsSceneFormatPlugin.cs
// Purpose: 3DS format plugin registration.
//
// Registers the 3D Studio importer and exporter with the scene format registry.
// -----------------------------------------------------------------------------

using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.ThreeDs;

public sealed class ThreeDsSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "3ds";
    public string DisplayName => "3DS";
    public IReadOnlyList<string> Extensions => new[] { ".3ds" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => Array.Empty<string>();

    /// <summary>Imports a 3DS file into the given scene.</summary>
    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        ThreeDsSceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress);

    /// <summary>Exports the given scene to a 3DS file.</summary>
    public void Export(Scene scene, string filePath, SceneSaveOptions options) => ThreeDsSceneSaver.Save(scene, filePath);
}
