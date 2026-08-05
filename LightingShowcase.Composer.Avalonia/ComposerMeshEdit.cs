using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Selection granularity used by the composer. Object mode keeps the existing
/// whole-object transform workflow; the three mesh modes expose welded topology.
/// </summary>
internal enum ComposerSelectionMode
{
    Object,
    Vertex,
    Edge,
    Face
}

internal readonly record struct ComposerMeshSelection(
    int GroupId,
    ComposerSelectionMode Mode,
    int ElementIndex);

internal sealed record ComposerMeshPickResult(
    int GroupId,
    ComposerSelectionMode Mode,
    int ElementIndex,
    string Label);

internal readonly record struct ComposerMeshEdge(int A, int B);

internal readonly record struct ComposerMeshFace(int TriangleIndex, int A, int B, int C);

internal readonly record struct ComposerMeshTriangleMove(int TriangleIndex, byte CornerMask);

internal readonly record struct ComposerWorldEdge(Vec3 A, Vec3 B);

/// <summary>Renderer-independent component highlight payload.</summary>
internal sealed record ComposerMeshSelectionVisual(
    ComposerSelectionMode Mode,
    IReadOnlyList<Vec3> Points,
    IReadOnlyList<ComposerWorldEdge> Edges,
    IReadOnlyList<Triangle> Faces);

/// <summary>
/// Indexed topology reconstructed from the engine's immutable triangle soup.
/// Vertices within a scale-aware tolerance are welded. UVs and authored normals
/// remain per-corner in the source triangles, while edit movement is shared by
/// every corner that references the same welded vertex.
/// </summary>
internal sealed class ComposerMeshTopology
{
    private readonly int[,] triangleVertexIds;
    private readonly Dictionary<EdgeKey, int> edgeIndexByKey;

    private ComposerMeshTopology(
        Vec3[] vertices,
        ComposerMeshEdge[] edges,
        ComposerMeshFace[] faces,
        int[,] triangleVertexIds,
        Dictionary<EdgeKey, int> edgeIndexByKey,
        double weldTolerance)
    {
        Vertices = vertices;
        Edges = edges;
        Faces = faces;
        this.triangleVertexIds = triangleVertexIds;
        this.edgeIndexByKey = edgeIndexByKey;
        WeldTolerance = weldTolerance;
    }

    public IReadOnlyList<Vec3> Vertices { get; }
    public IReadOnlyList<ComposerMeshEdge> Edges { get; }
    public IReadOnlyList<ComposerMeshFace> Faces { get; }
    public double WeldTolerance { get; }

    public static ComposerMeshTopology Build(IReadOnlyList<Triangle> triangles, double? requestedTolerance = null)
    {
        ArgumentNullException.ThrowIfNull(triangles);

        double tolerance = requestedTolerance.HasValue &&
                           requestedTolerance.Value > 0.0 &&
                           double.IsFinite(requestedTolerance.Value)
            ? requestedTolerance.Value
            : ComputeTolerance(triangles);
        tolerance = Math.Max(1e-9, tolerance);

        Dictionary<VertexKey, List<int>> vertexIndicesByCell = new(Math.Max(4, triangles.Count * 2));
        List<Vec3> sums = new(Math.Max(4, triangles.Count));
        List<int> counts = new(Math.Max(4, triangles.Count));
        int[,] triangleIds = new int[triangles.Count, 3];

        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            Triangle triangle = triangles[triangleIndex];
            triangleIds[triangleIndex, 0] = GetVertex(triangle.A);
            triangleIds[triangleIndex, 1] = GetVertex(triangle.B);
            triangleIds[triangleIndex, 2] = GetVertex(triangle.C);
        }

        Vec3[] vertices = new Vec3[sums.Count];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = sums[i] / Math.Max(1, counts[i]);

