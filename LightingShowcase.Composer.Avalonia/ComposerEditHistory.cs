using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal interface IComposerEditCommand
{
    string Description { get; }
    int? UndoSelectionId { get; }
    int? RedoSelectionId { get; }
    void Undo(Scene scene);
    void Redo(Scene scene);
}

/// <summary>
/// Immutable references to the original triangle objects in one subtree. Triangle
/// is immutable, so retaining references is substantially cheaper than cloning
/// vertex/material data and restores undo geometry bit-for-bit.
/// </summary>
internal sealed class BakedGeometryState
{
    private sealed record NodeGeometry(
        int GroupId,
        Triangle[] Triangles,
        string? PrimitiveKind,
        string? PrimitiveSourceName,
        KeyValuePair<string, double>[] PrimitiveParameters,
        int[][] LogicalFaceTriangleGroups);
    private readonly NodeGeometry[] nodes;

    private BakedGeometryState(NodeGeometry[] nodes)
    {
        this.nodes = nodes;
    }

    public static BakedGeometryState Capture(SceneObjectGroup root)
    {
        return new BakedGeometryState(root.SelfAndDescendants()
            .Select(group => new NodeGeometry(
                group.Id,
                group.LocalTriangles.ToArray(),
                group.PrimitiveKind,
                group.PrimitiveSourceName,
                group.PrimitiveParameters.ToArray(),
                group.LogicalFaceTriangleGroups.Select(face => face.ToArray()).ToArray()))
            .ToArray());
    }

    public void Restore(Scene scene)
    {
        foreach (NodeGeometry node in nodes)
        {
            SceneObjectGroup group = scene.GroupById(node.GroupId)
                ?? throw new InvalidOperationException("A transformed scene node no longer exists.");
            group.LocalTriangles.Clear();
            group.LocalTriangles.AddRange(node.Triangles);
            group.PrimitiveKind = node.PrimitiveKind;
            group.PrimitiveSourceName = node.PrimitiveSourceName;
            group.PrimitiveParameters.Clear();
            foreach (KeyValuePair<string, double> parameter in node.PrimitiveParameters)
                group.PrimitiveParameters[parameter.Key] = parameter.Value;
            group.SetLogicalFaceTriangleGroups(node.LogicalFaceTriangleGroups);
            group.Position = Vec3.Zero;
            group.Rotation = Vec3.Zero;
            group.Scale = new Vec3(1, 1, 1);
        }
    }
}

/// <summary>
/// Undo record for a baked transform. Original immutable triangle references are
/// retained so undo is exact; redo reapplies the transform to those originals.
/// </summary>
internal sealed class BakedTransformEditCommand : IComposerEditCommand
{
    private readonly int groupId;
    private readonly BakedGeometryState beforeGeometry;
    private readonly Vec3 fixedPivot;
    private readonly Vec3 position;
    private readonly Vec3 rotation;
    private readonly Vec3 scale;
    private readonly string beforeName;
    private readonly string afterName;
    private readonly bool beforeVisible;
    private readonly bool afterVisible;

    public BakedTransformEditCommand(
        int groupId,
        BakedGeometryState beforeGeometry,
        Vec3 fixedPivot,
        Vec3 position,
        Vec3 rotation,
        Vec3 scale,
        string beforeName,
        string afterName,
        bool beforeVisible,
        bool afterVisible)
    {
        this.groupId = groupId;
        this.beforeGeometry = beforeGeometry;
        this.fixedPivot = fixedPivot;
        this.position = position;
        this.rotation = rotation;
        this.scale = scale;
        this.beforeName = beforeName;
        this.afterName = afterName;
        this.beforeVisible = beforeVisible;
        this.afterVisible = afterVisible;
    }

    public string Description => "Bake transform";
    public int? UndoSelectionId => groupId;
    public int? RedoSelectionId => groupId;

    public void Undo(Scene scene)
    {
        SceneObjectGroup group = RequireGroup(scene);
        beforeGeometry.Restore(scene);
        group.Name = beforeName;
        group.Visible = beforeVisible;
        Rebuild(scene, group);
    }

    public void Redo(Scene scene)
    {
        SceneObjectGroup group = RequireGroup(scene);
        beforeGeometry.Restore(scene);
        group.ApplyBakedTransform(fixedPivot, position, rotation, scale, inverse: false);
        group.Name = afterName;
        group.Visible = afterVisible;
        Rebuild(scene, group);
    }

    private SceneObjectGroup RequireGroup(Scene scene) => scene.GroupById(groupId)
        ?? throw new InvalidOperationException("The transformed scene node no longer exists.");

    private static void Rebuild(Scene scene, SceneObjectGroup group)
    {
        foreach (SceneObjectGroup node in group.SelfAndDescendants().Reverse())
            node.RecalculatePivot();
        Scene.RecalculatePivotsToRoot(group.Parent);
        scene.RebuildWorldGeometry();
    }
}

/// <summary>
/// Undo/redo for a non-destructive transform of a parameterized primitive. Only
/// the small parameter dictionary is retained; the shadow mesh is regenerated on
/// undo/redo, keeping large grid/sphere edits much cheaper than triangle snapshots.
/// </summary>
internal sealed class ParametricTransformEditCommand : IComposerEditCommand
{
    private readonly int groupId;
    private readonly KeyValuePair<string, double>[] beforeParameters;
    private readonly KeyValuePair<string, double>[] afterParameters;
    private readonly string beforeName;
    private readonly string afterName;
    private readonly bool beforeVisible;
    private readonly bool afterVisible;

