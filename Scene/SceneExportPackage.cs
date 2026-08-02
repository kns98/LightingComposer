namespace LightingShowcase.SceneGraph;

/// <summary>One format/variant offered by the portable export workflow.</summary>
public sealed record SceneExportFormat(
    string Id,
    string DisplayName,
    string Extension,
    string? Variant = null,
    bool IsNativeScene = false)
{
    public override string ToString() => DisplayName;
}

/// <summary>Known export formats supported by the loaded exporter projects.</summary>
public static class SceneExportFormats
{
    public static IReadOnlyList<SceneExportFormat> All { get; } =
    [
        new("lscene", "LightingShowcase scene (.lscene)", ".lscene", IsNativeScene: true),
        new("lsb", "LightingShowcase binary scene (.lsb)", ".lsb", IsNativeScene: true),
        new("prop-xml", "Property XML (.prop.xml)", ".prop.xml"),
        new("xml", "XML scene (.xml)", ".xml"),
        new("obj", "Wavefront OBJ (.obj)", ".obj"),
        new("stl-binary", "Binary STL (.stl)", ".stl", "binary"),
        new("stl-ascii", "ASCII STL (.stl)", ".stl", "ascii"),
        new("ply-binary", "Binary PLY (.ply)", ".ply", "binary"),
        new("ply-ascii", "ASCII PLY (.ply)", ".ply", "ascii"),
        new("3ds", "3D Studio (.3ds)", ".3ds"),
        new("fbx-binary", "Binary FBX (.fbx)", ".fbx", "binary"),
        new("fbx-ascii", "ASCII FBX (.fbx)", ".fbx", "ascii"),
        new("gltf", "glTF JSON (.gltf)", ".gltf", "gltf"),
        new("glb", "GLB (.glb with external resources)", ".glb", "glb")
    ];

    public static SceneExportFormat Find(string id) =>
        All.FirstOrDefault(format => string.Equals(format.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown export format '{id}'.", nameof(id));
}

public sealed record SceneExportPackageResult(
    string DirectoryPath,
    string PrimaryFilePath,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> TextureFiles);

/// <summary>
/// Creates a new portable directory containing the selected model format and
/// every texture resource referenced by the scene.
/// </summary>
public sealed class SceneExportPackageService
{
    public SceneExportPackageResult Export(
        Scene scene,
        string parentDirectory,
        string baseName,
        SceneExportFormat format,
        CancellationToken cancellationToken = default)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(parentDirectory)) throw new ArgumentException("An export parent directory is required.", nameof(parentDirectory));
        if (format == null) throw new ArgumentNullException(nameof(format));

        cancellationToken.ThrowIfCancellationRequested();
        string parent = Path.GetFullPath(parentDirectory);
        Directory.CreateDirectory(parent);
        string safeBaseName = SanitizeFileName(baseName, "scene");
        string directory = CreateUniqueDirectory(parent, $"{safeBaseName}-{format.Id}");
        string primaryPath = Path.Combine(directory, safeBaseName + format.Extension);
        ResourceNameAllocator resourceNames = new();

        // Allocate format-specific companion resources before textures so the
        // numbering is deterministic and every non-primary asset follows the
        // res_0001.ext convention.
        string? materialFileName = string.Equals(format.Id, "obj", StringComparison.OrdinalIgnoreCase)
            ? resourceNames.Next(".mtl")
            : null;
        string? bufferFileName =
            string.Equals(format.Id, "gltf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format.Id, "glb", StringComparison.OrdinalIgnoreCase)
                ? resourceNames.Next(".bin")
                : null;

        IReadOnlyList<TextureMap> textures = SceneTextureResources.Enumerate(scene);
        Dictionary<TextureMap, string> relativeTexturePaths = new(ReferenceEqualityComparer.Instance);
        List<string> textureFiles = new();
        foreach (TextureMap texture in textures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = resourceNames.Next(TextureExtension(texture));
            string absolutePath = Path.Combine(directory, fileName);
            texture.SavePng(absolutePath);
            relativeTexturePaths[texture] = fileName;
            textureFiles.Add(fileName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (format.IsNativeScene)
        {
            BinarySceneFile.Save(scene, primaryPath, new BinarySceneSaveOptions
            {
                EmbedTexturePixels = false,
                TexturePathResolver = texture => relativeTexturePaths.TryGetValue(texture, out string? relative) ? relative : null
            });
        }
        else
        {
            SceneFormatRegistry.Export(scene, primaryPath, new SceneSaveOptions
            {
                Variant = format.Variant,
                PackageDirectory = directory,
                TexturePathResolver = texture => relativeTexturePaths.TryGetValue(texture, out string? relative) ? relative : null,
                BufferFileName = bufferFileName,
                MaterialFileName = materialFileName
            });
        }

        cancellationToken.ThrowIfCancellationRequested();
        string[] allFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SceneExportPackageResult(directory, primaryPath, allFiles, textureFiles);
    }

    private static string TextureExtension(TextureMap texture)
    {
        // TextureMap currently exposes decoded RGBA pixels, so PNG is the
        // lossless portable representation regardless of the original source.
        return ".png";
    }

    private static string CreateUniqueDirectory(string parent, string preferredName)
    {
        string candidate = Path.Combine(parent, preferredName);
        for (int suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
            candidate = Path.Combine(parent, $"{preferredName}-{suffix}");
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        string sanitized = new string(source.Select(ch =>
            ((ch >= 'a' && ch <= 'z') ||
             (ch >= 'A' && ch <= 'Z') ||
             (ch >= '0' && ch <= '9') ||
             ch is '-' or '_')
                ? ch
                : '_').ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private sealed class ResourceNameAllocator
    {
        private int nextNumber = 1;

        public string Next(string extension)
        {
            string normalized = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
            return $"res_{nextNumber++:0000}{normalized.ToLowerInvariant()}";
        }
    }
}
