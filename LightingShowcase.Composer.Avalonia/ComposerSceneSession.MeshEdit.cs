// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerSceneSession.MeshEdit.cs
// Purpose: Mesh editing side of the scene session.
//
// Applies component moves and topology edits to cached mesh data and commits them as undoable history entries.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed partial class ComposerSceneSession
{
    private sealed record CachedMeshTopology(long SceneRevision, int TriangleCount, ComposerMeshTopology Topology);

    private readonly Dictionary<int, CachedMeshTopology> meshTopologyByGroup = new();
    private ComposerSelectionMode selectionMode = ComposerSelectionMode.Object;
    private const double VertexPickRadiusPixels = 22.0;
    private const double EdgePickRadiusPixels = 18.0;

    private ComposerMeshSelection? selectedMeshSelection;
    private ComposerMeshSelection? hoveredMeshSelection;
    private Vec3 meshMovePreviewLocal = Vec3.Zero;
    private Vec3 meshMovePreviewWorld = Vec3.Zero;
    private ComposerGizmoAxis meshMoveAxisLock = ComposerGizmoAxis.None;
    private bool meshHoverVisible = true;
    private int meshSelectionSerial;

    public ComposerSelectionMode SelectionMode => selectionMode;
    public bool HasMeshComponentSelection => selectedMeshSelection.HasValue;
    public ComposerGizmoAxis MeshMoveAxisLock => meshMoveAxisLock;

    /// <summary>Returns the number of mesh faces in the given group.</summary>
    internal int GetMeshFaceGroupCountForTests(int groupId)
    {
        sceneGate.Wait();
        try
        {
            return scene.GroupById(groupId) is SceneObjectGroup group ? GetMeshTopology(group).Faces.Count : 0;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns the number of mesh edges in the given group.</summary>
    internal int GetMeshEdgeCountForTests(int groupId)
    {
        sceneGate.Wait();
        try
        {
            return scene.GroupById(groupId) is SceneObjectGroup group ? GetMeshTopology(group).Edges.Count : 0;
        }
        finally
        {
            sceneGate.Release();
        }
    }


    /// <summary>Switches the selection mode and clears any mesh selection state.</summary>
    public bool SetSelectionMode(ComposerSelectionMode mode)
    {
        sceneGate.Wait();
        try
        {
            if (selectionMode == mode)
                return false;

            selectionMode = mode;
            if (mode == ComposerSelectionMode.Object)
                meshMoveAxisLock = ComposerGizmoAxis.None;
            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshHoverVisible = true;
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
            RebuildSelectionOverlayCache();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Locks mesh moves to the given axis, forcing no lock in object mode.</summary>
    public bool SetMeshMoveAxisLock(ComposerGizmoAxis axis)
    {
        if (axis is not (ComposerGizmoAxis.None or ComposerGizmoAxis.X or ComposerGizmoAxis.Y or ComposerGizmoAxis.Z))
            throw new ArgumentOutOfRangeException(nameof(axis), axis, "Mesh movement can only be unlocked or constrained to X, Y, or Z.");

        sceneGate.Wait();
        try
        {
            ComposerGizmoAxis requested = selectionMode == ComposerSelectionMode.Object
                ? ComposerGizmoAxis.None
                : axis;
            if (meshMoveAxisLock == requested)
                return false;
            meshMoveAxisLock = requested;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Clears the hovered mesh element and returns true when one was set.</summary>
    public bool ClearMeshHover()
    {
        sceneGate.Wait();
        try
        {
            if (!hoveredMeshSelection.HasValue)
                return false;
            hoveredMeshSelection = null;
            meshHoverVisible = true;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Flips the hover highlight visibility when a mesh element is hovered.</summary>
    public bool ToggleMeshHoverPulse()
    {
        sceneGate.Wait();
        try
        {
            if (!hoveredMeshSelection.HasValue)
                return false;
            meshHoverVisible = !meshHoverVisible;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Inserts the named ready-made object into the scene and returns its group id.</summary>
    public int InsertPrimitive(string primitiveName)
    {
        sceneGate.Wait();
        try
        {
            bool wasEmpty = scene.Triangles.Count == 0;
            SceneSnapshot before = scene.CreateSnapshot();
            SceneObjectGroup group = scene.InsertReadyMadeObject(primitiveName);
            SceneSnapshot after = scene.CreateSnapshot();
            editHistory.PushApplied(new SceneSnapshotEditCommand(
                $"Add {group.Name}",
                before,
                after,
                selectedObjectId,
                group.Id));

            selectedObjectId = group.Id;
            SyncSelectionSetWithActive();
            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshHoverVisible = true;
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            meshMoveAxisLock = ComposerGizmoAxis.None;
            selectionMode = ComposerSelectionMode.Object;
            meshTopologyByGroup.Clear();
            RebuildSelectionOverlayCache();
            ScenePath = null;
            InvalidateRendererCaches();
            if (wasEmpty)
                Camera.Reset(scene);
            return group.Id;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Refreshes the hovered mesh element under the cursor and reports whether the hover changed.</summary>
    public ComposerMeshPickResult? UpdateMeshHover(
        CameraDefinition camera,
        double normalizedX,
        double normalizedY,
        int width,
        int height,
        ComposerSelectionMode mode,
        out bool changed)
    {
        changed = false;
        if (mode == ComposerSelectionMode.Object || width <= 0 || height <= 0)
        {
            changed = ClearMeshHover();
            return null;
        }

        sceneGate.Wait();
        try
        {
            ComposerMeshPickResult? result = TryFindMeshElement(
                camera,
                normalizedX,
                normalizedY,
                width,
                height,
                mode,
                out ComposerMeshSelection candidate)
                ? new ComposerMeshPickResult(candidate.GroupId, candidate.Mode, candidate.ElementIndex, MeshElementLabel(candidate))
                : null;

            if (result != null && selectedMeshSelection == candidate)
                result = null;
            ComposerMeshSelection? next = result != null ? candidate : null;
            changed = hoveredMeshSelection != next;
            if (changed)
            {
                hoveredMeshSelection = next;
                meshHoverVisible = true;
            }
            return result;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Selects the mesh element under the cursor for the given mode.</summary>
    public ComposerMeshPickResult? PickMeshElement(
        CameraDefinition camera,
        double normalizedX,
        double normalizedY,
        int width,
        int height,
        ComposerSelectionMode mode)
    {
        if (mode == ComposerSelectionMode.Object || width <= 0 || height <= 0)
            return null;

        sceneGate.Wait();
        try
        {
            if (!TryFindMeshElement(camera, normalizedX, normalizedY, width, height, mode, out ComposerMeshSelection selection))
                return null;

            selectionMode = mode;
            selectedObjectId = selection.GroupId;
            SyncSelectionSetWithActive();
            selectedMeshSelection = selection;
            hoveredMeshSelection = null;
            meshHoverVisible = true;
            meshSelectionSerial = unchecked(meshSelectionSerial + 1);
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            selectedTriangleGroupId = mode == ComposerSelectionMode.Face ? selection.GroupId : null;
            selectedTriangleIndex = mode == ComposerSelectionMode.Face
                ? GetMeshTopology(scene.GroupById(selection.GroupId)!).PrimaryTriangleIndex(selection.ElementIndex)
                : null;
            RebuildSelectionOverlayCache();
            return new ComposerMeshPickResult(
                selection.GroupId,
                mode,
                selection.ElementIndex,
                MeshElementLabel(selection));
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Locates the vertex, edge, or face under the cursor for the given selection mode.</summary>
    private bool TryFindMeshElement(
        CameraDefinition camera,
        double normalizedX,
        double normalizedY,
        int width,
        int height,
        ComposerSelectionMode mode,
        out ComposerMeshSelection selection)
    {
        selection = default;
        double imageX = Math.Clamp(normalizedX, 0.0, 1.0) * width;
        double imageY = Math.Clamp(normalizedY, 0.0, 1.0) * height;
        if (!TryFindNearbyPickSurface(camera, imageX, imageY, width, height, mode, out SceneObjectGroup group, out int triangleIndex))
            return false;

        ComposerMeshTopology topology = GetMeshTopology(group);
        if ((uint)triangleIndex >= (uint)topology.TriangleCount)
            return false;
        int triangleA = topology.TriangleVertexId(triangleIndex, 0);
        int triangleB = topology.TriangleVertexId(triangleIndex, 1);
        int triangleC = topology.TriangleVertexId(triangleIndex, 2);
        int elementIndex;
        switch (mode)
        {
            case ComposerSelectionMode.Vertex:
            {
                int[] candidates = [triangleA, triangleB, triangleC];
                (int Index, double Distance) nearest = candidates
                    .Select(index =>
                    {
                        Vec3 world = TransformPointToWorld(group, topology.Vertices[index]);
                        return TryProjectToImage(world, camera, width, height, out double x, out double y)
                            ? (Index: index, Distance: ScreenDistance(imageX, imageY, x, y))
                            : (Index: index, Distance: double.PositiveInfinity);
                    })
                    .OrderBy(candidate => candidate.Distance)
                    .First();
                if (nearest.Distance > VertexPickRadiusPixels)
                    return false;
                elementIndex = nearest.Index;
                break;
            }
            case ComposerSelectionMode.Edge:
            {
                (int A, int B)[] candidates = [(triangleA, triangleB), (triangleB, triangleC), (triangleC, triangleA)];
                ((int A, int B) Edge, double Distance) nearest = candidates
                    .Select(edge =>
                    {
                        Vec3 worldA = TransformPointToWorld(group, topology.Vertices[edge.A]);
                        Vec3 worldB = TransformPointToWorld(group, topology.Vertices[edge.B]);
                        if (!TryProjectToImage(worldA, camera, width, height, out double ax, out double ay) ||
                            !TryProjectToImage(worldB, camera, width, height, out double bx, out double by))
                        {
                            return (Edge: edge, Distance: double.PositiveInfinity);
                        }
                        return (Edge: edge, Distance: ScreenDistanceToSegment(imageX, imageY, ax, ay, bx, by));
                    })
                    .OrderBy(candidate => candidate.Distance)
                    .First();
                if (nearest.Distance > EdgePickRadiusPixels)
                    return false;
                elementIndex = topology.FindEdgeIndex(nearest.Edge.A, nearest.Edge.B);
                if (elementIndex < 0)
                    return false;
                break;
            }
            default:
                elementIndex = topology.FaceIndexForTriangle(triangleIndex);
                if (elementIndex < 0)
                    return false;
                break;
        }

        selection = new ComposerMeshSelection(group.Id, mode, elementIndex);
        return true;
    }

    /// <summary>Casts pick rays through a small offset grid to find the surface triangle near the cursor.</summary>
    private bool TryFindNearbyPickSurface(
        CameraDefinition camera,
        double imageX,
        double imageY,
        int width,
        int height,
        ComposerSelectionMode mode,
        out SceneObjectGroup group,
        out int triangleIndex)
    {
        group = null!;
        triangleIndex = -1;
        double radius = mode switch
        {
            ComposerSelectionMode.Vertex => VertexPickRadiusPixels * 0.90,
            ComposerSelectionMode.Edge => EdgePickRadiusPixels * 0.90,
            _ => 0.0
        };
        (double X, double Y)[] offsets = radius <= 0.0
            ? [(0.0, 0.0)]
            :
            [
                (0.0, 0.0),
                (-radius, 0.0), (radius, 0.0), (0.0, -radius), (0.0, radius),
                (-radius * 0.707, -radius * 0.707), (radius * 0.707, -radius * 0.707),
                (-radius * 0.707, radius * 0.707), (radius * 0.707, radius * 0.707)
            ];

        SceneObjectGroup? preferred = selectedObjectId is int activeId ? scene.GroupById(activeId) : null;
        foreach ((double offsetX, double offsetY) in offsets)
        {
            double sampleX = imageX + offsetX;
            double sampleY = imageY + offsetY;
            if (sampleX < 0.0 || sampleY < 0.0 || sampleX >= width || sampleY >= height)
                continue;

            Vec3 direction = RayTracer.RayDirection(
                sampleX,
                sampleY,
                width,
                height,
                camera.ToBasis(),
                camera.FieldOfViewDegrees);
            Ray ray = new(camera.Position, direction);

            if (preferred != null && preferred.LocalTriangles.Count > 0)
            {
                int preferredTriangle = FindHitTriangle(preferred, ray);
                if (preferredTriangle >= 0)
                {
                    group = preferred;
                    triangleIndex = preferredTriangle;
                    return true;
                }
            }

            Hit? sceneHit = scene.Intersect(ray);
            if (sceneHit is not { GroupId: >= 0 })
                continue;
            SceneObjectGroup? hitGroup = scene.GroupById(sceneHit.GroupId);
            if (hitGroup == null || hitGroup.LocalTriangles.Count == 0)
                continue;
            int hitTriangle = FindHitTriangle(hitGroup, ray);
            if (hitTriangle < 0)
                continue;
            group = hitGroup;
            triangleIndex = hitTriangle;
            return true;
        }
        return false;
    }

    /// <summary>Returns the display label for the given mesh selection.</summary>
    private static string MeshElementLabel(ComposerMeshSelection selection) => selection.Mode switch
    {
        ComposerSelectionMode.Vertex => $"Vertex {selection.ElementIndex + 1:N0}",
        ComposerSelectionMode.Edge => $"Edge {selection.ElementIndex + 1:N0}",
        ComposerSelectionMode.Face => $"Face {selection.ElementIndex + 1:N0}",
        _ => "Object"
    };

    /// <summary>Returns the world bounds of the active mesh or object selection.</summary>
    public Aabb? GetActiveSelectionBounds()
    {
        sceneGate.Wait();
        try
        {
            if (selectionMode != ComposerSelectionMode.Object)
            {
                return TryBuildMeshSelectionVisual(out Aabb componentBounds, out _)
                    ? componentBounds
                    : null;
            }

            return selectedObjectId is int id
                ? scene.GroupById(id)?.GetWorldBounds(includeHidden: true)
                : null;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Stores a live preview offset for the selected mesh element from the given world delta.</summary>
    public bool UpdateMeshElementMovePreview(int selectedId, Vec3 worldDelta)
    {
        ValidateFinite(worldDelta, nameof(worldDelta));
        sceneGate.Wait();
        try
        {
            if (selectedMeshSelection is not ComposerMeshSelection selection ||
                selection.GroupId != selectedId ||
                scene.GroupById(selectedId) is not SceneObjectGroup group)
            {
                return false;
            }

            Vec3 worldCenter = SelectionWorldCenter(group, selection, Vec3.Zero);
            meshMovePreviewLocal = WorldDeltaToLocal(group, worldCenter, worldDelta);
            meshMovePreviewWorld = worldDelta;
            ScenePath = null;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Discards the pending move preview for the selected mesh element.</summary>
    public bool CancelMeshElementMovePreview(int selectedId)
    {
        sceneGate.Wait();
        try
        {
            if (selectedMeshSelection is not ComposerMeshSelection selection || selection.GroupId != selectedId)
                return false;
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Applies the previewed move to the mesh and records it as an undoable edit.</summary>
    public bool CommitMeshElementMove(int selectedId)
    {
        sceneGate.Wait();
        try
        {
            if (selectedMeshSelection is not ComposerMeshSelection selection ||
                selection.GroupId != selectedId ||
                scene.GroupById(selectedId) is not SceneObjectGroup group)
            {
                return false;
            }

            if (meshMovePreviewLocal.Length() <= 1e-12)
            {
                meshMovePreviewWorld = Vec3.Zero;
                return true;
            }

            group.BakeCurrentTransform();
            meshTopologyByGroup.Remove(group.Id);
            ComposerMeshTopology topology = GetMeshTopology(group);
            Triangle[] before = group.LocalTriangles.ToArray();
            List<Triangle> after = topology.CreateMovedTriangles(before, selection, meshMovePreviewLocal);
            group.LocalTriangles.Clear();
            group.LocalTriangles.AddRange(after);
            ClearPrimitiveMetadata(group);
            group.RecalculatePivot();
            Scene.RecalculatePivotsToRoot(group.Parent);
            scene.RebuildWorldGeometry();

            editHistory.PushApplied(new MeshComponentMoveEditCommand(
                group.Id,
                before,
                after,
                $"Move {selection.Mode.ToString().ToLowerInvariant()}"));

            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            meshTopologyByGroup.Remove(group.Id);
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

    /// <summary>Returns true when the given object holds the currently selected editable face.</summary>
    public bool CanEditSelectedFace(int selectedId)
    {
        sceneGate.Wait();
        try
        {
            return selectedMeshSelection is ComposerMeshSelection selection &&
                   selection.Mode == ComposerSelectionMode.Face &&
                   selection.GroupId == selectedId &&
                   scene.GroupById(selectedId) is SceneObjectGroup group &&
                   selection.ElementIndex >= 0 &&
                   selection.ElementIndex < GetMeshTopology(group).Faces.Count;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Extrudes the selected face by the given distance in meters.</summary>
    public bool ExtrudeSelectedFace(int selectedId, double distanceMeters) =>
        ApplySelectedFaceTopologyEdit(selectedId, distanceMeters, inset: false);

    /// <summary>Insets the selected face by the given amount with no recess.</summary>
    public bool InsetSelectedFace(int selectedId, double insetMeters) =>
        InsetSelectedFace(selectedId, insetMeters, recessDepthMeters: 0.0, profile: ComposerInsetProfile.Square);

    /// <summary>Insets the selected face by the given amount and recess depth.</summary>
    public bool InsetSelectedFace(int selectedId, double insetMeters, double recessDepthMeters) =>
        InsetSelectedFace(selectedId, insetMeters, recessDepthMeters, ComposerInsetProfile.Square);

    /// <summary>Insets the selected face using the given amount, recess depth, and profile.</summary>
    public bool InsetSelectedFace(
        int selectedId,
        double insetMeters,
        double recessDepthMeters,
        ComposerInsetProfile profile) =>
        ApplySelectedFaceTopologyEdit(
            selectedId,
            insetMeters,
            inset: true,
            recessDepthMeters: recessDepthMeters,
            insetProfile: profile);

    /// <summary>Applies an extrude or inset edit to the selected face and records it as an undoable edit.</summary>
    private bool ApplySelectedFaceTopologyEdit(
        int selectedId,
        double amountMeters,
        bool inset,
        double recessDepthMeters = 0.0,
        ComposerInsetProfile insetProfile = ComposerInsetProfile.Square)
    {
        if (!double.IsFinite(amountMeters) || (inset ? amountMeters <= 1e-9 : Math.Abs(amountMeters) <= 1e-9))
            return false;
        if (inset && !double.IsFinite(recessDepthMeters))
            return false;
        if (inset && !Enum.IsDefined(insetProfile))
            return false;

        sceneGate.Wait();
        try
        {
            if (selectedMeshSelection is not ComposerMeshSelection selection ||
                selection.Mode != ComposerSelectionMode.Face ||
                selection.GroupId != selectedId ||
                scene.GroupById(selectedId) is not SceneObjectGroup group)
            {
                return false;
            }

            group.BakeCurrentTransform();
            meshTopologyByGroup.Remove(group.Id);
            ComposerMeshTopology topology = GetMeshTopology(group);
            if (selection.ElementIndex < 0 || selection.ElementIndex >= topology.Faces.Count)
                return false;

            BakedGeometryState before = BakedGeometryState.Capture(group);
            ComposerMeshTopologyEditResult edit = inset
                ? topology.CreateInsetFaceEdit(
                    group.LocalTriangles,
                    selection.ElementIndex,
                    amountMeters,
                    recessDepthMeters,
                    insetProfile)
                : topology.CreateExtrudedFaceEdit(group.LocalTriangles, selection.ElementIndex, amountMeters);
            if (edit.Triangles.Count == group.LocalTriangles.Count &&
                edit.Triangles.Zip(group.LocalTriangles).All(pair => ReferenceEquals(pair.First, pair.Second)))
            {
                return false;
            }

            group.LocalTriangles.Clear();
            group.LocalTriangles.AddRange(edit.Triangles);
            ClearPrimitiveMetadata(group);
            // Extrude/Inset know exactly which newly emitted render triangles
            // belong to each polygon, so preserve that authored topology instead
            // of asking a geometric heuristic to rediscover it afterward.
            group.SetLogicalFaceTriangleGroups(edit.LogicalFaceTriangleGroups);
            group.RecalculatePivot();
            Scene.RecalculatePivotsToRoot(group.Parent);
            scene.RebuildWorldGeometry();
            BakedGeometryState after = BakedGeometryState.Capture(group);
            editHistory.PushApplied(new GeometryStateEditCommand(
                inset ? "Inset face" : "Extrude face",
                group.Id,
                before,
                after));

            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
            meshTopologyByGroup.Remove(group.Id);
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

    /// <summary>Builds the Vulkan raster preview for the pending mesh-element move, or null when there is none.</summary>
    private VulkanRasterMeshEditPreview? CreateVulkanMeshEditPreview()
    {
        if (selectedMeshSelection is not ComposerMeshSelection selection ||
            scene.GroupById(selection.GroupId) is not SceneObjectGroup group ||
            meshMovePreviewWorld.Length() <= 1e-12)
        {
            return null;
        }

        ComposerMeshTopology topology = GetMeshTopology(group);
        IReadOnlyList<ComposerMeshTriangleMove> moves = topology.TriangleMoves(selection);
        if (moves.Count == 0)
            return null;

        return new VulkanRasterMeshEditPreview(
            meshSelectionSerial,
            selection.GroupId,
            moves.Select(move => new VulkanRasterMeshTriangleEdit(move.TriangleIndex, move.CornerMask)),
            meshMovePreviewWorld);
    }

    /// <summary>Builds the selection highlight visual for the active mesh selection.</summary>
    private bool TryBuildMeshSelectionVisual(
        out Aabb bounds,
        out ComposerMeshSelectionVisual visual) =>
        TryBuildMeshSelectionVisual(selectedMeshSelection, meshMovePreviewLocal, out bounds, out visual);

    /// <summary>Builds the highlight visual for the hovered mesh element.</summary>
    private bool TryBuildMeshHoverVisual(
        out ComposerMeshSelectionVisual visual)
    {
        bool built = TryBuildMeshSelectionVisual(hoveredMeshSelection, Vec3.Zero, out _, out visual);
        return built;
    }

    /// <summary>Builds the highlight visual for the given mesh selection plus a local move offset.</summary>
    private bool TryBuildMeshSelectionVisual(
        ComposerMeshSelection? selectionValue,
        Vec3 localDelta,
        out Aabb bounds,
        out ComposerMeshSelectionVisual visual)
    {
        if (selectionValue is not ComposerMeshSelection selection ||
            scene.GroupById(selection.GroupId) is not SceneObjectGroup group)
        {
            bounds = default;
            visual = null!;
            return false;
        }

        ComposerMeshTopology topology = GetMeshTopology(group);
        IReadOnlySet<int> selectedVertices = topology.VertexSet(selection);
        if (selectedVertices.Count == 0)
        {
            bounds = default;
            visual = null!;
            return false;
        }

        Dictionary<int, Vec3> worldByVertex = new(selectedVertices.Count);
        foreach (int vertexIndex in selectedVertices)
        {
            Vec3 movedLocal = topology.Vertices[vertexIndex] + localDelta;
            worldByVertex[vertexIndex] = TransformPointToWorld(group, movedLocal);
        }

        List<Vec3> points = worldByVertex.Values.ToList();
        List<ComposerWorldEdge> edges = new();
        List<Triangle> faces = new();
        switch (selection.Mode)
        {
            case ComposerSelectionMode.Edge:
            {
                ComposerMeshEdge edge = topology.Edges[selection.ElementIndex];
                edges.Add(new ComposerWorldEdge(worldByVertex[edge.A], worldByVertex[edge.B]));
                break;
            }
            case ComposerSelectionMode.Face:
            {
                ComposerMeshFace face = topology.Faces[selection.ElementIndex];
                int[] loop = face.BoundaryLoop;
                for (int i = 0; i < loop.Length; i++)
                {
                    int next = (i + 1) % loop.Length;
                    if (worldByVertex.TryGetValue(loop[i], out Vec3 a) &&
                        worldByVertex.TryGetValue(loop[next], out Vec3 b))
                    {
                        edges.Add(new ComposerWorldEdge(a, b));
                    }
                }
                foreach (int triangleIndex in face.TriangleIndices)
                {
                    Triangle source = group.LocalTriangles[triangleIndex];
                    int va = topology.TriangleVertexId(triangleIndex, 0);
                    int vb = topology.TriangleVertexId(triangleIndex, 1);
                    int vc = topology.TriangleVertexId(triangleIndex, 2);
                    if (!worldByVertex.TryGetValue(va, out Vec3 a) ||
                        !worldByVertex.TryGetValue(vb, out Vec3 b) ||
                        !worldByVertex.TryGetValue(vc, out Vec3 c))
                        continue;
                    faces.Add(new Triangle(a, b, c, source.Material, group.Id));
                }
                break;
            }
        }

        bounds = BoundsOfPoints(points);
        visual = new ComposerMeshSelectionVisual(selection.Mode, points, edges, faces);
        return true;
    }

    /// <summary>Returns the cached mesh topology for the given group, rebuilding it when stale.</summary>
    private ComposerMeshTopology GetMeshTopology(SceneObjectGroup group)
    {
        if (meshTopologyByGroup.TryGetValue(group.Id, out CachedMeshTopology cached) &&
            cached.SceneRevision == scene.Revision &&
            cached.TriangleCount == group.LocalTriangles.Count)
        {
            return cached.Topology;
        }

        ComposerMeshTopology topology = ComposerMeshTopology.Build(group);
        meshTopologyByGroup[group.Id] = new CachedMeshTopology(scene.Revision, group.LocalTriangles.Count, topology);
        return topology;
    }

    /// <summary>Returns the index of the closest triangle in the group hit by the ray.</summary>
    private int FindHitTriangle(SceneObjectGroup group, Ray ray)
    {
        int bestIndex = -1;
        double bestDistance = double.PositiveInfinity;
        for (int i = 0; i < group.LocalTriangles.Count; i++)
        {
            Triangle world = TransformTriangleToWorld(group, group.LocalTriangles[i]);
            Hit? hit = world.Intersect(ray);
            if (hit == null || hit.T >= bestDistance)
                continue;
            bestDistance = hit.T;
            bestIndex = i;
        }
        return bestIndex;
    }

    /// <summary>Averages the world positions of the selected mesh vertices into a center point.</summary>
    private Vec3 SelectionWorldCenter(
        SceneObjectGroup group,
        ComposerMeshSelection selection,
        Vec3 additionalLocalDelta)
    {
        ComposerMeshTopology topology = GetMeshTopology(group);
        IReadOnlySet<int> vertices = topology.VertexSet(selection);
        if (vertices.Count == 0)
            return TransformPointToWorld(group, group.Pivot);

        Vec3 sum = Vec3.Zero;
        foreach (int index in vertices)
            sum += TransformPointToWorld(group, topology.Vertices[index] + meshMovePreviewLocal + additionalLocalDelta);
        return sum / vertices.Count;
    }

    /// <summary>Transforms the point from group-local space to world space.</summary>
    private static Vec3 TransformPointToWorld(SceneObjectGroup group, Vec3 point)
    {
        Vec3 result = point;
        for (SceneObjectGroup? current = group; current != null; current = current.Parent)
            result = current.TransformPoint(result);
        return result;
    }

    /// <summary>Transforms the world point into group-local space.</summary>
    private static Vec3 WorldToLocalPoint(SceneObjectGroup group, Vec3 worldPoint)
    {
        List<SceneObjectGroup> chain = new();
        for (SceneObjectGroup? current = group; current != null; current = current.Parent)
            chain.Add(current);

        Vec3 result = worldPoint;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            SceneObjectGroup current = chain[i];
            result = TransformConverter.ApplyInverseSrt(
                result,
                current.Pivot,
                current.Position,
                current.Rotation,
                current.Scale);
        }
        return result;
    }

    /// <summary>Converts the world-space move delta into group-local space.</summary>
    private static Vec3 WorldDeltaToLocal(SceneObjectGroup group, Vec3 worldOrigin, Vec3 worldDelta) =>
        WorldToLocalPoint(group, worldOrigin + worldDelta) - WorldToLocalPoint(group, worldOrigin);

    /// <summary>Projects the world point onto the image plane and returns false when it lies behind the camera.</summary>
    private static bool TryProjectToImage(
        Vec3 point,
        CameraDefinition camera,
        int width,
        int height,
        out double x,
        out double y)
    {
        CameraBasis basis = camera.ToBasis();
        Vec3 relative = point - camera.Position;
        double depth = relative.Dot(basis.Forward);
        if (!double.IsFinite(depth) || depth <= Math.Max(1e-5, camera.NearPlane * 0.25))
        {
            x = 0.0;
            y = 0.0;
            return false;
        }

        double tangent = Math.Tan((Math.Clamp(camera.FieldOfViewDegrees, 1.0, 179.0) * Math.PI / 180.0) * 0.5);
        double aspect = width / (double)Math.Max(1, height);
        double horizontal = relative.Dot(basis.Right) / depth;
        double vertical = relative.Dot(basis.Up) / depth;
        x = width * 0.5 * (1.0 - horizontal / (aspect * tangent));
        y = height * 0.5 * (1.0 - vertical / tangent);
        return double.IsFinite(x) && double.IsFinite(y);
    }

    /// <summary>Measures the pixel distance from the point to the given screen segment.</summary>
    private static double ScreenDistanceToSegment(
        double px, double py,
        double ax, double ay,
        double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double denominator = dx * dx + dy * dy;
        double t = denominator <= 1e-12
            ? 0.0
            : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / denominator, 0.0, 1.0);
        return ScreenDistance(px, py, ax + dx * t, ay + dy * t);
    }

    /// <summary>Measures the pixel distance between two screen points.</summary>
    private static double ScreenDistance(double ax, double ay, double bx, double by)
    {
        double dx = ax - bx;
        double dy = ay - by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Computes the axis-aligned bounds enclosing the given points.</summary>
    private static Aabb BoundsOfPoints(IReadOnlyList<Vec3> points)
    {
        if (points.Count == 0)
            return new Aabb(Vec3.Zero, Vec3.Zero);

        Vec3 min = points[0];
        Vec3 max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            Vec3 point = points[i];
            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }
        return new Aabb(min, max);
    }

    /// <summary>Clears the procedural primitive metadata from the group.</summary>
    private static void ClearPrimitiveMetadata(SceneObjectGroup group)
    {
        group.PrimitiveKind = null;
        group.PrimitiveSourceName = null;
        group.PrimitiveParameters.Clear();
    }
}
