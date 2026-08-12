/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 *
 * `ObjLoadResult` packages the outputs of a completed operation into one value so callers see a consistent result
 * rather than partially updated out parameters.
 *
 * The `ObjLoadResult` constructor captures `filePath`, `vertexCount`, `faceCount`, `triangleCount`, `details`.
 * Those are the dependencies/initial values the instance needs for its lifetime, so callbacks and later
 * operations use the same objects/configuration rather than looking them up globally.
 *
 * The `ObjLoadProgress` constructor captures `stage`, `percent`, `vertexCount`, `faceCount`, `triangleCount`.
 * Those are the dependencies/initial values the instance needs for its lifetime, so callbacks and later
 * operations use the same objects/configuration rather than looking them up globally.
 */
namespace LightingShowcase.SceneGraph;

/// <summary>Result object returned by the OBJ loader.</summary>
public sealed class ObjLoadResult
{
    public string FilePath { get; }
    public int VertexCount { get; }
    public int FaceCount { get; }
    public int TriangleCount { get; }
    public string? Details { get; }
    public ObjLoadResult(string filePath, int vertexCount, int faceCount, int triangleCount, string? details = null)
    {
        FilePath = filePath;
        VertexCount = vertexCount;
        FaceCount = faceCount;
        TriangleCount = triangleCount;
        Details = details;
    }
}

/// <summary>Progress update emitted while importing OBJ assets.</summary>
public sealed class ObjLoadProgress
{
    public string Stage { get; }
    public int Percent { get; }
    public int VertexCount { get; }
    public int FaceCount { get; }
    public int TriangleCount { get; }
    public ObjLoadProgress(string stage, int percent, int vertexCount, int faceCount, int triangleCount)
    {
        Stage = stage;
        Percent = Math.Max(0, Math.Min(100, percent));
        VertexCount = vertexCount;
        FaceCount = faceCount;
        TriangleCount = triangleCount;
    }
}
