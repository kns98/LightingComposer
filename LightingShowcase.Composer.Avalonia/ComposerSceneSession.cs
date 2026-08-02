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

internal sealed class ComposerSceneSession : IDisposable
{
    private readonly SemaphoreSlim sceneGate = new(1, 1);
    private Scene scene;
    private SceneDocument document;
    private ShadowRasterRenderer.PreviewCache? rasterCache;
    private int? selectedObjectId;

    private static readonly Material SelectionMaterial = new(
        new Vec3(1.0, 0.34, 0.04),
        emission: 0.08,
        roughness: 0.48);

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

    public IReadOnlyList<SceneObjectInfo> GetObjectInfos() => document.GetObjectInfos();

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

    public bool SetSelectedObject(int? id)
    {
        sceneGate.Wait();
        try
        {
            int? normalized = id.HasValue && scene.GroupById(id.Value) != null ? id : null;
            if (selectedObjectId == normalized)
                return false;

            ClearSelectionHighlight();
            selectedObjectId = normalized;
            if (selectedObjectId is int selectedId && scene.GroupById(selectedId) is SceneObjectGroup group)
                group.PreviewColorOverride = SelectionMaterial;

            scene.RebuildWorldGeometry();
            InvalidateRendererCaches();
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
            selectedObjectId = null;
            ScenePath = null;
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
            if (ComposerFileTypes.IsBinaryScenePath(path))
            {
                loaded.SetDescription(BinarySceneFile.LoadIntoScene(loaded, path));
            }
            else
            {
                loaded.OpenModelFile(path, progress => cancellationToken.ThrowIfCancellationRequested());
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (loaded.Triangles.Count == 0)
                throw new InvalidDataException("The scene contains no renderable triangles.");

            scene = loaded;
            document = new SceneDocument(scene);
            ScenePath = path;
            selectedObjectId = null;
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
            scene.InsertModelFromFile(path, progress => cancellationToken.ThrowIfCancellationRequested());

            List<int> insertedRootIds = scene.ObjectGroups
                .Where(group => !rootsBeforeInsert.Contains(group.Id))
                .Select(group => group.Id)
                .ToList();
            if (insertedRootIds.Count == 0)
                throw new InvalidDataException("The imported model did not create any scene objects.");

            SceneObjectGroup wrapper = scene.WrapRootGroups(
                insertedRootIds,
                Path.GetFileNameWithoutExtension(path));
            ScenePath = null;
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
            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null)
                return false;

            group.Name = string.IsNullOrWhiteSpace(name) ? group.Name : name.Trim();
            group.Visible = visible;
            group.Position = position;
            group.Rotation = rotationRadians;
            group.Scale = scale;
            Scene.RecalculatePivotsToRoot(group.Parent);
            scene.RebuildWorldGeometry();
            ScenePath = null;
            InvalidateRendererCaches();
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
            Scene.RecalculatePivotsToRoot(group.Parent);
            scene.RebuildWorldGeometry();
            ScenePath = null;
            InvalidateRendererCaches();
            return true;
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
            if (deletingSelection)
                selectedObjectId = null;
            ScenePath = null;
            InvalidateRendererCaches();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public int GenerateGridCopies(int id, int copyCount, double spacing, CancellationToken cancellationToken)
    {
        sceneGate.Wait(cancellationToken);
        try
        {
            int created = scene.DuplicateGroupGrid(id, copyCount, spacing, cancellationToken);
            ScenePath = null;
            InvalidateRendererCaches();
            return created;
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
        CancellationToken cancellationToken)
    {
        sceneGate.Wait(cancellationToken);
        try
        {
            if (scene.Triangles.Count == 0)
                throw new InvalidOperationException("Add or open a model before rendering.");

            switch (renderer)
            {
                case ComposerRendererKind.VulkanRaster:
                    VulkanSceneComputeRenderer.ReleasePreparedScene();
                    break;
                case ComposerRendererKind.VulkanCompute:
                    VulkanRasterRenderer.ReleasePreparedScene();
                    break;
                default:
                    VulkanSceneComputeRenderer.ReleasePreparedScene();
                    VulkanRasterRenderer.ReleasePreparedScene();
                    break;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            string details;
            RenderImage image = renderer switch
            {
                ComposerRendererKind.Raster => ShadowRasterRenderer.Render(
                    rasterCache ??= ShadowRasterRenderer.BuildCache(scene, cancellationToken),
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

            if (selectedObjectId is int selectedId && scene.GroupById(selectedId) is SceneObjectGroup selectedGroup)
            {
                ComposerOverlayRenderer.DrawSelection(
                    image,
                    camera,
                    selectedGroup.GetWorldBounds(includeHidden: true));
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

    public void Dispose()
    {
        sceneGate.Wait();
        try
        {
            ReleaseRendererCaches();
            scene = CreateEmptyScene();
            document = new SceneDocument(scene);
            selectedObjectId = null;
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

    private void ClearSelectionHighlight()
    {
        foreach (SceneObjectGroup group in scene.ObjectGroups.SelectMany(root => root.SelfAndDescendants()))
            group.PreviewColorOverride = null;
    }

    private void InvalidateRendererCaches()
    {
        rasterCache = null;
        VulkanSceneComputeRenderer.ReleasePreparedScene();
        VulkanRasterRenderer.ReleasePreparedScene();
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

    private static void ValidateFinite(Vec3 value, string name)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
            throw new ArgumentException("Vector values must be finite.", name);
    }
}
