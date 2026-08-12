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
