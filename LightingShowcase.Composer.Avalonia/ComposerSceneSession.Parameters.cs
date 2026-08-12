/*
 * `ComposerSceneSession` is the synchronization and transaction boundary around the live scene. Code in these
 * partial files takes the session gate before touching shared mutable scene data, records logical edits for
 * undo/redo, preserves procedural metadata when possible, and invalidates renderer caches when geometry/material
 * state changes. UI code should ask the session to perform edits instead of modifying scene objects directly.
 *
 * `ComposerPrimitiveParameterModel` is an immutable packet of related values. Record value semantics make it
 * suitable for snapshots, options, commands, or parsed intermediate data because callers can copy/compare it
 * without sharing mutable state. Its constructor values (`ObjectId`, `ObjectName`, `PrimitiveName`, `Parameters`,
 * `Values`) travel together because consumers need a consistent snapshot rather than reading those values
 * independently from mutable objects.
 *
 * `ComposerSceneSession` is one slice of a partial type whose shared state/invariants continue in sibling files.
 *
 * `PrimitiveParameterPreviewState` is an immutable packet of related values. Record value semantics make it
 * suitable for snapshots, options, commands, or parsed intermediate data because callers can copy/compare it
 * without sharing mutable state. Its constructor values (`GroupId`, `BeforeGeometry`, `BeforeParameters`) travel
 * together because consumers need a consistent snapshot rather than reading those values independently from
 * mutable objects.
 *
 * `CanEditPrimitiveParameters` evaluates whether edit primitive parameters is currently legal/available from the
 * existing state. It is a side-effect-free guard intended to drive command enablement or reject an edit before
 * mutation. It takes `sceneGate` before touching the live scene and releases it in `finally`, so
 * readers/renderers cannot observe a half-completed mutation.
 *
 * `GetPrimitiveParameterModel` reads primitive parameter model from the authoritative model and returns a
 * value/snapshot suitable for callers, avoiding direct access to mutable internal storage. It takes `sceneGate`
 * before touching the live scene and releases it in `finally`, so readers/renderers cannot observe a
 * half-completed mutation.
 *
 * `BeginPrimitiveParameterEdit` starts primitive parameter edit by capturing the pre-edit baseline and allocating
 * any temporary preview state needed for cancellation and a single undoable commit. It takes `sceneGate` before
 * touching the live scene and releases it in `finally`, so readers/renderers cannot observe a half-completed
 * mutation.
 *
 * `PreviewPrimitiveParameters` applies a temporary primitive parameters update for interactive feedback. The
 * preview may run many times during one gesture, but it deliberately does not create a separate history entry for
 * every intermediate value. It takes `sceneGate` before touching the live scene and releases it in `finally`, so
 * readers/renderers cannot observe a half-completed mutation. Cancellation is propagated so shutdown or a newer
 * request can make obsolete work stop early.
 *
 * `CommitPrimitiveParameterEdit` finalizes primitive parameter edit: the current preview becomes authoritative
 * and the before/after state is recorded as one logical undoable edit. It takes `sceneGate` before touching the
 * live scene and releases it in `finally`, so readers/renderers cannot observe a half-completed mutation.
 *
 * `CancelPrimitiveParameterEdit` evaluates whether cel primitive parameter edit is currently legal/available from
 * the existing state. It is a side-effect-free guard intended to drive command enablement or reject an edit
 * before mutation. It takes `sceneGate` before touching the live scene and releases it in `finally`, so
 * readers/renderers cannot observe a half-completed mutation. Cancellation is propagated so shutdown or a newer
 * request can make obsolete work stop early.
 *
 * `ConvertParametricObjectToMesh` changes parametric object to mesh into a different representation while
 * preserving the information that representation can express; metadata that no longer applies is deliberately
 * dropped at this boundary. It takes `sceneGate` before touching the live scene and releases it in `finally`, so
 * readers/renderers cannot observe a half-completed mutation. History bookkeeping surrounds the mutation so
 * internal steps collapse into the intended user-level undo transaction.
 *
 * `CommitPrimitiveParameterEditCore` finalizes primitive parameter edit core: the current preview becomes
 * authoritative and the before/after state is recorded as one logical undoable edit. History bookkeeping
 * surrounds the mutation so internal steps collapse into the intended user-level undo transaction.
 */
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record ComposerPrimitiveParameterModel(
    int ObjectId,
    string ObjectName,
    string PrimitiveName,
    IReadOnlyList<PrimitiveParameterDescriptor> Parameters,
    IReadOnlyDictionary<string, double> Values);

