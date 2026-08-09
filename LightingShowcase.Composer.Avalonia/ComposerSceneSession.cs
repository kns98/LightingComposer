using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.CommandLine;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record ComposerFrame(RenderImage Image, double ElapsedMilliseconds, string Details);

internal sealed record ComposerObjectState(
    int Id,
    string Name,
    bool Visible,
    Vec3 Position,
    Vec3 Rotation,
    Vec3 Scale,
    int? ParentId,
    int HighestAncestorId);

internal sealed record ComposerModelEvidence(
    int ObjectId,
    long SceneRevision,
    Vec3 Position,
    Vec3 Rotation,
    Vec3 Scale,
    Aabb WorldBounds,
    ulong WorldGeometryHash,
    ulong LocalGeometryHash,
    int TriangleCount);

internal sealed record ComposerTriangleInfo(int Index, string Label);
internal sealed record ComposerFaceInfo(int FaceIndex, int PrimaryTriangleIndex, int TriangleCount, string Label);

internal sealed partial class ComposerSceneSession : IDisposable
{
    private readonly SemaphoreSlim sceneGate = new(1, 1);
    private Scene scene;
    private SceneDocument document;
    private readonly ComposerEditHistory editHistory = new();
    private ShadowRasterRenderer.PreviewCache? rasterCache;
    private int? selectedObjectId;
    private int? selectedTriangleGroupId;
    private int? selectedTriangleIndex;
    private Aabb? selectedOverlayBounds;
    private IReadOnlyList<Triangle> selectedOverlayTriangles = Array.Empty<Triangle>();
    private const int MaximumCachedSelectionTriangles = 256;

    public ComposerSceneSession()
    {
        PluginBootstrap.EnsureLoaded();
        scene = CreateEmptyScene();
        document = new SceneDocument(scene);
        Camera.Reset(scene);
    }

    public ComposerCamera Camera { get; } = new();
    public string? ScenePath { get; private set; }
    public int TriangleCount => scene.Triangles.Count;
    public int LightCount => scene.Lights.Count;
    public int ObjectCount => scene.ObjectGroups.SelectMany(group => group.SelfAndDescendants()).Count();
    public bool HasRenderableScene => scene.Triangles.Count > 0;
    public bool CanUndo => editHistory.CanUndo;
    public bool CanRedo => editHistory.CanRedo;
    public string? UndoDescription => editHistory.UndoDescription;
    public string? RedoDescription => editHistory.RedoDescription;
    public string LastGeometryRefreshDetails { get; private set; } = string.Empty;
    public string? LastImportDetails { get; private set; }

    public IReadOnlyList<SceneObjectInfo> GetObjectInfos() => document.GetObjectInfos();

    public IReadOnlyList<ComposerTriangleInfo> GetTriangleInfos(int groupId, int offset, int count)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(groupId);
            if (group == null || count <= 0)
                return Array.Empty<ComposerTriangleInfo>();

            int start = Math.Clamp(offset, 0, group.LocalTriangles.Count);
            int end = Math.Min(group.LocalTriangles.Count, start + count);
            List<ComposerTriangleInfo> result = new(end - start);
            for (int i = start; i < end; i++)
            {
                Triangle triangle = group.LocalTriangles[i];
                result.Add(new ComposerTriangleInfo(
                    i,
                    $"Triangle {i + 1:N0}  [centroid {triangle.Centroid.X:F3}, {triangle.Centroid.Y:F3}, {triangle.Centroid.Z:F3}]"));
            }
            return result;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns logical polygon faces for the lazy hierarchy detail view.</summary>
    public IReadOnlyList<ComposerFaceInfo> GetFaceInfos(int groupId, int offset, int count)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(groupId);
            if (group == null || count <= 0 || group.LocalTriangles.Count == 0)
                return Array.Empty<ComposerFaceInfo>();

            ComposerMeshTopology topology = GetMeshTopology(group);
            int start = Math.Clamp(offset, 0, topology.Faces.Count);
            int end = Math.Min(topology.Faces.Count, start + count);
            List<ComposerFaceInfo> result = new(end - start);
            for (int i = start; i < end; i++)
            {
                ComposerMeshFace face = topology.Faces[i];
                int triangleCount = face.TriangleIndices.Length;
                string triangleLabel = triangleCount == 1 ? "1 triangle" : $"{triangleCount:N0} triangles";
                result.Add(new ComposerFaceInfo(
                    i,
                    topology.PrimaryTriangleIndex(i),
                    triangleCount,
                    $"Face {i + 1:N0}  [{triangleLabel}]"));
            }
            return result;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int GetFaceCount(int groupId)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(groupId);
            return group == null || group.LocalTriangles.Count == 0
                ? 0
                : GetMeshTopology(group).Faces.Count;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public ComposerObjectState? GetObjectState(int id)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(id);
            SceneObjectGroup? highest = scene.HighestAncestorById(id);
            return group == null || highest == null
                ? null
                : new ComposerObjectState(
                    group.Id,
                    group.Name,
                    group.Visible,
                    group.Position,
                    group.Rotation,
                    group.Scale,
                    group.Parent?.Id,
                    highest.Id);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int? SelectedObjectId => selectedObjectId;

    public ComposerObjectState? GetTransformTargetState(int selectedId)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? target = scene.GroupById(selectedId);
            if (target == null)
                return null;

