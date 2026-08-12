/*
 * This representation separates durable or isolated scene state from the mutable live editor graph. Save/load,
 * undo, background rendering, and tests need snapshots/documents that can be copied or serialized without
 * exposing shared mutable objects across threads.
 *
 * `SceneObjectInfo` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Id`, `Name`, `Visible`, `IsSelectable`, `ParentId`, `Depth`,
 * `TriangleCount`, `Kind`, `ChildCount`, `LocalTriangleCount`) travel together because consumers need a
 * consistent snapshot rather than reading those values independently from mutable objects.
 *
 * `MaterialSummary` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Id`, `Name`, `BaseColor`, `Metallic`, `Roughness`, `EmissiveStrength`,
 * `Opacity`) travel together because consumers need a consistent snapshot rather than reading those values
 * independently from mutable objects.
 *
 * `Title` is derived rather than separately stored: it evaluates `string.IsNullOrWhiteSpace(Scene.Description) ?
 * : Scene.Description`. Keeping the value computed from its source fields prevents a second cached flag/value
 * from drifting out of sync.
 *
 * `Lights` is derived rather than separately stored: it evaluates `Scene.Lights`. Keeping the value computed from
 * its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `Assets` is derived rather than separately stored: it evaluates `AssetRegistry.FromScene(Scene)`. Keeping the
 * value computed from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * The `SceneDocument` constructor captures `scene`. Those are the dependencies/initial values the instance needs
 * for its lifetime, so callbacks and later operations use the same objects/configuration rather than looking them
 * up globally.
 *
 * `BuildRenderData` derives render data from lower-level input data, resolving indexing/grouping/derived values
 * once so callers can operate on a coherent higher-level representation.
 *
 * `GetObjectInfos` reads object infos from the authoritative model and returns a value/snapshot suitable for
 * callers, avoiding direct access to mutable internal storage.
 *
 * `AddObjectInfo` adds object info to the owning collection/model while using this boundary to preserve indexing,
 * ownership, and derived-state invariants.
 *
 * `GetObjectKind` reads object kind from the authoritative model and returns a value/snapshot suitable for
 * callers, avoiding direct access to mutable internal storage.
 *
 * `FindObject` searches for object and returns the matching object/value rather than assuming it exists. Callers
 * can therefore distinguish a missing match from the found instance.
 *
 * `SetObjectVisibility` sets object visibility through the owning abstraction instead of exposing a mutable
 * field. That gives the method one place to validate the value and perform any history/cache/UI side effects
 * required by the change.
 *
 * `SetObjectsVisibility` sets objects visibility through the owning abstraction instead of exposing a mutable
 * field. That gives the method one place to validate the value and perform any history/cache/UI side effects
 * required by the change.
 *
 * `GetMaterialSummaries` reads material summaries from the authoritative model and returns a value/snapshot
 * suitable for callers, avoiding direct access to mutable internal storage.
 */
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

    public SceneDocument(Scene scene)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public string Title => string.IsNullOrWhiteSpace(Scene.Description) ? "scene" : Scene.Description;
    public IReadOnlyList<SceneLight> Lights => Scene.Lights;
    public AssetRegistry Assets => AssetRegistry.FromScene(Scene);

    public SceneRenderData BuildRenderData(RenderSettings? settings = null) => SceneRenderDataBuilder.Build(this, settings);

    /// <summary>Returns a flattened, depth-aware view of editable objects for list/tree UI controls.</summary>
    public IReadOnlyList<SceneObjectInfo> GetObjectInfos()
    {
        List<SceneObjectInfo> infos = new();
        foreach (SceneObjectGroup group in Scene.ObjectGroups)
            AddObjectInfo(infos, group, depth: 0);
        return infos;
    }

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

    private static string GetObjectKind(SceneObjectGroup group, int triangleCount)
    {
        if (group.Children.Count > 0) return "group";
        if (!string.IsNullOrWhiteSpace(group.PrimitiveKind)) return group.PrimitiveKind!;
        return triangleCount == 1 ? "triangle" : "mesh";
    }

    public SceneObjectGroup? FindObject(int id) => Scene.GroupById(id);

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

    public bool SetObjectVisibility(int id, bool visible)
    {
        SceneObjectGroup? group = FindObject(id);
        if (group == null || group.Visible == visible)
            return false;

        group.Visible = visible;
        Scene.RebuildWorldGeometry();
        return true;
    }

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

    public int ShowAllObjects()
    {
        List<int> hiddenIds = Scene.ObjectGroups
            .SelectMany(g => g.SelfAndDescendants())
            .Where(g => !g.Visible)
            .Select(g => g.Id)
            .ToList();

        return SetObjectsVisibility(hiddenIds, visible: true);
    }

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
