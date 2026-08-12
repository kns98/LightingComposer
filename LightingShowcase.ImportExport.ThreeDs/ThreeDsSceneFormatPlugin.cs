/*
 * This adapter registers THREEDS with the format registry. The registry sees a uniform `SceneFormat` capability,
 * while this assembly remains responsible for constructing the actual THREEDS loader/saver; that keeps the core
 * scene layer free of hard-coded format dependencies.
 */
using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.ThreeDs;

// ThreeDsSceneFormatPlugin is the adapter that registers this assembly’s capability with a shared registry, keeping
// discovery separate from the concrete implementation.
public sealed class ThreeDsSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "3ds";
    public string DisplayName => "3DS";
    public IReadOnlyList<string> Extensions => new[] { ".3ds" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => Array.Empty<string>();

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        ThreeDsSceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) => ThreeDsSceneSaver.Save(scene, filePath);
}
