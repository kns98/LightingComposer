/*
 * This is an extensibility seam. Callers discover capabilities through a registry/interface instead of referencing
 * every concrete format or object-library assembly, allowing plugins to be added while the core scene/editor code
 * remains unchanged.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

// SceneLoadOptions collects one operation/backend’s tunable choices and provides a single validation/defaulting
// boundary before those choices affect execution.
public sealed class SceneLoadOptions
{
    public Material FallbackMaterial { get; init; } = new(new Vec3(0.82, 0.82, 0.78));
    public double TargetSize { get; init; } = 2.15;
    public Vec3? TargetCenter { get; init; }
    public double FloorY { get; init; } = -1.48;
    public bool ReplaceScene { get; init; }
    public double? SimplifyKeepFraction { get; init; }
    public Action<ObjLoadProgress>? Progress { get; init; }
}

// SceneSaveOptions collects one operation/backend’s tunable choices and provides a single validation/defaulting
// boundary before those choices affect execution.
public sealed class SceneSaveOptions
{
    public string? Variant { get; init; }

    /// <summary>
    /// Resolves an in-memory texture to a URI relative to the exported primary
    /// file. Package exports use this to make OBJ, glTF, and XML references
    /// portable instead of retaining absolute source paths.
    /// </summary>
    public Func<TextureMap, string?>? TexturePathResolver { get; init; }

    /// <summary>Directory containing the primary export and related resources.</summary>
    public string? PackageDirectory { get; init; }

    /// <summary>Optional package-relative name for the glTF binary buffer.</summary>
    public string? BufferFileName { get; init; }

    /// <summary>Optional package-relative name for the OBJ material library.</summary>
    public string? MaterialFileName { get; init; }

    /// <summary>
    /// When true, exporters may ignore editor-only chunk boundaries, merge
    /// compatible geometry, and weld shared vertices for faster runtime loading.
    /// The current implementation is used by glTF/GLB export.
    /// </summary>
    public bool OptimizeGeometry { get; init; }
}

// ISceneFormatPlugin defines a capability boundary: callers depend on the contract rather than the concrete
// plugin/backend implementing it. New implementations can therefore participate without changing the core caller.
public interface ISceneFormatPlugin
{
    string FormatId { get; }
    string DisplayName { get; }
    IReadOnlyList<string> Extensions { get; }
    // CanImport is a read-only predicate over the object’s existing state; it exists so callers share one exact
    // condition when enabling commands or deciding whether an operation is applicable.
    bool CanImport { get; }
    // CanExport is a read-only predicate over the object’s existing state; it exists so callers share one exact
    // condition when enabling commands or deciding whether an operation is applicable.
    bool CanExport { get; }
    bool CarriesLights { get; }
    IReadOnlyList<string> ExportVariants { get; }

    ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options);
    void Export(Scene scene, string filePath, SceneSaveOptions options);
}
