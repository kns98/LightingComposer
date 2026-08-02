using System.Text.Json;

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
        new("glb", "GLB binary (.glb)", ".glb", "glb")
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

        // Native .lscene/.lsb files already embed decoded texture resources.
        // External PNGs are only required for interchange formats.
        IReadOnlyList<TextureMap> textures = format.IsNativeScene
            ? Array.Empty<TextureMap>()
            : SceneTextureResources.Enumerate(scene);
        Dictionary<TextureMap, string> relativeTexturePaths = new(ReferenceEqualityComparer.Instance);
        List<string> textureFiles = new();
        if (textures.Count > 0)
        {
            string textureDirectory = Path.Combine(directory, "textures");
            Directory.CreateDirectory(textureDirectory);
            HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < textures.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TextureMap texture = textures[i];
                string? preferred = Path.GetFileNameWithoutExtension(texture.SourcePath);
                if (string.IsNullOrWhiteSpace(preferred))
                    preferred = texture.Name;
                string fileName = UniqueFileName(SanitizeFileName(preferred, $"texture-{i + 1:000}"), usedNames) + ".png";
                string absolutePath = Path.Combine(textureDirectory, fileName);
                texture.SavePng(absolutePath);
                string relativePath = Path.Combine("textures", fileName).Replace('\\', '/');
                relativeTexturePaths[texture] = relativePath;
                textureFiles.Add(relativePath);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (format.IsNativeScene)
        {
            BinarySceneFile.Save(scene, primaryPath);
        }
        else
        {
            SceneFormatRegistry.Export(scene, primaryPath, new SceneSaveOptions
            {
                Variant = format.Variant,
                PackageDirectory = directory,
                TexturePathResolver = texture => relativeTexturePaths.TryGetValue(texture, out string? relative) ? relative : null
            });
        }

        cancellationToken.ThrowIfCancellationRequested();
        string[] filesBeforeManifest = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string manifestPath = Path.Combine(directory, "export-manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            format = format.Id,
            formatName = format.DisplayName,
            primaryFile = Path.GetFileName(primaryPath),
            textureFiles,
            files = filesBeforeManifest,
            note = format.IsNativeScene
                ? "Texture resources are embedded in the native scene file."
                : "Textures are included for portability. Formats without texture channels retain geometry and include the texture files as related resources."
        }, new JsonSerializerOptions { WriteIndented = true }));

        string[] allFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SceneExportPackageResult(directory, primaryPath, allFiles, textureFiles);
    }

    private static string CreateUniqueDirectory(string parent, string preferredName)
    {
        string candidate = Path.Combine(parent, preferredName);
        for (int suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
            candidate = Path.Combine(parent, $"{preferredName}-{suffix}");
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static string UniqueFileName(string preferred, HashSet<string> used)
    {
        string candidate = preferred;
        for (int suffix = 2; !used.Add(candidate); suffix++)
            candidate = $"{preferred}-{suffix}";
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
}
