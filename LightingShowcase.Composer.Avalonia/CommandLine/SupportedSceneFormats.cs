/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
namespace LightingShowcase.CommandLine;

/// <summary>
/// Single source of truth for every file type the LightingShowcase editor can open
/// as a complete scene or replacement model.
/// </summary>
public static class SupportedSceneFormats
{
    // Keep native scene formats first, then richer external scene formats,
    // followed by geometry-focused interchange formats.
    public static readonly IReadOnlyList<string> Extensions = new[]
    {
        ".lscene",
        ".lsb",
        ".prop.xml",
        ".xml",
        ".glb",
        ".gltf",
        ".fbx",
        ".obj",
        ".3ds",
        ".ply",
        ".stl"
    };

    public static readonly IReadOnlyList<string> BinarySceneExtensions = new[]
    {
        ".lscene",
        ".lsb"
    };

    // IsSupportedPath tests whether supported path is true for the supplied/current value. Keeping the predicate
    // here ensures every caller uses the same definition instead of duplicating a slightly different condition.
    public static bool IsSupportedPath(string path) =>
        Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    // IsBinaryScenePath tests whether binary scene path is true for the supplied/current value. Keeping the
    // predicate here ensures every caller uses the same definition instead of duplicating a slightly different
    // condition.
    public static bool IsBinaryScenePath(string path) =>
        BinarySceneExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static string Describe() => string.Join(", ", Extensions);
}
