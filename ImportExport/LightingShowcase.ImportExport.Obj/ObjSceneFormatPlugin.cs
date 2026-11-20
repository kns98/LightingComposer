// -----------------------------------------------------------------------------
// File: ImportExport/LightingShowcase.ImportExport.Obj/ObjSceneFormatPlugin.cs
// Purpose: OBJ format plugin registration.
//
// Registers the Wavefront OBJ importer and exporter with the scene format registry.
// -----------------------------------------------------------------------------

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

    /// <summary>Imports a Wavefront OBJ file into the scene.</summary>
    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        ObjSceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress, options.SimplifyKeepFraction);

    /// <summary>Exports the scene to a Wavefront OBJ file.</summary>
    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        ObjSceneSaver.Save(scene, filePath, options);
}
