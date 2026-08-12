/*
 * This representation separates durable or isolated scene state from the mutable live editor graph. Save/load,
 * undo, background rendering, and tests need snapshots/documents that can be copied or serialized without
 * exposing shared mutable objects across threads.
 *
 * `SceneCloner` provides shared algorithms/registration behavior without per-instance state.
 *
 * The `SceneSnapshot` constructor captures `description`, `objectGroups`, `lights`. Those are the
 * dependencies/initial values the instance needs for its lifetime, so callbacks and later operations use the same
 * objects/configuration rather than looking them up globally.
 */
using LightingShowcase.Lighting;

namespace LightingShowcase.SceneGraph;

/// <summary>Immutable deep copy of scene objects and lights for undo/redo.</summary>
public sealed class SceneSnapshot
{
    public string Description { get; }
    public IReadOnlyList<SceneObjectGroup> ObjectGroups { get; }
    public IReadOnlyList<SceneLight> Lights { get; }

    public SceneSnapshot(string description, IEnumerable<SceneObjectGroup> objectGroups, IEnumerable<SceneLight> lights)
    {
        Description = description;
        ObjectGroups = objectGroups.Select(SceneCloner.CloneGroupPreservingId).ToList();
        Lights = lights.Select(SceneCloner.CloneLight).ToList();
    }
}

/// <summary>Deep-copy helpers used by SceneSnapshot.</summary>
internal static class SceneCloner
{
    public static SceneObjectGroup CloneGroupPreservingId(SceneObjectGroup source)
        => CloneGroup(source, source.Id, source.Name, preserveChildIds: true);

    public static SceneObjectGroup CloneGroupWithNewId(SceneObjectGroup source, int id, string name)
        => CloneGroup(source, id, name, preserveChildIds: true);

    public static SceneObjectGroup CloneGroupWithFreshIds(SceneObjectGroup source, Func<int> nextId, string name)
        => CloneGroupWithFreshIdsCore(source, nextId, name);

    private static SceneObjectGroup CloneGroup(SceneObjectGroup source, int id, string name, bool preserveChildIds)
    {
        SceneObjectGroup clone = new(id, name, source.IsSelectable)
        {
            Position = source.Position,
            Rotation = source.Rotation,
            Scale = source.Scale,
            ColorOverride = source.ColorOverride,
            PrimitiveKind = source.PrimitiveKind,
            PrimitiveSourceName = source.PrimitiveSourceName,
            Visible = source.Visible
        };
        foreach (KeyValuePair<string, double> parameter in source.PrimitiveParameters)
            clone.PrimitiveParameters[parameter.Key] = parameter.Value;
        clone.LocalTriangles.AddRange(source.LocalTriangles);
        clone.SetLogicalFaceTriangleGroups(source.LogicalFaceTriangleGroups);

        foreach (SceneObjectGroup sourceChild in source.Children)
        {
            SceneObjectGroup childClone = CloneGroup(sourceChild, sourceChild.Id, sourceChild.Name, preserveChildIds);
            clone.AddChild(childClone);
        }

        clone.RecalculatePivot();
        return clone;
    }

    private static SceneObjectGroup CloneGroupWithFreshIdsCore(SceneObjectGroup source, Func<int> nextId, string name)
    {
        SceneObjectGroup clone = new(nextId(), name, source.IsSelectable)
        {
            Position = source.Position,
            Rotation = source.Rotation,
            Scale = source.Scale,
            ColorOverride = source.ColorOverride,
            PrimitiveKind = source.PrimitiveKind,
            PrimitiveSourceName = source.PrimitiveSourceName,
            Visible = source.Visible
        };
        foreach (KeyValuePair<string, double> parameter in source.PrimitiveParameters)
            clone.PrimitiveParameters[parameter.Key] = parameter.Value;
        clone.LocalTriangles.AddRange(source.LocalTriangles);
        clone.SetLogicalFaceTriangleGroups(source.LogicalFaceTriangleGroups);

        foreach (SceneObjectGroup sourceChild in source.Children)
            clone.AddChild(CloneGroupWithFreshIdsCore(sourceChild, nextId, sourceChild.Name));

        clone.RecalculatePivot();
        return clone;
    }

    public static SceneLight CloneLight(SceneLight source)
        => new(source.Id, source.Position, source.Color, source.Intensity, source.Enabled, source.Kind, source.Direction, source.Range, source.InnerConeAngle, source.OuterConeAngle, source.CastsShadow, source.IsImported, source.IsDefault);
}