    public ParametricTransformEditCommand(
        int groupId,
        IEnumerable<KeyValuePair<string, double>> beforeParameters,
        IEnumerable<KeyValuePair<string, double>> afterParameters,
        string beforeName,
        string afterName,
        bool beforeVisible,
        bool afterVisible)
    {
        this.groupId = groupId;
        this.beforeParameters = beforeParameters.ToArray();
        this.afterParameters = afterParameters.ToArray();
        this.beforeName = beforeName;
        this.afterName = afterName;
        this.beforeVisible = beforeVisible;
        this.afterVisible = afterVisible;
    }

    public string Description => "Transform parameterized object";
    public int? UndoSelectionId => groupId;
    public int? RedoSelectionId => groupId;

    public void Undo(Scene scene) => Restore(scene, beforeParameters, beforeName, beforeVisible);
    public void Redo(Scene scene) => Restore(scene, afterParameters, afterName, afterVisible);

    private void Restore(
        Scene scene,
        IReadOnlyList<KeyValuePair<string, double>> parameters,
        string name,
        bool visible)
    {
        SceneObjectGroup group = scene.GroupById(groupId)
            ?? throw new InvalidOperationException("The transformed procedural object no longer exists.");

        group.PrimitiveParameters.Clear();
        foreach (KeyValuePair<string, double> parameter in parameters)
            group.PrimitiveParameters[parameter.Key] = parameter.Value;
        group.Position = Vec3.Zero;
        group.Rotation = Vec3.Zero;
        group.Scale = new Vec3(1, 1, 1);
        group.Name = name;
        group.Visible = visible;

        if (!scene.RebuildParametricObject(group))
            throw new InvalidOperationException("The procedural object could not be regenerated during undo/redo.");
        Scene.RecalculatePivotsToRoot(group.Parent);
        scene.RebuildWorldGeometry();
    }
}

/// <summary>Undo/redo for a small object geometry/metadata replacement such as procedural parameter edits.</summary>
internal sealed class GeometryStateEditCommand : IComposerEditCommand
{
    private readonly int groupId;
    private readonly BakedGeometryState before;
    private readonly BakedGeometryState after;

    public GeometryStateEditCommand(string description, int groupId, BakedGeometryState before, BakedGeometryState after)
    {
        Description = description;
        this.groupId = groupId;
        this.before = before;
        this.after = after;
    }

    public string Description { get; }
    public int? UndoSelectionId => groupId;
    public int? RedoSelectionId => groupId;

    public void Undo(Scene scene) => Restore(scene, before);
    public void Redo(Scene scene) => Restore(scene, after);

    private void Restore(Scene scene, BakedGeometryState state)
    {
        SceneObjectGroup group = scene.GroupById(groupId)
            ?? throw new InvalidOperationException("The edited scene node no longer exists.");
        state.Restore(scene);
        foreach (SceneObjectGroup node in group.SelfAndDescendants().Reverse())
            node.RecalculatePivot();
        Scene.RecalculatePivotsToRoot(group.Parent);
        scene.RebuildWorldGeometry();
    }
}

/// <summary>Deep snapshot command used only for topology-changing edits such as ungroup/delete.</summary>
internal sealed class SceneSnapshotEditCommand : IComposerEditCommand
{
    private readonly SceneSnapshot before;
    private readonly SceneSnapshot after;

    public SceneSnapshotEditCommand(
        string description,
        SceneSnapshot before,
        SceneSnapshot after,
        int? undoSelectionId,
        int? redoSelectionId)
    {
        Description = description;
        this.before = before;
        this.after = after;
        UndoSelectionId = undoSelectionId;
        RedoSelectionId = redoSelectionId;
    }

    public string Description { get; }
    public int? UndoSelectionId { get; }
    public int? RedoSelectionId { get; }
    public void Undo(Scene scene) => scene.RestoreSnapshot(before);
    public void Redo(Scene scene) => scene.RestoreSnapshot(after);
}

internal sealed class ComposerEditHistory
{
    private const int MaximumEntries = 10;
    private readonly Stack<IComposerEditCommand> undo = new();
    private readonly Stack<IComposerEditCommand> redo = new();

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string? UndoDescription => undo.TryPeek(out IComposerEditCommand? command) ? command.Description : null;
    public string? RedoDescription => redo.TryPeek(out IComposerEditCommand? command) ? command.Description : null;

    public void PushApplied(IComposerEditCommand command)
    {
        undo.Push(command);
        redo.Clear();
        if (undo.Count <= MaximumEntries)
            return;

        IComposerEditCommand[] newestFirst = undo.ToArray();
        undo.Clear();
        for (int i = Math.Min(MaximumEntries, newestFirst.Length) - 1; i >= 0; i--)
            undo.Push(newestFirst[i]);
    }

    public int? Undo(Scene scene)
    {
        if (!undo.TryPop(out IComposerEditCommand? command))
            return null;
        command.Undo(scene);
        redo.Push(command);
        return command.UndoSelectionId;
    }

    public int? Redo(Scene scene)
    {
        if (!redo.TryPop(out IComposerEditCommand? command))
            return null;
        command.Redo(scene);
        undo.Push(command);
        return command.RedoSelectionId;
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
