// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/SupportedSceneFormats.cs
// Purpose: Supported scene and model formats.
//
// Single source of truth for every extension the editor can open, with helpers to test and describe paths.
// -----------------------------------------------------------------------------

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

    /// <summary>Returns true when the path ends with a supported scene extension.</summary>
    public static bool IsSupportedPath(string path) =>
        Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true when the path ends with a binary scene extension.</summary>
    public static bool IsBinaryScenePath(string path) =>
        BinarySceneExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the supported scene extensions as a comma-separated list.</summary>
    public static string Describe() => string.Join(", ", Extensions);
}
