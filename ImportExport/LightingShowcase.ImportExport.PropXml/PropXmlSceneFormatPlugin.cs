// -----------------------------------------------------------------------------
// File: ImportExport/LightingShowcase.ImportExport.PropXml/PropXmlSceneFormatPlugin.cs
// Purpose: Property XML format plugin registration.
//
// Registers the native .prop.xml importer and exporter with the scene format registry.
// -----------------------------------------------------------------------------

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

    /// <summary>Imports a prop XML file into the scene and reports the triangle count.</summary>
    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options)
    {
        PropXmlSceneLoader.LoadIntoScene(scene, filePath);
        scene.RebuildWorldGeometry();
        int triangles = scene.ObjectGroups.SelectMany(g => g.SelfAndDescendants()).Sum(g => g.LocalTriangles.Count);
        return new ObjLoadResult(filePath, 0, triangles, triangles);
    }

    /// <summary>Exports the scene to a prop XML file.</summary>
    public void Export(Scene scene, string filePath, SceneSaveOptions options) => PropXmlSceneSaver.Save(scene, filePath, options);
}