        Dictionary<EdgeKey, int> edgeIndexByKey = new(Math.Max(4, triangles.Count * 2));
        List<ComposerMeshEdge> edges = new(Math.Max(4, triangles.Count * 2));
        ComposerMeshFace[] faces = new ComposerMeshFace[triangles.Count];
        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            int a = triangleIds[triangleIndex, 0];
            int b = triangleIds[triangleIndex, 1];
            int c = triangleIds[triangleIndex, 2];
            faces[triangleIndex] = new ComposerMeshFace(triangleIndex, a, b, c);
            AddEdge(a, b);
            AddEdge(b, c);
            AddEdge(c, a);
        }

        return new ComposerMeshTopology(
            vertices,
            edges.ToArray(),
            faces,
            triangleIds,
            edgeIndexByKey,
            tolerance);

        int GetVertex(Vec3 point)
        {
            VertexKey key = VertexKey.From(point, tolerance);
            int bestIndex = -1;
            double bestDistanceSquared = tolerance * tolerance;

            // Quantization alone is not enough: two points can lie on opposite
            // sides of a cell boundary while still being closer than the weld
            // distance. Search the 27 neighboring cells and verify the actual
            // Euclidean distance before declaring the positions common.
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                VertexKey neighbor = key.Offset(x, y, z);
                if (!vertexIndicesByCell.TryGetValue(neighbor, out List<int>? candidates))
                    continue;

                foreach (int candidate in candidates)
                {
                    Vec3 representative = sums[candidate] / Math.Max(1, counts[candidate]);
                    Vec3 difference = representative - point;
                    double distanceSquared = difference.Dot(difference);
                    if (distanceSquared <= bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        bestIndex = candidate;
                    }
                }
            }

            if (bestIndex >= 0)
            {
                sums[bestIndex] += point;
                counts[bestIndex]++;
                return bestIndex;
            }

            int index = sums.Count;
            sums.Add(point);
            counts.Add(1);
            if (!vertexIndicesByCell.TryGetValue(key, out List<int>? cell))
            {
                cell = new List<int>(1);
                vertexIndicesByCell.Add(key, cell);
            }
            cell.Add(index);
            return index;
        }

        void AddEdge(int a, int b)
        {
            if (a == b)
                return;
            EdgeKey key = new(a, b);
            if (edgeIndexByKey.ContainsKey(key))
                return;
            edgeIndexByKey[key] = edges.Count;
            edges.Add(new ComposerMeshEdge(key.A, key.B));
        }
    }

    public int FindEdgeIndex(int a, int b) =>
        edgeIndexByKey.TryGetValue(new EdgeKey(a, b), out int index) ? index : -1;

    public IReadOnlySet<int> VertexSet(ComposerMeshSelection selection)
    {
        HashSet<int> result = new();
        switch (selection.Mode)
        {
            case ComposerSelectionMode.Vertex when selection.ElementIndex >= 0 && selection.ElementIndex < Vertices.Count:
                result.Add(selection.ElementIndex);
                break;
            case ComposerSelectionMode.Edge when selection.ElementIndex >= 0 && selection.ElementIndex < Edges.Count:
            {
                ComposerMeshEdge edge = Edges[selection.ElementIndex];
                result.Add(edge.A);
                result.Add(edge.B);
                break;
            }
            case ComposerSelectionMode.Face when selection.ElementIndex >= 0 && selection.ElementIndex < Faces.Count:
            {
                ComposerMeshFace face = Faces[selection.ElementIndex];
                result.Add(face.A);
                result.Add(face.B);
                result.Add(face.C);
                break;
            }
        }
        return result;
    }

    public IReadOnlyList<ComposerMeshTriangleMove> TriangleMoves(ComposerMeshSelection selection)
    {
        IReadOnlySet<int> selectedVertices = VertexSet(selection);
        if (selectedVertices.Count == 0)
            return Array.Empty<ComposerMeshTriangleMove>();

        List<ComposerMeshTriangleMove> result = new();
        for (int triangleIndex = 0; triangleIndex < Faces.Count; triangleIndex++)
        {
            byte mask = 0;
            if (selectedVertices.Contains(triangleVertexIds[triangleIndex, 0])) mask |= 1;
            if (selectedVertices.Contains(triangleVertexIds[triangleIndex, 1])) mask |= 2;
            if (selectedVertices.Contains(triangleVertexIds[triangleIndex, 2])) mask |= 4;
            if (mask != 0)
                result.Add(new ComposerMeshTriangleMove(triangleIndex, mask));
        }
        return result;
    }

    public List<Triangle> CreateMovedTriangles(
        IReadOnlyList<Triangle> source,
        ComposerMeshSelection selection,
        Vec3 delta)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlySet<int> selectedVertices = VertexSet(selection);
        if (selectedVertices.Count == 0 || delta.Length() <= 1e-12)
            return source.ToList();

        List<Triangle> result = new(source.Count);
        for (int triangleIndex = 0; triangleIndex < source.Count; triangleIndex++)
        {
            Triangle triangle = source[triangleIndex];
            bool moveA = selectedVertices.Contains(triangleVertexIds[triangleIndex, 0]);
            bool moveB = selectedVertices.Contains(triangleVertexIds[triangleIndex, 1]);
            bool moveC = selectedVertices.Contains(triangleVertexIds[triangleIndex, 2]);
            if (!moveA && !moveB && !moveC)
            {
                result.Add(triangle);
                continue;
            }

            Vec3 a = moveA ? triangle.A + delta : triangle.A;
            Vec3 b = moveB ? triangle.B + delta : triangle.B;
            Vec3 c = moveC ? triangle.C + delta : triangle.C;

            // A component move changes the geometric normal. Rebuild affected
            // faces with derived normals; untouched triangles retain their exact
            // authored UV/normal/material references.
            result.Add(new Triangle(
                a, b, c,
                triangle.UvA, triangle.UvB, triangle.UvC,
                triangle.Material,
                triangle.GroupId));
        }
        return result;
    }

    public List<Triangle> CreateWeldedTriangles(IReadOnlyList<Triangle> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<Triangle> result = new(source.Count);
        for (int triangleIndex = 0; triangleIndex < source.Count; triangleIndex++)
        {
            Triangle triangle = source[triangleIndex];
            Vec3 a = Vertices[triangleVertexIds[triangleIndex, 0]];
            Vec3 b = Vertices[triangleVertexIds[triangleIndex, 1]];
            Vec3 c = Vertices[triangleVertexIds[triangleIndex, 2]];
            result.Add(new Triangle(
                a, b, c,
                triangle.UvA, triangle.UvB, triangle.UvC,
                triangle.NormalA, triangle.NormalB, triangle.NormalC,
                triangle.Material,
                triangle.GroupId));
        }
        return result;
    }

    private static double ComputeTolerance(IReadOnlyList<Triangle> triangles)
    {
        if (triangles.Count == 0)
            return 1e-7;

        bool initialized = false;
        Vec3 min = Vec3.Zero;
        Vec3 max = Vec3.Zero;
        foreach (Triangle triangle in triangles)
        {
            Include(triangle.A);
            Include(triangle.B);
            Include(triangle.C);
        }

        double diagonal = initialized ? (max - min).Length() : 1.0;
        // Keep the automatic tolerance conservative and bounded. This captures
        // normal import-rounding noise without collapsing intentional details in
        // very large models. A future UI can expose an explicit merge distance.
        return Math.Clamp(diagonal * 1e-7, 1e-9, 1e-4);

        void Include(Vec3 point)
        {
            if (!initialized)
            {
                min = point;
                max = point;
                initialized = true;
                return;
            }
            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }
    }

    private readonly record struct VertexKey(long X, long Y, long Z)
    {
        public static VertexKey From(Vec3 value, double tolerance) => new(
            Quantize(value.X, tolerance),
            Quantize(value.Y, tolerance),
            Quantize(value.Z, tolerance));

        public VertexKey Offset(int x, int y, int z) => new(X + x, Y + y, Z + z);

        private static long Quantize(double value, double tolerance)
        {
            double scaled = Math.Round(value / tolerance);
            if (scaled >= long.MaxValue) return long.MaxValue;
            if (scaled <= long.MinValue) return long.MinValue;
            return (long)scaled;
        }
    }

    private readonly record struct EdgeKey
    {
        public EdgeKey(int a, int b)
        {
            A = Math.Min(a, b);
            B = Math.Max(a, b);
        }

        public int A { get; }
        public int B { get; }
    }
}


/// <summary>Undo record for one component-move bake without cloning the full scene.</summary>
internal sealed class MeshComponentMoveEditCommand : IComposerEditCommand
{
    private readonly int groupId;
    private readonly Triangle[] before;
    private readonly Triangle[] after;
    private readonly string description;

    public MeshComponentMoveEditCommand(
        int groupId,
        IEnumerable<Triangle> before,
        IEnumerable<Triangle> after,
        string description)
    {
        this.groupId = groupId;
        this.before = before.ToArray();
        this.after = after.ToArray();
        this.description = description;
    }

    public string Description => description;
    public int? UndoSelectionId => groupId;
    public int? RedoSelectionId => groupId;

    public void Undo(Scene scene) => Restore(scene, before);
    public void Redo(Scene scene) => Restore(scene, after);

    private void Restore(Scene scene, IReadOnlyList<Triangle> triangles)
    {
        SceneObjectGroup group = scene.GroupById(groupId)
            ?? throw new InvalidOperationException("The edited mesh no longer exists.");
        group.LocalTriangles.Clear();
        group.LocalTriangles.AddRange(triangles);
        group.RecalculatePivot();
        Scene.RecalculatePivotsToRoot(group.Parent);
        scene.RebuildWorldGeometry();
    }
}
