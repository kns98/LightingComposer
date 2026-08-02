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