            return new ComposerObjectState(
                target.Id,
                target.Name,
                target.Visible,
                target.Position,
                target.Rotation,
                target.Scale,
                target.Parent?.Id,
                target.Id);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public Aabb? GetTransformTargetBounds(int selectedId)
    {
        sceneGate.Wait();
        try
        {
            return scene.GroupById(selectedId)?.GetWorldBounds(includeHidden: true);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public ComposerModelEvidence? GetModelEvidence(int objectId)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(objectId);
            if (group == null)
                return null;

            List<Triangle> triangles = group.BuildWorldTriangles(includeHidden: true).ToList();
            return new ComposerModelEvidence(
                group.Id,
                scene.Revision,
                group.Position,
                group.Rotation,
                group.Scale,
                group.GetWorldBounds(includeHidden: true),
                ComputeGeometryHash(triangles),
                ComputeGeometryHash(group.SelfAndDescendants().SelectMany(node => node.LocalTriangles)),
                triangles.Count);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    internal SceneCacheStamp CaptureSceneCacheStampForTests()
    {
        sceneGate.Wait();
        try
        {
            return SceneCacheStamp.Capture(scene);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool SetSelectedObject(int? id)
    {
        sceneGate.Wait();
        try
        {
            int? normalized = id.HasValue && scene.GroupById(id.Value) != null ? id : null;
            bool changed = selectedObjectId != normalized || selectedTriangleIndex.HasValue || selectedMeshSelection.HasValue;
            if (!changed)
                return false;

            selectedObjectId = normalized;
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshHoverVisible = true;
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            meshMoveAxisLock = ComposerGizmoAxis.None;
            if (selectionMode != ComposerSelectionMode.Object)
                selectionMode = ComposerSelectionMode.Object;
            RebuildSelectionOverlayCache();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>
    /// Selects a virtual triangle-row hit without creating a scene object. The
    /// raw triangle is mapped to its logical polygon face, so a cube side selects
    /// both render triangles and the overlay shows the complete quad boundary.
    /// </summary>
    public bool SetSelectedTriangle(int groupId, int localTriangleIndex)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(groupId);
            if (group == null || localTriangleIndex < 0 || localTriangleIndex >= group.LocalTriangles.Count)
                return false;

            ComposerMeshTopology topology = GetMeshTopology(group);
            int faceIndex = topology.FaceIndexForTriangle(localTriangleIndex);
            if (faceIndex < 0)
                return false;

            selectedObjectId = groupId;
            selectedTriangleGroupId = groupId;
            selectedTriangleIndex = localTriangleIndex;
            selectionMode = ComposerSelectionMode.Face;
            selectedMeshSelection = new ComposerMeshSelection(groupId, ComposerSelectionMode.Face, faceIndex);
            hoveredMeshSelection = null;
            meshHoverVisible = true;
            meshSelectionSerial = unchecked(meshSelectionSerial + 1);
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            RebuildSelectionOverlayCache();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public void NewScene(CancellationToken cancellationToken)
    {
        sceneGate.Wait(cancellationToken);
        try
        {
            ReleaseRendererCaches();
            scene = CreateEmptyScene();
            document = new SceneDocument(scene);
            editHistory.Clear();
            ClearSelectionState();
            ScenePath = null;
            LastImportDetails = null;
            Camera.Reset(scene);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public void Load(string inputPath, CancellationToken cancellationToken)
    {
        string path = ResolveScenePath(inputPath);
        string assetDirectory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The scene path has no parent directory.");

        sceneGate.Wait(cancellationToken);
        try
        {
            ReleaseRendererCaches();
            TextureMap.ConfigureAssetRoots([assetDirectory]);

            Scene loaded = new();
            bool isComposerScene = ComposerFileTypes.IsBinaryScenePath(path);
            ObjLoadResult? importResult = null;
            if (isComposerScene)
            {
                loaded.SetDescription(BinarySceneFile.LoadIntoScene(loaded, path));
            }
            else
            {
                importResult = loaded.OpenModelFile(path, progress => cancellationToken.ThrowIfCancellationRequested());
            }
            LastImportDetails = importResult?.Details;

            cancellationToken.ThrowIfCancellationRequested();
            if (loaded.Triangles.Count == 0)
                throw new InvalidDataException("The scene contains no renderable triangles.");

            List<int> loadedRootIds = loaded.ObjectGroups.Select(group => group.Id).ToList();
            if (!isComposerScene && loadedRootIds.Count > 0)
            {
                loaded.WrapRootGroups(
                    loadedRootIds,
                    Path.GetFileNameWithoutExtension(path));
            }

            scene = loaded;
            document = new SceneDocument(scene);
            editHistory.Clear();
            ScenePath = path;
            ClearSelectionState();
            Camera.Reset(scene);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int Insert(string inputPath, CancellationToken cancellationToken)
    {
        string path = ResolveInsertPath(inputPath);
        string assetDirectory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The model path has no parent directory.");

        sceneGate.Wait(cancellationToken);
        try
        {
            bool wasEmpty = scene.Triangles.Count == 0;
            HashSet<int> rootsBeforeInsert = scene.ObjectGroups.Select(group => group.Id).ToHashSet();
            TextureMap.ConfigureAssetRoots([assetDirectory]);
            ObjLoadResult importResult = scene.InsertModelFromFile(path, progress => cancellationToken.ThrowIfCancellationRequested());
            LastImportDetails = importResult.Details;

            List<int> insertedRootIds = scene.ObjectGroups
                .Where(group => !rootsBeforeInsert.Contains(group.Id))
                .Select(group => group.Id)
                .ToList();
            if (insertedRootIds.Count == 0)
                throw new InvalidDataException("The imported model did not create any scene objects.");

            SceneObjectGroup wrapper = scene.WrapRootGroups(
                insertedRootIds,
                Path.GetFileNameWithoutExtension(path));
            editHistory.Clear();
            ScenePath = null;
            meshTopologyByGroup.Clear();
            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshHoverVisible = true;
            meshMovePreviewLocal = Vec3.Zero;
            meshMovePreviewWorld = Vec3.Zero;
            meshMoveAxisLock = ComposerGizmoAxis.None;
            selectionMode = ComposerSelectionMode.Object;
            InvalidateRendererCaches();
            if (wasEmpty && scene.Triangles.Count > 0)
                Camera.Reset(scene);
            return wrapper.Id;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public void Save(string outputPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("A scene output path is required.", nameof(outputPath));

        string path = Path.GetFullPath(outputPath);
        if (!path.EndsWith(".lscene", StringComparison.OrdinalIgnoreCase))
            path += ".lscene";

        sceneGate.Wait(cancellationToken);
        try
        {
            BinarySceneFile.Save(scene, path);
            ScenePath = path;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public SceneExportPackageResult ExportPackage(
        string parentDirectory,
        SceneExportFormat format,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory))
            throw new ArgumentException("An export parent directory is required.", nameof(parentDirectory));

        sceneGate.Wait(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string baseName = ScenePath == null
                ? "composition"
                : Path.GetFileNameWithoutExtension(ScenePath);
            if (baseName.EndsWith(".prop", StringComparison.OrdinalIgnoreCase))
                baseName = Path.GetFileNameWithoutExtension(baseName);

            return new SceneExportPackageService().Export(
                scene,
                parentDirectory,
                baseName,
                format,
                cancellationToken);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool UpdateObject(
        int id,
        string name,
        bool visible,
        Vec3 position,
        Vec3 rotationRadians,
        Vec3 scale)
    {
        ValidateFinite(position, nameof(position));
        ValidateFinite(rotationRadians, nameof(rotationRadians));
        ValidateFinite(scale, nameof(scale));
        if (scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale values must be greater than zero.");

        sceneGate.Wait();
        try
        {
            SceneObjectGroup? selectedGroup = scene.GroupById(id);
            if (selectedGroup == null)
                return false;

            string beforeName = selectedGroup.Name;
            bool beforeVisible = selectedGroup.Visible;
            string afterName = string.IsNullOrWhiteSpace(name) ? beforeName : name.Trim();

            if (selectedGroup.HasParametricPrimitive && selectedGroup.Children.Count == 0)
            {
                // A modeless parameter window may have an active preview batch.
                // Finish that batch before changing the hidden authored transform
                // so closing the dialog later cannot restore pre-transform geometry.
                if (primitiveParameterPreview?.GroupId == selectedGroup.Id)
                    CommitPrimitiveParameterEditCore(selectedGroup.Id);

                KeyValuePair<string, double>[] beforeParameters = selectedGroup.PrimitiveParameters.ToArray();
                Vec3 fixedPivot = selectedGroup.Pivot;
                if (!ObjectLibraryRegistry.AccumulateParametricTransform(
                        selectedGroup, fixedPivot, position, rotationRadians, scale) ||
                    !scene.RebuildParametricObject(selectedGroup))
                {
                    return false;
                }

                selectedGroup.Name = afterName;
                selectedGroup.Visible = visible;
                Scene.RecalculatePivotsToRoot(selectedGroup.Parent);
                scene.RebuildWorldGeometry();
                meshTopologyByGroup.Remove(selectedGroup.Id);
                RebuildSelectionOverlayCache();

                editHistory.PushApplied(new ParametricTransformEditCommand(
                    selectedGroup.Id,
                    beforeParameters,
                    selectedGroup.PrimitiveParameters,
                    beforeName,
                    afterName,
                    beforeVisible,
                    visible));

                ScenePath = null;
                RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
                return true;
            }

            // Ordinary meshes retain the original bake-to-geometry workflow.
            selectedGroup.BakeCurrentTransform();
            BakedGeometryState beforeGeometry = BakedGeometryState.Capture(selectedGroup);
            Vec3 meshFixedPivot = selectedGroup.Pivot;
            selectedGroup.BakeTransform(position, rotationRadians, scale);
            selectedGroup.Name = afterName;
            selectedGroup.Visible = visible;
            Scene.RecalculatePivotsToRoot(selectedGroup.Parent);
            scene.RebuildWorldGeometry();
            RebuildSelectionOverlayCache();

            editHistory.PushApplied(new BakedTransformEditCommand(
                selectedGroup.Id,
                beforeGeometry,
                meshFixedPivot,
                position,
                rotationRadians,
                scale,
                beforeName,
                afterName,
                beforeVisible,
                visible));

            ScenePath = null;
            RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>
    /// Updates temporary gizmo transform metadata while dragging. The final mouse
    /// release calls <see cref="CommitPendingTransform"/> to bake it into geometry.
    /// </summary>
    public bool UpdateTransformTarget(
        int selectedId,
        Vec3 position,
        Vec3 rotationRadians,
        Vec3 scale)
    {
        ValidateFinite(position, nameof(position));
        ValidateFinite(rotationRadians, nameof(rotationRadians));
        ValidateFinite(scale, nameof(scale));
        if (scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale values must be greater than zero.");

        sceneGate.Wait();
        try
        {
            SceneObjectGroup? target = scene.GroupById(selectedId);
            if (target == null)
                return false;

            // Gizmo movement is intentionally metadata-only until mouse release.
            // This avoids rebuilding every world triangle and reuploading the
            // Vulkan vertex buffers on every pointer-move event. CommitPendingTransform
            // bakes once and triggers one renderer refresh.
            target.Position = position;
            target.Rotation = rotationRadians;
            target.Scale = scale;
            ScenePath = null;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool CancelPendingTransform(int selectedId)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? target = scene.GroupById(selectedId);
            if (target == null)
                return false;
            target.Position = Vec3.Zero;
            target.Rotation = Vec3.Zero;
            target.Scale = new Vec3(1, 1, 1);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Bakes the current gizmo preview into geometry and records one undo step.</summary>
    public bool CommitPendingTransform(int selectedId)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? target = scene.GroupById(selectedId);
            if (target == null)
                return false;

            Vec3 position = target.Position;
            Vec3 rotation = target.Rotation;
            Vec3 scale = target.Scale;
            if (IsIdentityTransform(position, rotation, scale))
                return true;

            Vec3 fixedPivot = target.Pivot;
            if (target.HasParametricPrimitive && target.Children.Count == 0)
            {
                if (primitiveParameterPreview?.GroupId == target.Id)
                    CommitPrimitiveParameterEditCore(target.Id);

                KeyValuePair<string, double>[] beforeParameters = target.PrimitiveParameters.ToArray();
                if (!ObjectLibraryRegistry.AccumulateParametricTransform(target, fixedPivot, position, rotation, scale) ||
                    !scene.RebuildParametricObject(target))
                {
                    return false;
                }

                Scene.RecalculatePivotsToRoot(target.Parent);
                scene.RebuildWorldGeometry();
                meshTopologyByGroup.Remove(target.Id);
                RebuildSelectionOverlayCache();
                editHistory.PushApplied(new ParametricTransformEditCommand(
                    target.Id,
                    beforeParameters,
                    target.PrimitiveParameters,
                    target.Name,
                    target.Name,
                    target.Visible,
                    target.Visible));
                ScenePath = null;
                RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
                return true;
            }

            BakedGeometryState beforeGeometry = BakedGeometryState.Capture(target);
            target.BakeCurrentTransform();
            Scene.RecalculatePivotsToRoot(target.Parent);
            scene.RebuildWorldGeometry();
            RebuildSelectionOverlayCache();
            editHistory.PushApplied(new BakedTransformEditCommand(
                target.Id,
                beforeGeometry,
                fixedPivot,
                position,
                rotation,
                scale,
                target.Name,
                target.Name,
                target.Visible,
                target.Visible));
            ScenePath = null;
            RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool ResetObjectTransform(int id)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null)
                return false;

            group.Position = Vec3.Zero;
            group.Rotation = Vec3.Zero;
            group.Scale = new Vec3(1, 1, 1);
            scene.RebuildWorldGeometry();
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

    public bool CanGroupObjects(IEnumerable<int> ids)
    {
        sceneGate.Wait();
        try
        {
            return scene.CanGroupSelectedObjects(ids);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int? GroupObjects(IEnumerable<int> ids, string name = "Group")
    {
        int[] selectedIds = ids.Distinct().ToArray();
        sceneGate.Wait();
        try
        {
            if (!scene.CanGroupSelectedObjects(selectedIds))
                return null;
            SceneSnapshot before = scene.CreateSnapshot();
            SceneObjectGroup parent = scene.GroupSelectedObjects(selectedIds, name);
            SceneSnapshot after = scene.CreateSnapshot();
            editHistory.PushApplied(new SceneSnapshotEditCommand("Group objects", before, after, selectedIds.FirstOrDefault(), parent.Id));
            selectedObjectId = parent.Id;
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshTopologyByGroup.Clear();
            RebuildSelectionOverlayCache();
            ScenePath = null;
            InvalidateRendererCaches();
            return parent.Id;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool CanUngroupObjects(IEnumerable<int> ids)
    {
        int[] selectedIds = ids.Distinct().ToArray();
        sceneGate.Wait();
        try
        {
            // For a Ctrl multi-selection, "Ungroup" means dissolve hierarchy
            // groups, not explode every selected leaf mesh into triangle objects.
            // Keep the historical single-selection geometry ungroup behavior.
            return selectedIds.Length > 1
                ? selectedIds.Any(id => scene.GroupById(id)?.Children.Count > 0)
                : selectedIds.Any(scene.CanUngroup);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public IReadOnlyList<int> UngroupObjects(IEnumerable<int> ids)
    {
        int[] selectedIds = ids.Distinct().ToArray();
        sceneGate.Wait();
        try
        {
            int[] targets = selectedIds.Length > 1
                ? selectedIds.Where(id => scene.GroupById(id)?.Children.Count > 0).ToArray()
                : selectedIds.Where(scene.CanUngroup).ToArray();
            if (targets.Length == 0)
                return Array.Empty<int>();

            SceneSnapshot before = scene.CreateSnapshot();
            List<int> promotedIds = new();
            foreach (int id in targets)
            {
                if (!scene.CanUngroup(id))
                    continue;
                promotedIds.AddRange(scene.Ungroup(id).Select(group => group.Id));
            }
            SceneSnapshot after = scene.CreateSnapshot();
            int? preferred = promotedIds.Count > 0 ? promotedIds[0] : null;
            editHistory.PushApplied(new SceneSnapshotEditCommand("Ungroup objects", before, after, targets[0], preferred));
            selectedObjectId = preferred;
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
            selectedMeshSelection = null;
            hoveredMeshSelection = null;
            meshTopologyByGroup.Clear();
            RebuildSelectionOverlayCache();
            ScenePath = null;
            InvalidateRendererCaches();
            return promotedIds;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool CanUngroupObject(int id)
    {
        sceneGate.Wait();
        try
        {
            return scene.CanUngroup(id);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public IReadOnlyList<int> UngroupObject(int id)
    {
        sceneGate.Wait();
        try
        {
            if (!scene.CanUngroup(id))
                return Array.Empty<int>();

            SceneSnapshot before = scene.CreateSnapshot();
            IReadOnlyList<SceneObjectGroup> promoted = scene.Ungroup(id);
            SceneSnapshot after = scene.CreateSnapshot();
            int? preferred = promoted.FirstOrDefault()?.Id;
            editHistory.PushApplied(new SceneSnapshotEditCommand("Ungroup", before, after, id, preferred));
            if (selectedObjectId == id)
                selectedObjectId = preferred;
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
            RebuildSelectionOverlayCache();
            ScenePath = null;
            InvalidateRendererCaches();
            return promoted.Select(group => group.Id).ToList();
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int? Undo()
    {
        sceneGate.Wait();
        try
        {
            int? preferred = editHistory.Undo(scene);
            selectedObjectId = preferred.HasValue && scene.GroupById(preferred.Value) != null ? preferred : null;
            ApplySelectionHighlightAndRebuild();
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
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
            RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
            return selectedObjectId;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int? Redo()
    {
        sceneGate.Wait();
        try
        {
            int? preferred = editHistory.Redo(scene);
            selectedObjectId = preferred.HasValue && scene.GroupById(preferred.Value) != null ? preferred : null;
            ApplySelectionHighlightAndRebuild();
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
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
            RefreshRendererCachesAfterGeometryBake(CancellationToken.None);
            return selectedObjectId;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int? DuplicateObject(int id)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup duplicate = scene.DuplicateGroup(id);
            editHistory.Clear();
            ScenePath = null;
            InvalidateRendererCaches();
            return duplicate.Id;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool DeleteObject(int id)
    {
        sceneGate.Wait();
        try
        {
            if (scene.GroupById(id) == null)
                return false;

            bool deletingSelection = selectedObjectId.HasValue &&
                                     (selectedObjectId.Value == id ||
                                      scene.GroupById(id)?.ContainsDescendant(selectedObjectId.Value) == true);
            scene.DeleteGroup(id);
            editHistory.Clear();
            if (deletingSelection)
                ClearSelectionState();
            else
                RebuildSelectionOverlayCache();
            ScenePath = null;
            InvalidateRendererCaches();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool FrameObject(int id)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null)
                return false;
            Camera.Frame(group.GetWorldBounds(includeHidden: true));
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int? PickObject(CameraDefinition camera, double normalizedX, double normalizedY, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        sceneGate.Wait();
        try
        {
            double x = Math.Clamp(normalizedX, 0.0, 1.0) * width;
            double y = Math.Clamp(normalizedY, 0.0, 1.0) * height;
            Vec3 direction = RayTracer.RayDirection(
                x,
                y,
                width,
                height,
                camera.ToBasis(),
                camera.FieldOfViewDegrees);
            Hit? hit = scene.Intersect(new Ray(camera.Position, direction));
            if (hit is not { GroupId: >= 0 })
                return null;

            // Viewport clicks select the highest imported/group node so transforms
            // move the complete asset. Child nodes remain directly selectable in
            // the hierarchy tree for precise editing.
            return scene.HighestAncestorById(hit.GroupId)?.Id ?? hit.GroupId;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public ComposerFrame Render(
        ComposerRendererKind renderer,
        CameraDefinition camera,
        int width,
        int height,
        bool interactive,
        CancellationToken cancellationToken,
        ComposerGizmoMode gizmoMode = ComposerGizmoMode.Translate,
        bool objectGizmoOnly = false)
    {
        _ = objectGizmoOnly; // Retained for call-site compatibility; Object mode is always gizmo-only now.
        sceneGate.Wait(cancellationToken);
        try
        {
            if (scene.Triangles.Count == 0)
                throw new InvalidOperationException("Add or open a model before rendering.");

            switch (renderer)
            {
                case ComposerRendererKind.VulkanRaster:
                    VulkanSceneComputeRenderer.ReleasePreparedScene();
                    VulkanRasterRenderer.TryRefreshPreparedGeometry(scene, cancellationToken, out _);
                    break;
                case ComposerRendererKind.VulkanCompute:
                    VulkanRasterRenderer.ReleasePreparedScene();
                    break;
                default:
                    VulkanSceneComputeRenderer.ReleasePreparedScene();
                    VulkanRasterRenderer.ReleasePreparedScene();
                    break;
            }

            if (renderer == ComposerRendererKind.Raster &&
                (rasterCache == null || !rasterCache.IsCurrent(scene)))
            {
                rasterCache = ShadowRasterRenderer.BuildCache(scene, cancellationToken);
            }

            VulkanRasterMeshEditPreview? meshEditPreview = renderer == ComposerRendererKind.VulkanRaster
                ? CreateVulkanMeshEditPreview()
                : null;
            VulkanRasterTransformPreview? transformPreview = renderer == ComposerRendererKind.VulkanRaster && meshEditPreview == null
                ? CreateVulkanTransformPreview()
                : null;

            Stopwatch stopwatch = Stopwatch.StartNew();
            string details;
            RenderImage image = renderer switch
            {
                ComposerRendererKind.Raster => ShadowRasterRenderer.Render(
                    rasterCache ?? throw new InvalidOperationException("Raster cache was not prepared."),
                    camera.Position,
                    camera.ToBasis(),
                    width,
                    height,
                    cancellationToken,
                    out details,
                    interactiveFast: interactive),

                ComposerRendererKind.VulkanRaster => VulkanRasterRenderer.Render(
                    scene,
                    camera.Position,
                    camera.ToBasis(),
                    width,
                    height,
                    transformPreview,
                    meshEditPreview,
                    cancellationToken,
                    out details),

                ComposerRendererKind.VulkanCompute => VulkanSceneComputeRenderer.Render(
                    scene,
                    camera.Position,
                    camera.ToBasis(),
                    width,
                    height,
                    bounceCount: 0,
                    sampleIndex: 0,
                    sampleCount: 1,
                    cancellationToken: cancellationToken,
                    details: out details,
                    progressCallback: null,
                    settings: new RenderSettings
                    {
                        Width = width,
                        Height = height,
                        Backend = RenderBackend.VulkanGpu,
                        PathBounceCount = 0,
                        Exposure = 1.0,
                        AmbientStrength = 1.0,
                        UseShadows = true
                    },
                    fieldOfViewDegrees: camera.FieldOfViewDegrees),

                ComposerRendererKind.Cpu => CpuComposerRenderer.Render(
                    scene,
                    camera,
                    width,
                    height,
                    cancellationToken,
                    out details),

                _ => throw new ArgumentOutOfRangeException(nameof(renderer), renderer, "Unknown renderer.")
            };

            if (selectionMode != ComposerSelectionMode.Object)
            {
                if (meshHoverVisible && TryBuildMeshHoverVisual(out ComposerMeshSelectionVisual hoverVisual))
                    ComposerOverlayRenderer.DrawMeshHover(image, camera, hoverVisual);

                if (TryBuildMeshSelectionVisual(out Aabb componentBounds, out ComposerMeshSelectionVisual componentVisual))
                {
                    ComposerOverlayRenderer.DrawSelection(
                        image,
                        camera,
                        componentBounds,
                        componentVisual.Faces,
                        ComposerGizmoMode.Translate,
                        componentVisual,
                        drawBounds: false,
                        axisConstraint: meshMoveAxisLock);
                }
            }
            else if (TryGetSelectionOverlayForRender(out Aabb overlayBounds, out _))
            {
                // Object mode uses the transform gizmo as the sole selection cue.
                // Do not restore bounds or a sampled triangle wireframe when a drag
                // finishes; those extra outlines obscure the shaded result.
                ComposerOverlayRenderer.DrawSelection(
                    image,
                    camera,
                    overlayBounds,
                    selectedTriangles: null,
                    gizmoMode: gizmoMode,
                    drawBounds: false);
            }

            stopwatch.Stop();
            return new ComposerFrame(image, stopwatch.Elapsed.TotalMilliseconds, details);
        }
        catch (OutOfMemoryException ex) when (renderer is ComposerRendererKind.VulkanRaster or ComposerRendererKind.VulkanCompute)
        {
            VulkanSceneComputeRenderer.ReleasePreparedScene();
            VulkanRasterRenderer.ReleasePreparedScene();
            throw new InvalidOperationException(
                "Vulkan could not reserve enough memory for the complete scene. The scene cache was released.",
                ex);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    private VulkanRasterTransformPreview? CreateVulkanTransformPreview()
    {
        if (selectedObjectId is not int selectedId ||
            scene.GroupById(selectedId) is not SceneObjectGroup target ||
            IsIdentityTransform(target.Position, target.Rotation, target.Scale))
        {
            return null;
        }

        return new VulkanRasterTransformPreview(
            target.Id,
            target.SelfAndDescendants().Select(group => group.Id),
            target.Pivot,
            target.Position,
            target.Rotation,
            target.Scale);
    }

    private bool TryGetSelectionOverlayForRender(
        out Aabb bounds,
        out IReadOnlyList<Triangle> triangles)
    {
        if (selectedOverlayBounds is not Aabb baseBounds)
        {
            bounds = default;
            triangles = Array.Empty<Triangle>();
            return false;
        }

        if (selectedObjectId is not int selectedId ||
            scene.GroupById(selectedId) is not SceneObjectGroup target ||
            IsIdentityTransform(target.Position, target.Rotation, target.Scale))
        {
            bounds = baseBounds;
            triangles = selectedOverlayTriangles;
            return true;
        }

        bounds = TransformBounds(baseBounds, target);
        Triangle[] transformed = new Triangle[selectedOverlayTriangles.Count];
        for (int i = 0; i < transformed.Length; i++)
            transformed[i] = TransformPreviewTriangle(selectedOverlayTriangles[i], target);
        triangles = transformed;
        return true;
    }

    private static Aabb TransformBounds(Aabb source, SceneObjectGroup target)
    {
        Vec3[] corners =
        [
            new(source.Min.X, source.Min.Y, source.Min.Z),
            new(source.Max.X, source.Min.Y, source.Min.Z),
            new(source.Max.X, source.Max.Y, source.Min.Z),
            new(source.Min.X, source.Max.Y, source.Min.Z),
            new(source.Min.X, source.Min.Y, source.Max.Z),
            new(source.Max.X, source.Min.Y, source.Max.Z),
            new(source.Max.X, source.Max.Y, source.Max.Z),
            new(source.Min.X, source.Max.Y, source.Max.Z)
        ];

        Vec3 first = target.TransformPoint(corners[0]);
        Vec3 min = first;
        Vec3 max = first;
        for (int i = 1; i < corners.Length; i++)
        {
            Vec3 point = target.TransformPoint(corners[i]);
            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }
        return new Aabb(min, max);
    }

    private static Triangle TransformPreviewTriangle(Triangle triangle, SceneObjectGroup target) => new(
        target.TransformPoint(triangle.A),
        target.TransformPoint(triangle.B),
        target.TransformPoint(triangle.C),
        triangle.UvA,
        triangle.UvB,
        triangle.UvC,
        target.TransformNormal(triangle.NormalA),
        target.TransformNormal(triangle.NormalB),
        target.TransformNormal(triangle.NormalC),
        triangle.Material,
        triangle.GroupId);

    public void Dispose()
    {
        sceneGate.Wait();
        try
        {
            ReleaseRendererCaches();
            scene = CreateEmptyScene();
            document = new SceneDocument(scene);
            ClearSelectionState();
        }
        finally
        {
            sceneGate.Release();
            sceneGate.Dispose();
        }
    }

    private static Scene CreateEmptyScene()
    {
        Scene result = new();
        result.Clear();
        result.SetDescription("Untitled Avalonia composition");
        result.Lights.Add(new SceneLight("key", new Vec3(3.0, 4.5, -2.5), new Vec3(1.0, 0.96, 0.88), 5.2, isDefault: true));
        result.Lights.Add(new SceneLight("fill", new Vec3(-3.0, 2.0, 1.5), new Vec3(0.62, 0.76, 1.0), 2.8, isDefault: true));
        return result;
    }

    private void ClearSelectionState()
    {
        selectedObjectId = null;
        selectedTriangleGroupId = null;
        selectedTriangleIndex = null;
        selectedMeshSelection = null;
        hoveredMeshSelection = null;
        meshHoverVisible = true;
        meshMovePreviewLocal = Vec3.Zero;
        meshMovePreviewWorld = Vec3.Zero;
        meshMoveAxisLock = ComposerGizmoAxis.None;
        selectionMode = ComposerSelectionMode.Object;
        selectedOverlayBounds = null;
        selectedOverlayTriangles = Array.Empty<Triangle>();
        meshTopologyByGroup.Clear();
    }

    private void RebuildSelectionOverlayCache()
    {
        if (selectedObjectId is not int selectedId || scene.GroupById(selectedId) is not SceneObjectGroup group)
        {
            selectedOverlayBounds = null;
            selectedOverlayTriangles = Array.Empty<Triangle>();
            return;
        }

        if (selectedTriangleGroupId == selectedId &&
            selectedTriangleIndex is int triangleIndex &&
            triangleIndex >= 0 && triangleIndex < group.LocalTriangles.Count)
        {
            Triangle worldTriangle = TransformTriangleToWorld(group, group.LocalTriangles[triangleIndex]);
            selectedOverlayTriangles = new[] { worldTriangle };
            selectedOverlayBounds = BoundsOf(worldTriangle);
            return;
        }

        List<Triangle> reservoir = new(MaximumCachedSelectionTriangles);
        Random random = new(0x51EC710);
        bool hasBounds = false;
        Vec3 min = Vec3.Zero;
        Vec3 max = Vec3.Zero;
        int seen = 0;

        foreach (Triangle triangle in group.BuildWorldTriangles(includeHidden: true))
        {
            ExpandBounds(triangle.A);
            ExpandBounds(triangle.B);
            ExpandBounds(triangle.C);

            seen++;
            if (reservoir.Count < MaximumCachedSelectionTriangles)
            {
                reservoir.Add(triangle);
            }
            else
            {
                int replacement = random.Next(seen);
                if (replacement < MaximumCachedSelectionTriangles)
                    reservoir[replacement] = triangle;
            }
        }

        selectedOverlayBounds = hasBounds ? new Aabb(min, max) : null;
        selectedOverlayTriangles = reservoir;
        return;

        void ExpandBounds(Vec3 point)
        {
            if (!hasBounds)
            {
                min = point;
                max = point;
                hasBounds = true;
                return;
            }

            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }
    }

    private static Triangle TransformTriangleToWorld(SceneObjectGroup group, Triangle triangle)
    {
        Vec3 a = triangle.A;
        Vec3 b = triangle.B;
        Vec3 c = triangle.C;
        Vec3 normalA = triangle.NormalA;
        Vec3 normalB = triangle.NormalB;
        Vec3 normalC = triangle.NormalC;

        for (SceneObjectGroup? current = group; current != null; current = current.Parent)
        {
            a = current.TransformPoint(a);
            b = current.TransformPoint(b);
            c = current.TransformPoint(c);
            normalA = current.TransformNormal(normalA);
            normalB = current.TransformNormal(normalB);
            normalC = current.TransformNormal(normalC);
        }

        Material material = group.PreviewColorOverride ?? group.ColorOverride ?? triangle.Material;
        return new Triangle(
            a, b, c,
            triangle.UvA, triangle.UvB, triangle.UvC,
            normalA, normalB, normalC,
            material,
            group.Id);
    }

    private static Aabb BoundsOf(Triangle triangle)
    {
        Vec3 min = new(
            Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X)),
            Math.Min(triangle.A.Y, Math.Min(triangle.B.Y, triangle.C.Y)),
            Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z)));
        Vec3 max = new(
            Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X)),
            Math.Max(triangle.A.Y, Math.Max(triangle.B.Y, triangle.C.Y)),
            Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z)));
        return new Aabb(min, max);
    }

    private void ClearSelectionHighlight()
    {
        foreach (SceneObjectGroup group in scene.ObjectGroups.SelectMany(root => root.SelfAndDescendants()))
            group.PreviewColorOverride = null;
    }

    private void ApplySelectionHighlightAndRebuild()
    {
        // Kept as a single history hook. Selection is rendered as a post-process
        // overlay, so no scene rebuild or renderer cache invalidation is required.
        ClearSelectionHighlight();
    }

    private void RefreshRendererCachesAfterGeometryBake(CancellationToken cancellationToken)
    {
        rasterCache = null;
        VulkanSceneComputeRenderer.ReleasePreparedScene();
        if (VulkanRasterRenderer.TryRefreshPreparedGeometry(scene, cancellationToken, out string details))
        {
            LastGeometryRefreshDetails = details;
            return;
        }

        VulkanRasterRenderer.ReleasePreparedScene();
        LastGeometryRefreshDetails = details;
    }

    private void InvalidateRendererCaches()
    {
        rasterCache = null;
        VulkanSceneComputeRenderer.ReleasePreparedScene();
        VulkanRasterRenderer.ReleasePreparedScene();
        LastGeometryRefreshDetails = "Renderer scene caches were invalidated because topology or materials changed.";
    }

    private void ReleaseRendererCaches()
    {
        rasterCache = null;
        VulkanSceneComputeRenderer.DisposeSharedDevice();
        VulkanRasterRenderer.DisposeSharedDevice();
    }

    private static string ResolveScenePath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Select a local scene or model file.", nameof(inputPath));

        string path = Path.GetFullPath(inputPath.Trim());
        if (!File.Exists(path))
            throw new FileNotFoundException("Scene input was not found.", path);
        if (!ComposerFileTypes.IsSupportedPath(path))
            throw new NotSupportedException($"Unsupported scene/model format: {Path.GetExtension(path)}");
        return path;
    }

    private static string ResolveInsertPath(string inputPath)
    {
        string path = ResolveScenePath(inputPath);
        if (ComposerFileTypes.IsBinaryScenePath(path) ||
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Insert accepts standalone 3D model files. Open native or XML scenes instead.");
        }
        return path;
    }

    private static ulong ComputeGeometryHash(IEnumerable<Triangle> triangles)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;

        foreach (Triangle triangle in triangles)
        {
            AddLong(triangle.GroupId);
            AddVec(triangle.A); AddVec(triangle.B); AddVec(triangle.C);
            AddVec(triangle.NormalA); AddVec(triangle.NormalB); AddVec(triangle.NormalC);
        }

        return hash;

        void AddVec(Vec3 value)
        {
            AddLong(BitConverter.DoubleToInt64Bits(value.X));
            AddLong(BitConverter.DoubleToInt64Bits(value.Y));
            AddLong(BitConverter.DoubleToInt64Bits(value.Z));
        }

        void AddLong(long value)
        {
            unchecked
            {
                hash ^= (ulong)value;
                hash *= prime;
            }
        }
    }

    private static bool IsIdentityTransform(Vec3 position, Vec3 rotation, Vec3 scale) =>
        position.Length() <= 1e-12 &&
        rotation.Length() <= 1e-12 &&
        Math.Abs(scale.X - 1.0) <= 1e-12 &&
        Math.Abs(scale.Y - 1.0) <= 1e-12 &&
        Math.Abs(scale.Z - 1.0) <= 1e-12;

    private static void ValidateFinite(Vec3 value, string name)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
            throw new ArgumentException("Vector values must be finite.", name);
    }
}
