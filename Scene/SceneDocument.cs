// -----------------------------------------------------------------------------
// File: Scene/SceneDocument.cs
// Purpose: Editor-facing scene document facade.
//
// The UI should not manually infer object
// metadata by walking low-level triangle/group data each time it refreshes.  This
// document facade keeps the existing Scene as the geometry source of truth while
// exposing stable, UI-friendly object records and a small set of mutation helpers.
// -----------------------------------------------------------------------------

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>
/// Editor document wrapper around <see cref="Scene"/>.
///
/// The contained Scene remains the authoritative model for geometry, lighting,
/// transforms, serialization, and raytracing.  SceneDocument adds a narrow
/// application-facing layer for object listing, selection plumbing, and common
/// visibility/name changes so the UI no longer reaches into every scene node for
/// routine editor operations.
/// </summary>
public sealed class SceneDocument
{
    public Scene Scene { get; }
    public RenderSettings RenderSettings { get; } = new();

    /// <summary>Wraps the given scene as an editor document.</summary>
    public SceneDocument(Scene scene)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public string Title => string.IsNullOrWhiteSpace(Scene.Description) ? "scene" : Scene.Description;
    public IReadOnlyList<SceneLight> Lights => Scene.Lights;
    public AssetRegistry Assets => AssetRegistry.FromScene(Scene);

    /// <summary>Builds render data for the document with optional settings.</summary>
    public SceneRenderData BuildRenderData(RenderSettings? settings = null) => SceneRenderDataBuilder.Build(this, settings);

    /// <summary>Returns a flattened, depth-aware view of editable objects for list/tree UI controls.</summary>
    public IReadOnlyList<SceneObjectInfo> GetObjectInfos()
    {
        List<SceneObjectInfo> infos = new();
        foreach (SceneObjectGroup group in Scene.ObjectGroups)
            AddObjectInfo(infos, group, depth: 0);
        return infos;
    }

    /// <summary>Appends one object record and recurses into its children.</summary>
    private static void AddObjectInfo(List<SceneObjectInfo> infos, SceneObjectGroup group, int depth)
    {
        int triangleCount = group.CountLocalTrianglesRecursively();
        infos.Add(new SceneObjectInfo(
            group.Id,
            group.Name,
            group.Visible,
            group.IsSelectable,
            group.Parent?.Id,
            depth,
            triangleCount,
            GetObjectKind(group, triangleCount),
            group.Children.Count,
            group.LocalTriangles.Count));

        foreach (SceneObjectGroup child in group.Children)
            AddObjectInfo(infos, child, depth + 1);
    }

    /// <summary>Classifies an object as a group, primitive, triangle, or mesh.</summary>
    private static string GetObjectKind(SceneObjectGroup group, int triangleCount)
    {
        if (group.Children.Count > 0) return "group";
        if (!string.IsNullOrWhiteSpace(group.PrimitiveKind)) return group.PrimitiveKind!;
        return triangleCount == 1 ? "triangle" : "mesh";
    }

    /// <summary>Finds the object with the given id or returns null.</summary>
    public SceneObjectGroup? FindObject(int id) => Scene.GroupById(id);

    /// <summary>Renames one object and reports whether it changed.</summary>
    public bool RenameObject(int id, string name)
    {
        SceneObjectGroup? group = FindObject(id);
        if (group == null) return false;

        string cleaned = string.IsNullOrWhiteSpace(name) ? group.Name : name.Trim();
        if (string.Equals(group.Name, cleaned, StringComparison.Ordinal))
            return false;

        group.Name = cleaned;
        return true;
    }

    /// <summary>Sets one object visibility and rebuilds world geometry.</summary>
    public bool SetObjectVisibility(int id, bool visible)
    {
        SceneObjectGroup? group = FindObject(id);
        if (group == null || group.Visible == visible)
            return false;

        group.Visible = visible;
        Scene.RebuildWorldGeometry();
        return true;
    }

    /// <summary>Sets visibility for several objects and returns the change count.</summary>
    public int SetObjectsVisibility(IEnumerable<int> ids, bool visible)
    {
        int changed = 0;
        foreach (int id in ids.Distinct())
        {
            SceneObjectGroup? group = FindObject(id);
            if (group == null || group.Visible == visible)
                continue;

            group.Visible = visible;
            changed++;
        }

        if (changed > 0)
            Scene.RebuildWorldGeometry();
        return changed;
    }

    /// <summary>Shows every hidden object and returns the change count.</summary>
    public int ShowAllObjects()
    {
        List<int> hiddenIds = Scene.ObjectGroups
            .SelectMany(g => g.SelfAndDescendants())
            .Where(g => !g.Visible)
            .Select(g => g.Id)
            .ToList();

        return SetObjectsVisibility(hiddenIds, visible: true);
    }

    /// <summary>Returns one summary per scene material.</summary>
    public IReadOnlyList<MaterialSummary> GetMaterialSummaries()
    {
        AssetRegistry registry = Assets;
        return registry.Materials
            .Select(m => new MaterialSummary(m.Id, m.Name, m.BaseColor, m.Metallic, m.Roughness, m.EmissiveStrength, m.Opacity))
            .ToList();
    }
}

/// <summary>Immutable UI-facing object metadata produced from the Scene source of truth.</summary>
public sealed record SceneObjectInfo(
    int Id,
    string Name,
    bool Visible,
    bool IsSelectable,
    int? ParentId,
    int Depth,
    int TriangleCount,
    string Kind,
    int ChildCount,
    int LocalTriangleCount);

public sealed record MaterialSummary(
    string Id,
    string Name,
    Vec3 BaseColor,
    double Metallic,
    double Roughness,
    double EmissiveStrength,
    double Opacity);
