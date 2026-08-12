/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
namespace LightingShowcase.CommandLine;

internal sealed class ResolvedSceneInput
{
    public required string ScenePath { get; init; }
    public required string AssetDirectory { get; init; }
}

// SceneInputResolver turns flexible user/file input into a canonical internal value, centralizing
// path/format/identity rules that callers should not duplicate.
internal static class SceneInputResolver
{
    public static ResolvedSceneInput Resolve(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("A local scene/model file path is required.", nameof(input));

        if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
            throw new NotSupportedException("Remote scene URLs are not supported. Place the scene and its assets in a local directory.");

        string scenePath = Path.GetFullPath(input);
        if (!File.Exists(scenePath))
            throw new FileNotFoundException("Scene input was not found.", scenePath);
        if (string.Equals(Path.GetExtension(scenePath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("ZIP scene packages are not supported. Extract the files and pass the local scene path directly.");
        if (!SupportedSceneFormats.IsSupportedPath(scenePath))
            throw new NotSupportedException($"Unsupported scene/model format. Supported inputs: {SupportedSceneFormats.Describe()}.");

        string assetDirectory = Path.GetDirectoryName(scenePath)
            ?? throw new InvalidOperationException("The scene path does not have a parent directory.");

        return new ResolvedSceneInput
        {
            ScenePath = scenePath,
            AssetDirectory = assetDirectory
        };
    }
}
