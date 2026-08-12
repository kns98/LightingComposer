/*
 * This adapter registers PROPXML with the format registry. The registry sees a uniform `SceneFormat` capability,
 * while this assembly remains responsible for constructing the actual PROPXML loader/saver; that keeps the core
 * scene layer free of hard-coded format dependencies.
 *
 * `PropXmlSceneFormatPlugin` is the adapter that registers this assembly’s capability with a shared registry,
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
 * `ExportVariants` is derived rather than separately stored: it evaluates `Array.Empty<string>()`. Keeping the
 * value computed from its source fields prevents a second cached flag/value from drifting out of sync.
 */
using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.PropXml;

public sealed class PropXmlSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "prop-xml";
    public string DisplayName => "Prop XML";
    public IReadOnlyList<string> Extensions => new[] { ".xml", ".prop.xml" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => true;
    public IReadOnlyList<string> ExportVariants => Array.Empty<string>();

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options)
    {
        PropXmlSceneLoader.LoadIntoScene(scene, filePath);
        scene.RebuildWorldGeometry();
        int triangles = scene.ObjectGroups.SelectMany(g => g.SelfAndDescendants()).Sum(g => g.LocalTriangles.Count);
        return new ObjLoadResult(filePath, 0, triangles, triangles);
    }

    public void Export(Scene scene, string filePath, SceneSaveOptions options) => PropXmlSceneSaver.Save(scene, filePath, options);
}
