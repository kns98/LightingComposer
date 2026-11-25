// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerEditHistory.cs
// Purpose: Undo and redo stack for composer edits.
//
// Defines geometry, transform, and snapshot command types plus the history that applies, reverts, and re-applies them.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal interface IComposerEditCommand
{
    string Description { get; }
    int? UndoSelectionId { get; }
    int? RedoSelectionId { get; }
    /// <summary>Reverts this command against the given scene.</summary>
    void Undo(Scene scene);
    /// <summary>Reapplies this command against the given scene.</summary>
    void Redo(Scene scene);
}

/// <summary>Marks edits whose renderer resources cannot be refreshed by the geometry-only fast path.</summary>
internal interface IFullRendererInvalidationEditCommand
{
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

    /// <summary>Initializes a new baked geometry snapshot from the given node states.</summary>
    private BakedGeometryState(NodeGeometry[] nodes)
    {
        this.nodes = nodes;
    }

    /// <summary>Captures the baked triangle state of the given subtree.</summary>
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

    /// <summary>Restores the captured triangles, parameters, and identity transforms onto the scene.</summary>
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

    /// <summary>Initializes a new baked transform command for the given group, pivot, pose, and display state.</summary>
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

    /// <summary>Restores the original geometry, name, and visibility and rebuilds the scene.</summary>
    public void Undo(Scene scene)
    {
        SceneObjectGroup group = RequireGroup(scene);
        beforeGeometry.Restore(scene);
        group.Name = beforeName;
        group.Visible = beforeVisible;
        Rebuild(scene, group);
    }

    /// <summary>Reapplies the baked transform to the original geometry and rebuilds the scene.</summary>
    public void Redo(Scene scene)
    {
        SceneObjectGroup group = RequireGroup(scene);
        beforeGeometry.Restore(scene);
        group.ApplyBakedTransform(fixedPivot, position, rotation, scale, inverse: false);
        group.Name = afterName;
        group.Visible = afterVisible;
        Rebuild(scene, group);
    }

    /// <summary>Returns the edited group or throws when it no longer exists.</summary>
    private SceneObjectGroup RequireGroup(Scene scene) => scene.GroupById(groupId)
        ?? throw new InvalidOperationException("The transformed scene node no longer exists.");

    /// <summary>Recalculates pivots and rebuilds world geometry after a transform edit.</summary>
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

    /// <summary>Initializes a new parametric transform command with before and after parameters and display state.</summary>
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

    /// <summary>Restores the before parameters, name, and visibility.</summary>
    public void Undo(Scene scene) => Restore(scene, beforeParameters, beforeName, beforeVisible);
    /// <summary>Restores the after parameters, name, and visibility.</summary>
    public void Redo(Scene scene) => Restore(scene, afterParameters, afterName, afterVisible);

    /// <summary>Restores the given parameters and display state, then regenerates the procedural object.</summary>
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

    /// <summary>Initializes a new geometry replacement command with before and after state.</summary>
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

    /// <summary>Restores the before geometry snapshot.</summary>
    public void Undo(Scene scene) => Restore(scene, before);
    /// <summary>Restores the after geometry snapshot.</summary>
    public void Redo(Scene scene) => Restore(scene, after);

    /// <summary>Restores the given geometry snapshot and rebuilds pivots and world geometry.</summary>
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
/// <summary>
/// Undo record for one multi-selection batch. Child commands each target a
/// different topmost object, so undo walks them in reverse and redo in order.
/// </summary>
internal sealed class CompositeEditCommand : IComposerEditCommand
{
    private readonly IReadOnlyList<IComposerEditCommand> commands;

    /// <summary>Initializes a new batch command with its description, children, and selection targets.</summary>
    public CompositeEditCommand(
        string description,
        IEnumerable<IComposerEditCommand> commands,
        int? undoSelectionId,
        int? redoSelectionId)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        this.commands = commands?.ToArray() ?? throw new ArgumentNullException(nameof(commands));
        if (this.commands.Count == 0)
            throw new ArgumentException("A batch command needs at least one child command.", nameof(commands));
        UndoSelectionId = undoSelectionId;
        RedoSelectionId = redoSelectionId;
    }

    public string Description { get; }
    public int? UndoSelectionId { get; }
    public int? RedoSelectionId { get; }
    /// <summary>Reverts every child command in reverse order.</summary>
    public void Undo(Scene scene)
    {
        for (int i = commands.Count - 1; i >= 0; i--)
            commands[i].Undo(scene);
    }
    /// <summary>Reapplies every child command in forward order.</summary>
    public void Redo(Scene scene)
    {
        for (int i = 0; i < commands.Count; i++)
            commands[i].Redo(scene);
    }
}

internal sealed class SceneSnapshotEditCommand : IComposerEditCommand
{
    private readonly SceneSnapshot before;
    private readonly SceneSnapshot after;

    /// <summary>Initializes a new snapshot command with before and after scenes and selection targets.</summary>
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
    /// <summary>Restores the before scene snapshot.</summary>
    public void Undo(Scene scene) => scene.RestoreSnapshot(before);
    /// <summary>Restores the after scene snapshot.</summary>
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
    public bool UndoRequiresFullRendererInvalidation =>
        undo.TryPeek(out IComposerEditCommand? command) && command is IFullRendererInvalidationEditCommand;
    public bool RedoRequiresFullRendererInvalidation =>
        redo.TryPeek(out IComposerEditCommand? command) && command is IFullRendererInvalidationEditCommand;

    /// <summary>Pushes an applied command onto the undo stack and trims it to the entry limit.</summary>
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

    /// <summary>Undoes the top command and returns its undo selection target.</summary>
    public int? Undo(Scene scene)
    {
        if (!undo.TryPop(out IComposerEditCommand? command))
            return null;
        command.Undo(scene);
        redo.Push(command);
        return command.UndoSelectionId;
    }

    /// <summary>Redoes the top command and returns its redo selection target.</summary>
    public int? Redo(Scene scene)
    {
        if (!redo.TryPop(out IComposerEditCommand? command))
            return null;
        command.Redo(scene);
        undo.Push(command);
        return command.RedoSelectionId;
    }

    /// <summary>Clears both the undo and redo stacks.</summary>
    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
