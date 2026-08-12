/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 *
 * `ComposerFileTypes` provides shared algorithms/registration behavior without per-instance state.
 *
 * `IsSupportedPath` tests whether supported path is true for the supplied/current value. Keeping the predicate
 * here ensures every caller uses the same definition instead of duplicating a slightly different condition.
 *
 * `IsBinaryScenePath` tests whether binary scene path is true for the supplied/current value. Keeping the
 * predicate here ensures every caller uses the same definition instead of duplicating a slightly different
 * condition.
 */
using Avalonia.Platform.Storage;

namespace LightingShowcase.Composer;

internal static class ComposerFileTypes
{
    public static readonly IReadOnlyList<string> SupportedExtensions =
    [
        ".lscene", ".lsb", ".prop.xml", ".xml", ".glb", ".gltf",
        ".fbx", ".obj", ".3ds", ".ply", ".stl"
    ];

    public static readonly IReadOnlyList<FilePickerFileType> OpenPickerTypes =
    [
        new("All supported scenes and models")
        {
            Patterns = SupportedExtensions.Select(extension => $"*{extension}").ToArray()
        },
        new("LightingShowcase scenes") { Patterns = ["*.lscene", "*.lsb"] },
        new("glTF models") { Patterns = ["*.glb", "*.gltf"] },
        new("FBX models") { Patterns = ["*.fbx"] },
        new("Wavefront OBJ models") { Patterns = ["*.obj"] },
        new("3D Studio models") { Patterns = ["*.3ds"] },
        new("PLY models") { Patterns = ["*.ply"] },
        new("STL models") { Patterns = ["*.stl"] },
        new("Property XML scenes") { Patterns = ["*.prop.xml", "*.xml"] }
    ];

    public static readonly IReadOnlyList<FilePickerFileType> InsertPickerTypes =
    [
        new("Supported 3D models")
        {
            Patterns = ["*.glb", "*.gltf", "*.fbx", "*.obj", "*.3ds", "*.ply", "*.stl"]
        },
        new("glTF models") { Patterns = ["*.glb", "*.gltf"] },
        new("FBX models") { Patterns = ["*.fbx"] },
        new("Wavefront OBJ models") { Patterns = ["*.obj"] },
        new("3D Studio models") { Patterns = ["*.3ds"] },
        new("PLY models") { Patterns = ["*.ply"] },
        new("STL models") { Patterns = ["*.stl"] }
    ];

    public static readonly IReadOnlyList<FilePickerFileType> TexturePickerTypes =
    [
        new("Image textures")
        {
            Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga", "*.gif", "*.psd", "*.hdr"]
        },
        new("PNG images") { Patterns = ["*.png"] },
        new("JPEG images") { Patterns = ["*.jpg", "*.jpeg"] },
        new("Other supported images") { Patterns = ["*.bmp", "*.tga", "*.gif", "*.psd", "*.hdr"] }
    ];

    public static readonly FilePickerFileType ComposerScene = new("LightingShowcase Composer scene")
    {
        Patterns = ["*.lscene"],
        MimeTypes = ["application/octet-stream"]
    };

    public static bool IsSupportedPath(string path) =>
        SupportedExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static bool IsBinaryScenePath(string path) =>
        path.EndsWith(".lscene", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".lsb", StringComparison.OrdinalIgnoreCase);
}