internal sealed partial class ComposerSceneSession
{
    private sealed record PrimitiveParameterPreviewState(
        int GroupId,
        BakedGeometryState BeforeGeometry,
        KeyValuePair<string, double>[] BeforeParameters);

    private PrimitiveParameterPreviewState? primitiveParameterPreview;

    public bool CanEditPrimitiveParameters(int id)
    {
        sceneGate.Wait();
        try
        {
            return TryGetEditablePrimitive(id, out _, out _, out _);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public ComposerPrimitiveParameterModel? GetPrimitiveParameterModel(int id)
    {
        sceneGate.Wait();
        try
        {
            if (!TryGetEditablePrimitive(id, out SceneObjectGroup group, out ISceneObjectDefinition definition, out IEditablePrimitiveDefinition editable))
                return null;

            return new ComposerPrimitiveParameterModel(
                group.Id,
                group.Name,
                definition.DisplayName,
                editable.EditableParameters,
                new Dictionary<string, double>(group.PrimitiveParameters, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public ComposerPrimitiveParameterModel? BeginPrimitiveParameterEdit(int id)
    {
        sceneGate.Wait();
        try
        {
            if (!TryGetEditablePrimitive(id, out SceneObjectGroup group, out ISceneObjectDefinition definition, out IEditablePrimitiveDefinition editable))
                return null;

            if (primitiveParameterPreview != null && primitiveParameterPreview.GroupId != id)
                CommitPrimitiveParameterEditCore(primitiveParameterPreview.GroupId);

            primitiveParameterPreview ??= new PrimitiveParameterPreviewState(
                id,
                BakedGeometryState.Capture(group),
                group.PrimitiveParameters.ToArray());

            return new ComposerPrimitiveParameterModel(
                group.Id,
                group.Name,
                definition.DisplayName,
                editable.EditableParameters,
                new Dictionary<string, double>(group.PrimitiveParameters, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool PreviewPrimitiveParameters(int id, IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        sceneGate.Wait();
        try
        {
            if (primitiveParameterPreview == null || primitiveParameterPreview.GroupId != id)
            {
                if (!TryGetEditablePrimitive(id, out SceneObjectGroup beginGroup, out _, out _))
                    return false;
                primitiveParameterPreview = new PrimitiveParameterPreviewState(
                    id,
                    BakedGeometryState.Capture(beginGroup),
                    beginGroup.PrimitiveParameters.ToArray());
            }

            if (!TryGetEditablePrimitive(id, out SceneObjectGroup group, out _, out IEditablePrimitiveDefinition editable))
                return false;

            bool changed = false;
            foreach (PrimitiveParameterDescriptor descriptor in editable.EditableParameters)
            {
                if (!values.TryGetValue(descriptor.Key, out double requested))
                    continue;
                double normalized = descriptor.Normalize(requested);
                if (!group.PrimitiveParameters.TryGetValue(descriptor.Key, out double current) || Math.Abs(current - normalized) > 1e-12)
                {
                    group.PrimitiveParameters[descriptor.Key] = normalized;
                    changed = true;
                }
            }

            if (!changed)
                return true;

            if (!scene.RebuildParametricObject(group))
                return false;

            Scene.RecalculatePivotsToRoot(group.Parent);
            scene.RebuildWorldGeometry();
            meshTopologyByGroup.Remove(group.Id);
            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            selectionMode = ComposerSelectionMode.Object;
            RebuildSelectionOverlayCache();
            ScenePath = null;
            RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool CommitPrimitiveParameterEdit(int id)
    {
        sceneGate.Wait();
        try
        {
            return CommitPrimitiveParameterEditCore(id);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool CancelPrimitiveParameterEdit(int id)
    {
        sceneGate.Wait();
        try
        {
            PrimitiveParameterPreviewState? preview = primitiveParameterPreview;
            if (preview == null || preview.GroupId != id)
                return false;

            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null)
            {
                primitiveParameterPreview = null;
                return false;
            }

            preview.BeforeGeometry.Restore(scene);
            foreach (SceneObjectGroup node in group.SelfAndDescendants().Reverse())
                node.RecalculatePivot();
            Scene.RecalculatePivotsToRoot(group.Parent);
            scene.RebuildWorldGeometry();
            primitiveParameterPreview = null;
            meshTopologyByGroup.Remove(id);
            RebuildSelectionOverlayCache();
            ScenePath = null;
            RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool ConvertParametricObjectToMesh(int id)
    {
        sceneGate.Wait();
        try
        {
            if (primitiveParameterPreview != null && primitiveParameterPreview.GroupId == id)
                CommitPrimitiveParameterEditCore(id);

            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null || !group.HasParametricPrimitive)
                return false;

            // Materialize the primitive's authored polygon partition before
            // discarding the procedural definition. The resulting mesh keeps
            // real logical faces (Cube = six quads) rather than falling back to
            // raw render triangles.
            _ = GetMeshTopology(group);
            BakedGeometryState before = BakedGeometryState.Capture(group);
            group.PrimitiveKind = null;
            group.PrimitiveSourceName = null;
            group.PrimitiveParameters.Clear();
            BakedGeometryState after = BakedGeometryState.Capture(group);
            editHistory.PushApplied(new GeometryStateEditCommand("Convert primitive to mesh", id, before, after));
            meshTopologyByGroup.Remove(id);
            RebuildSelectionOverlayCache();
            ScenePath = null;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    private bool CommitPrimitiveParameterEditCore(int id)
    {
        PrimitiveParameterPreviewState? preview = primitiveParameterPreview;
        if (preview == null || preview.GroupId != id)
            return false;

        primitiveParameterPreview = null;
        SceneObjectGroup? group = scene.GroupById(id);
        if (group == null)
            return false;

        bool parametersChanged = !ParameterSetsEqual(preview.BeforeParameters, group.PrimitiveParameters);
        if (!parametersChanged)
            return true;

        BakedGeometryState after = BakedGeometryState.Capture(group);
        editHistory.PushApplied(new GeometryStateEditCommand(
            $"Edit {group.PrimitiveSourceName ?? group.PrimitiveKind ?? "primitive"} parameters",
            id,
            preview.BeforeGeometry,
            after));
        ScenePath = null;
        return true;
    }

    private bool TryGetEditablePrimitive(
        int id,
        out SceneObjectGroup group,
        out ISceneObjectDefinition definition,
        out IEditablePrimitiveDefinition editable)
    {
        group = null!;
        definition = null!;
        editable = null!;
        SceneObjectGroup? candidate = scene.GroupById(id);
        if (candidate == null || !candidate.HasParametricPrimitive || candidate.Children.Count > 0)
            return false;
        if (ScenePrimitiveRegistry.Find(candidate.PrimitiveKind ?? candidate.PrimitiveSourceName) is not ISceneObjectDefinition found ||
            found is not IEditablePrimitiveDefinition editableDefinition ||
            editableDefinition.EditableParameters.Count == 0)
        {
            return false;
        }

        group = candidate;
        definition = found;
        editable = editableDefinition;
        return true;
    }

    private static bool ParameterSetsEqual(
        IReadOnlyCollection<KeyValuePair<string, double>> before,
        IReadOnlyDictionary<string, double> after)
    {
        if (before.Count != after.Count)
            return false;
        foreach (KeyValuePair<string, double> pair in before)
        {
            if (!after.TryGetValue(pair.Key, out double value) || Math.Abs(pair.Value - value) > 1e-12)
                return false;
        }
        return true;
    }
}
