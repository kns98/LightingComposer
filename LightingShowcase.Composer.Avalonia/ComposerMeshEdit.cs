using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Selection granularity used by the composer. Object mode keeps the existing
/// whole-object transform workflow; the three mesh modes expose welded topology.
/// Face mode operates on authored polygon faces rather than individual render triangles.
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

/// <summary>
/// One editable polygon face. TriangleIndices are the renderer triangles that
/// make up the face; BoundaryLoop contains the welded vertices in perimeter order.
/// </summary>
internal sealed record ComposerMeshFace(
    int Index,
    int[] TriangleIndices,
    int[] VertexIndices,
    int[] BoundaryLoop);

internal readonly record struct ComposerMeshTriangleMove(int TriangleIndex, byte CornerMask);

internal sealed record ComposerMeshTopologyEditResult(
    IReadOnlyList<Triangle> Triangles,
    IReadOnlyList<int[]> LogicalFaceTriangleGroups);

internal readonly record struct ComposerWorldEdge(Vec3 A, Vec3 B);

/// <summary>Renderer-independent component highlight payload.</summary>
internal sealed record ComposerMeshSelectionVisual(
    ComposerSelectionMode Mode,
    IReadOnlyList<Vec3> Points,
    IReadOnlyList<ComposerWorldEdge> Edges,
    IReadOnlyList<Triangle> Faces);

/// <summary>
/// Indexed topology reconstructed from the engine's immutable triangle soup.
/// Vertices within a scale-aware tolerance are welded. Primitive-created meshes
/// additionally recover their authored polygon faces (for example a Cube has six
/// quad faces even though the renderer stores twelve triangles).
/// </summary>
internal sealed class ComposerMeshTopology
{
    private readonly int[,] triangleVertexIds;
    private readonly int[] triangleFaceIds;
    private readonly Dictionary<EdgeKey, int> edgeIndexByKey;

    private ComposerMeshTopology(
        Vec3[] vertices,
        ComposerMeshEdge[] edges,
        ComposerMeshFace[] faces,
        int[,] triangleVertexIds,
        int[] triangleFaceIds,
        Dictionary<EdgeKey, int> edgeIndexByKey,
        double weldTolerance)
    {
        Vertices = vertices;
        Edges = edges;
        Faces = faces;
        this.triangleVertexIds = triangleVertexIds;
        this.triangleFaceIds = triangleFaceIds;
        this.edgeIndexByKey = edgeIndexByKey;
        WeldTolerance = weldTolerance;
    }

    public IReadOnlyList<Vec3> Vertices { get; }
    public IReadOnlyList<ComposerMeshEdge> Edges { get; }
    public IReadOnlyList<ComposerMeshFace> Faces { get; }
    public double WeldTolerance { get; }
    public int TriangleCount => triangleVertexIds.GetLength(0);

    public static ComposerMeshTopology Build(SceneObjectGroup group, double? requestedTolerance = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ComposerMeshTopology topology = BuildCore(
            group.LocalTriangles,
            group.PrimitiveKind ?? group.PrimitiveSourceName,
            group.PrimitiveParameters,
            group.LogicalFaceTriangleGroups,
            requestedTolerance);

        // Once a face grouping has been proven structurally valid, retain it on
        // the object. This makes a logical face real editor topology rather than
        // a transient guess made again on every selection/pick.
        group.SetLogicalFaceTriangleGroups(topology.Faces.Select(face => face.TriangleIndices));
        return topology;
    }

    public static ComposerMeshTopology Build(IReadOnlyList<Triangle> triangles, double? requestedTolerance = null) =>
        BuildCore(triangles, null, null, null, requestedTolerance);

    private static ComposerMeshTopology BuildCore(
        IReadOnlyList<Triangle> triangles,
        string? primitiveKind,
        IReadOnlyDictionary<string, double>? primitiveParameters,
        IReadOnlyList<int[]>? storedLogicalFaces,
        double? requestedTolerance)
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

        List<int[]> triangleGroups = new();
        bool usedStoredTopology = storedLogicalFaces != null && storedLogicalFaces.Count > 0 &&
            TryValidateFacePartition(storedLogicalFaces, triangles, triangleIds, vertices, tolerance, requirePlanar: false, requireUvContinuity: false, out triangleGroups);

        if (!usedStoredTopology)
        {
            List<int[]>? authoredGroups = BuildPrimitiveFaceGroups(primitiveKind, primitiveParameters, triangles.Count);
            bool usedAuthoredTopology = authoredGroups != null &&
                TryValidateFacePartition(authoredGroups, triangles, triangleIds, vertices, tolerance, requirePlanar: false, requireUvContinuity: false, out triangleGroups);

            if (!usedAuthoredTopology)
                triangleGroups = BuildFallbackFaceGroups(triangles, triangleIds, vertices, tolerance);
        }

        if (triangleGroups.Count == 0 && triangles.Count > 0)
            triangleGroups = Enumerable.Range(0, triangles.Count).Select(i => new[] { i }).ToList();

        int[] triangleFaceIds = Enumerable.Repeat(-1, triangles.Count).ToArray();
        List<ComposerMeshFace> faces = new(triangleGroups.Count);
        foreach (int[] candidate in triangleGroups)
        {
            int[] group = candidate.Where(i => i >= 0 && i < triangles.Count).Distinct().ToArray();
            if (group.Length == 0 ||
                !TryBuildLogicalBoundary(group, triangles, triangleIds, vertices, tolerance, requirePlanar: false, requireUvContinuity: false, out int[] boundary))
            {
                continue;
            }

            int faceIndex = faces.Count;
            foreach (int triangleIndex in group)
                triangleFaceIds[triangleIndex] = faceIndex;
            int[] faceVertices = group
                .SelectMany(t => new[] { triangleIds[t, 0], triangleIds[t, 1], triangleIds[t, 2] })
                .Distinct()
                .ToArray();
            faces.Add(new ComposerMeshFace(faceIndex, group, faceVertices, boundary));
        }

        // Defensive completion: a triangle that cannot participate in a valid
        // polygon is still a perfectly valid one-triangle logical face.
        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            if (triangleFaceIds[triangleIndex] >= 0)
                continue;
            int faceIndex = faces.Count;
            int[] vertexIds = [triangleIds[triangleIndex, 0], triangleIds[triangleIndex, 1], triangleIds[triangleIndex, 2]];
            faces.Add(new ComposerMeshFace(faceIndex, [triangleIndex], vertexIds.Distinct().ToArray(), vertexIds));
            triangleFaceIds[triangleIndex] = faceIndex;
        }

        // Expose polygon boundary edges, not renderer-internal triangulation
        // diagonals. A cube therefore has 12 editor edges even though its shadow
        // mesh contains 18 unique triangle edges.
        Dictionary<EdgeKey, int> edgeIndexByKey = new(Math.Max(4, faces.Count * 3));
        List<ComposerMeshEdge> edges = new(Math.Max(4, faces.Count * 3));
        foreach (ComposerMeshFace face in faces)
        {
            int[] loop = face.BoundaryLoop;
            for (int i = 0; i < loop.Length; i++)
                AddEdge(loop[i], loop[(i + 1) % loop.Length]);
        }

        return new ComposerMeshTopology(
            vertices,
            edges.ToArray(),
            faces.ToArray(),
            triangleIds,
            triangleFaceIds,
            edgeIndexByKey,
            tolerance);

        int GetVertex(Vec3 point)
        {
            VertexKey key = VertexKey.From(point, tolerance);
            int bestIndex = -1;
            double bestDistanceSquared = tolerance * tolerance;
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
            if (a == b) return;
            EdgeKey key = new(a, b);
            if (edgeIndexByKey.ContainsKey(key)) return;
            edgeIndexByKey[key] = edges.Count;
            edges.Add(new ComposerMeshEdge(key.A, key.B));
        }
    }

    public int FaceIndexForTriangle(int triangleIndex) =>
        triangleIndex >= 0 && triangleIndex < triangleFaceIds.Length ? triangleFaceIds[triangleIndex] : -1;

    public int TriangleVertexId(int triangleIndex, int corner) =>
        triangleIndex >= 0 && triangleIndex < TriangleCount && corner >= 0 && corner < 3
            ? triangleVertexIds[triangleIndex, corner]
            : -1;

    public int PrimaryTriangleIndex(int faceIndex) =>
        faceIndex >= 0 && faceIndex < Faces.Count && Faces[faceIndex].TriangleIndices.Length > 0
            ? Faces[faceIndex].TriangleIndices[0]
            : -1;

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
                foreach (int vertex in Faces[selection.ElementIndex].VertexIndices)
                    result.Add(vertex);
                break;
        }
        return result;
    }

    public IReadOnlyList<ComposerMeshTriangleMove> TriangleMoves(ComposerMeshSelection selection)
    {
        IReadOnlySet<int> selectedVertices = VertexSet(selection);
        if (selectedVertices.Count == 0)
            return Array.Empty<ComposerMeshTriangleMove>();

        List<ComposerMeshTriangleMove> result = new();
        for (int triangleIndex = 0; triangleIndex < TriangleCount; triangleIndex++)
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

    public List<Triangle> CreateMovedTriangles(IReadOnlyList<Triangle> source, ComposerMeshSelection selection, Vec3 delta)
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
            result.Add(new Triangle(a, b, c, triangle.UvA, triangle.UvB, triangle.UvC, triangle.Material, triangle.GroupId));
        }
        return result;
    }

    public List<Triangle> CreateExtrudedFaceTriangles(IReadOnlyList<Triangle> source, int faceIndex, double distance) =>
        CreateExtrudedFaceEdit(source, faceIndex, distance).Triangles.ToList();

    public ComposerMeshTopologyEditResult CreateExtrudedFaceEdit(IReadOnlyList<Triangle> source, int faceIndex, double distance)
    {
        if (faceIndex < 0 || faceIndex >= Faces.Count)
            return UnchangedEdit(source);
        if (!double.IsFinite(distance) || Math.Abs(distance) <= 1e-9)
            return UnchangedEdit(source);

        ComposerMeshFace face = Faces[faceIndex];
        if (face.BoundaryLoop.Length < 3)
            return UnchangedEdit(source);
        Vec3 faceNormal = FaceNormal(source, face);
        if (faceNormal.Length() <= 1e-8)
            return UnchangedEdit(source);

        // Extrusion distance is signed in object-space semantics, not triangle-winding
        // semantics: positive moves toward the exterior of a closed object, negative
        // moves inward. Imported meshes may contain a logical face whose source
        // triangles are wound backwards, so resolve the exterior direction from the
        // face position relative to the mesh centroid before applying the distance.
        Vec3 faceCenter = Vec3.Zero;
        foreach (int vertex in face.BoundaryLoop) faceCenter += Vertices[vertex];
        faceCenter /= face.BoundaryLoop.Length;
        Vec3 outwardNormal = ResolveExteriorNormal(faceNormal, faceCenter);
        Vec3 delta = outwardNormal * distance;
        HashSet<int> removed = face.TriangleIndices.ToHashSet();
        List<Triangle> result = new(source.Count - removed.Count + removed.Count + face.BoundaryLoop.Length * 2);
        Dictionary<int, int> oldToNew = new();
        for (int i = 0; i < source.Count; i++)
        {
            if (removed.Contains(i)) continue;
            oldToNew[i] = result.Count;
            result.Add(source[i]);
        }
        List<int[]> logicalFaces = RemapUnaffectedFaces(faceIndex, oldToNew);

        int capStart = result.Count;
        foreach (int triangleIndex in face.TriangleIndices)
        {
            Triangle tri = source[triangleIndex];
            Triangle moved = new(
                tri.A + delta, tri.B + delta, tri.C + delta,
                tri.UvA, tri.UvB, tri.UvC, tri.Material, tri.GroupId);
            // The exposed cap should face the object's exterior even when the
            // imported source face was wound inward.
            if (moved.Normal.Dot(outwardNormal) >= 0.0)
                result.Add(moved);
            else
                result.Add(new Triangle(
                    tri.A + delta, tri.C + delta, tri.B + delta,
                    tri.UvA, tri.UvC, tri.UvB, tri.Material, tri.GroupId));
        }

        logicalFaces.Add(Enumerable.Range(capStart, face.TriangleIndices.Length).ToArray());

        Material material = source[face.TriangleIndices[0]].Material;
        int groupId = source[face.TriangleIndices[0]].GroupId;
        for (int i = 0; i < face.BoundaryLoop.Length; i++)
        {
            int va = face.BoundaryLoop[i];
            int vb = face.BoundaryLoop[(i + 1) % face.BoundaryLoop.Length];
            Vec3 a = Vertices[va];
            Vec3 b = Vertices[vb];
            Vec3 a2 = a + delta;
            Vec3 b2 = b + delta;
            Vec2 uva = UvForVertex(source, face, va);
            Vec2 uvb = UvForVertex(source, face, vb);
            int sideStart = result.Count;
            Triangle first = new(a, b, b2, uva, uvb, uvb, material, groupId);
            Triangle second = new(a, b2, a2, uva, uvb, uva, material, groupId);

            Vec3 edgeMidpoint = (a + b) * 0.5;
            Vec3 desiredNormal = distance >= 0.0
                ? edgeMidpoint - faceCenter   // raised extrusion: side faces away from the bump
                : faceCenter - edgeMidpoint;  // inward extrusion: cavity walls face the opening
            desiredNormal -= outwardNormal * desiredNormal.Dot(outwardNormal);

            if (desiredNormal.Length() <= 1e-8 || first.Normal.Dot(desiredNormal) >= 0.0)
            {
                result.Add(first);
                result.Add(second);
            }
            else
            {
                result.Add(new Triangle(a, b2, b, uva, uvb, uvb, material, groupId));
                result.Add(new Triangle(a, a2, b2, uva, uva, uvb, material, groupId));
            }
            logicalFaces.Add([sideStart, sideStart + 1]);
        }
        return new ComposerMeshTopologyEditResult(result, logicalFaces);
    }

    public List<Triangle> CreateInsetFaceTriangles(IReadOnlyList<Triangle> source, int faceIndex, double insetMeters) =>
        CreateInsetFaceEdit(source, faceIndex, insetMeters, recessDepthMeters: 0.0).Triangles.ToList();

    public ComposerMeshTopologyEditResult CreateInsetFaceEdit(IReadOnlyList<Triangle> source, int faceIndex, double insetMeters) =>
        CreateInsetFaceEdit(source, faceIndex, insetMeters, recessDepthMeters: 0.0);

    public ComposerMeshTopologyEditResult CreateInsetFaceEdit(
        IReadOnlyList<Triangle> source,
        int faceIndex,
        double insetMeters,
        double recessDepthMeters)
    {
        if (faceIndex < 0 || faceIndex >= Faces.Count)
            return UnchangedEdit(source);
        if (!double.IsFinite(insetMeters) || insetMeters <= 1e-9)
            return UnchangedEdit(source);
        if (!double.IsFinite(recessDepthMeters))
            return UnchangedEdit(source);

        ComposerMeshFace face = Faces[faceIndex];
        int[] loop = face.BoundaryLoop;
        if (loop.Length < 3)
            return UnchangedEdit(source);

        Vec3 faceNormal = FaceNormal(source, face);
        if (faceNormal.Length() <= 1e-8)
            return UnchangedEdit(source);

        Vec3 center = Vec3.Zero;
        foreach (int vertex in loop) center += Vertices[vertex];
        center /= loop.Length;

        // Work in the face plane so "Inset 0.05 m" means a true 5 cm parallel
        // offset from every polygon edge (for the convex polygon faces generated
        // by Composer primitives), rather than a radial scale toward the centroid.
        Vec3 tangent = Vec3.Zero;
        for (int i = 0; i < loop.Length; i++)
        {
            Vec3 candidate = Vertices[loop[(i + 1) % loop.Length]] - Vertices[loop[i]];
            if (candidate.Length() > 1e-8)
            {
                tangent = candidate.Normalize();
                break;
            }
        }
        if (tangent.Length() <= 1e-8)
            return UnchangedEdit(source);
        Vec3 bitangent = faceNormal.Cross(tangent).Normalize();
        if (bitangent.Length() <= 1e-8)
            return UnchangedEdit(source);

        (double X, double Y)[] outer2 = new (double X, double Y)[loop.Length];
        for (int i = 0; i < loop.Length; i++)
        {
            Vec3 relative = Vertices[loop[i]] - center;
            outer2[i] = (relative.Dot(tangent), relative.Dot(bitangent));
        }

        double signedArea = SignedArea(outer2);
        if (Math.Abs(signedArea) <= 1e-12)
            return UnchangedEdit(source);
        double orientation = Math.Sign(signedArea);

        // Offset every edge inward, then intersect neighboring offset lines.
        // This is the standard polygon-inset construction for convex faces.
        (double X, double Y)[] edgePoint = new (double X, double Y)[loop.Length];
        (double X, double Y)[] edgeDirection = new (double X, double Y)[loop.Length];
        for (int i = 0; i < loop.Length; i++)
        {
            int next = (i + 1) % loop.Length;
            double dx = outer2[next].X - outer2[i].X;
            double dy = outer2[next].Y - outer2[i].Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length) || length <= 1e-9)
                return UnchangedEdit(source);

            double ux = dx / length;
            double uy = dy / length;
            // CCW polygons have their interior on the left side of each edge;
            // CW polygons have it on the right.
            double nx = orientation > 0.0 ? -uy : uy;
            double ny = orientation > 0.0 ? ux : -ux;
            edgePoint[i] = (outer2[i].X + nx * insetMeters, outer2[i].Y + ny * insetMeters);
            edgeDirection[i] = (ux, uy);
        }

        (double X, double Y)[] inner2 = new (double X, double Y)[loop.Length];
        for (int i = 0; i < loop.Length; i++)
        {
            int previous = (i - 1 + loop.Length) % loop.Length;
            if (!TryIntersectLines(
                    edgePoint[previous], edgeDirection[previous],
                    edgePoint[i], edgeDirection[i],
                    out inner2[i]))
            {
                return UnchangedEdit(source);
            }
        }

        double innerArea = SignedArea(inner2);
        if (!double.IsFinite(innerArea) || Math.Abs(innerArea) <= 1e-12 || Math.Sign(innerArea) != Math.Sign(signedArea))
            return UnchangedEdit(source); // inset is too large or the polygon is not suitable

        Vec3[] inner = new Vec3[loop.Length];
        for (int i = 0; i < loop.Length; i++)
        {
            inner[i] = center + tangent * inner2[i].X + bitangent * inner2[i].Y;
            if (!double.IsFinite(inner[i].X) || !double.IsFinite(inner[i].Y) || !double.IsFinite(inner[i].Z))
                return UnchangedEdit(source);
        }

        // Keep the planar inset distance independent from the signed depth.
        // Positive depth means inward (toward the object interior), negative
        // means outward/protruding, and zero remains a classic planar inset.
        // Face winding alone is not a reliable indication of exterior direction
        // on imported geometry, so orient a separate exterior normal using the
        // mesh centroid when that direction is unambiguous. This makes the sign
        // convention intuitive on closed objects such as cubes even if a face's
        // source triangles happen to be wound the opposite way.
        Vec3 outwardNormal = ResolveExteriorNormal(faceNormal, center);

        Vec3 depthOffset = outwardNormal * -recessDepthMeters;
        bool hasDepth = Math.Abs(recessDepthMeters) > 1e-9;
        Vec3[] recessedInner = hasDepth
            ? inner.Select(point => point + depthOffset).ToArray()
            : inner;
        Vec3 recessedCenter = center + depthOffset;

        // Preserve a continuous planar UV transform where the source face has one.
        // This keeps brick/wood/glass atlas placement stable across the new ring.
        Func<double, double, Vec2> mapUv = BuildPlanarUvMapper(source, face, center, tangent, bitangent);
        Vec2[] innerUv = new Vec2[loop.Length];
        for (int i = 0; i < loop.Length; i++)
            innerUv[i] = mapUv(inner2[i].X, inner2[i].Y);
        Vec2 uvCenter = mapUv(0.0, 0.0);

        HashSet<int> removed = face.TriangleIndices.ToHashSet();
        int trianglesPerEdge = hasDepth ? 5 : 3;
        List<Triangle> result = new(source.Count - removed.Count + loop.Length * trianglesPerEdge);
        Dictionary<int, int> oldToNew = new();
        for (int i = 0; i < source.Count; i++)
        {
            if (removed.Contains(i)) continue;
            oldToNew[i] = result.Count;
            result.Add(source[i]);
        }
        List<int[]> logicalFaces = RemapUnaffectedFaces(faceIndex, oldToNew);

        Material material = source[face.TriangleIndices[0]].Material;
        int groupId = source[face.TriangleIndices[0]].GroupId;

        // Ring: one quad per polygon edge.
        for (int i = 0; i < loop.Length; i++)
        {
            int next = (i + 1) % loop.Length;
            Vec3 a = Vertices[loop[i]], b = Vertices[loop[next]], c = inner[next], d = inner[i];
            Vec2 ua = UvForVertex(source, face, loop[i]), ub = UvForVertex(source, face, loop[next]);
            int ringStart = result.Count;
            result.Add(new Triangle(a, b, c, ua, ub, innerUv[next], material, groupId));
            result.Add(new Triangle(a, c, d, ua, innerUv[next], innerUv[i], material, groupId));
            logicalFaces.Add([ringStart, ringStart + 1]);
        }

        // Optional depth walls make either a recess or a protrusion physically visible.
        // Their winding follows the original logical face; shading remains distinct
        // from the coplanar outer ring and the displaced cap.
        if (hasDepth)
        {
            for (int i = 0; i < loop.Length; i++)
            {
                int next = (i + 1) % loop.Length;
                Vec3 a = inner[i], b = inner[next];
                Vec3 c = recessedInner[next], d = recessedInner[i];
                Vec3 edgeMidpoint = (a + b) * 0.5;
                Vec3 desiredNormal = recessDepthMeters >= 0.0
                    ? center - edgeMidpoint       // cavity wall faces into the opening
                    : edgeMidpoint - center;      // raised wall faces away from the bump
                desiredNormal -= outwardNormal * desiredNormal.Dot(outwardNormal);

                // There is no authored UV surface for a newly created reveal.
                // Reuse the boundary U coordinates and give depth a small, stable
                // V span so texture derivatives remain non-degenerate.
                Vec2 ua = innerUv[i];
                Vec2 ub = innerUv[next];
                double uvDepth = Math.Max(1e-4, Math.Abs(recessDepthMeters));
                Vec2 uc = new(ub.U, ub.V + uvDepth);
                Vec2 ud = new(ua.U, ua.V + uvDepth);

                int revealStart = result.Count;
                Triangle first = new(a, b, c, ua, ub, uc, material, groupId);
                Triangle second = new(a, c, d, ua, uc, ud, material, groupId);
                if (first.Normal.Dot(desiredNormal) >= 0.0)
                {
                    result.Add(first);
                    result.Add(second);
                }
                else
                {
                    result.Add(new Triangle(a, c, b, ua, uc, ub, material, groupId));
                    result.Add(new Triangle(a, d, c, ua, ud, uc, material, groupId));
                }
                logicalFaces.Add([revealStart, revealStart + 1]);
            }
        }

        // Inner cap is a fan, maintaining the original face winding. Signed
        // depth places it either behind (+) or in front of (-) the original
        // surface, producing clear lighting and silhouette cues.
        int innerCapStart = result.Count;
        for (int i = 0; i < loop.Length; i++)
        {
            int next = (i + 1) % loop.Length;
            Triangle tri = new(recessedCenter, recessedInner[i], recessedInner[next], uvCenter, innerUv[i], innerUv[next], material, groupId);
            if (tri.Normal.Dot(outwardNormal) >= 0.0)
                result.Add(tri);
            else
                result.Add(new Triangle(recessedCenter, recessedInner[next], recessedInner[i], uvCenter, innerUv[next], innerUv[i], material, groupId));
        }
        logicalFaces.Add(Enumerable.Range(innerCapStart, loop.Length).ToArray());
        return new ComposerMeshTopologyEditResult(result, logicalFaces);

        static double SignedArea(IReadOnlyList<(double X, double Y)> points)
        {
            double area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                int next = (i + 1) % points.Count;
                area += points[i].X * points[next].Y - points[next].X * points[i].Y;
            }
            return area * 0.5;
        }

        static bool TryIntersectLines(
            (double X, double Y) p,
            (double X, double Y) r,
            (double X, double Y) q,
            (double X, double Y) s,
            out (double X, double Y) intersection)
        {
            double denominator = r.X * s.Y - r.Y * s.X;
            if (Math.Abs(denominator) <= 1e-10)
            {
                intersection = default;
                return false;
            }
            double qpx = q.X - p.X;
            double qpy = q.Y - p.Y;
            double t = (qpx * s.Y - qpy * s.X) / denominator;
            intersection = (p.X + r.X * t, p.Y + r.Y * t);
            return double.IsFinite(intersection.X) && double.IsFinite(intersection.Y);
        }
    }

    private ComposerMeshTopologyEditResult UnchangedEdit(IReadOnlyList<Triangle> source) =>
        new(source.ToList(), Faces.Select(face => face.TriangleIndices.ToArray()).ToArray());

    private List<int[]> RemapUnaffectedFaces(int excludedFaceIndex, IReadOnlyDictionary<int, int> oldToNew)
    {
        List<int[]> result = new(Math.Max(0, Faces.Count - 1));
        for (int faceIndex = 0; faceIndex < Faces.Count; faceIndex++)
        {
            if (faceIndex == excludedFaceIndex)
                continue;
            int[] mapped = Faces[faceIndex].TriangleIndices
                .Where(oldToNew.ContainsKey)
                .Select(oldIndex => oldToNew[oldIndex])
                .ToArray();
            if (mapped.Length > 0)
                result.Add(mapped);
        }
        return result;
    }

    private Func<double, double, Vec2> BuildPlanarUvMapper(
        IReadOnlyList<Triangle> source,
        ComposerMeshFace face,
        Vec3 center,
        Vec3 tangent,
        Vec3 bitangent)
    {
        // Solve the affine map (x,y)->(u,v) from the first non-degenerate source
        // triangle. Composer primitive faces use planar UVs, so the same map is
        // valid for the entire polygon. Imported seam-heavy faces still fall back
        // to a stable average mapping rather than producing invalid coordinates.
        foreach (int triangleIndex in face.TriangleIndices)
        {
            Triangle tri = source[triangleIndex];
            Vec3 ra = tri.A - center, rb = tri.B - center, rc = tri.C - center;
            double ax = ra.Dot(tangent), ay = ra.Dot(bitangent);
            double bx = rb.Dot(tangent), by = rb.Dot(bitangent);
            double cx = rc.Dot(tangent), cy = rc.Dot(bitangent);
            double determinant = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Math.Abs(determinant) <= 1e-12)
                continue;

            double inv = 1.0 / determinant;
            double du1 = tri.UvB.U - tri.UvA.U, du2 = tri.UvC.U - tri.UvA.U;
            double dv1 = tri.UvB.V - tri.UvA.V, dv2 = tri.UvC.V - tri.UvA.V;
            double ux = (du1 * (cy - ay) - du2 * (by - ay)) * inv;
            double uy = ((bx - ax) * du2 - (cx - ax) * du1) * inv;
            double vx = (dv1 * (cy - ay) - dv2 * (by - ay)) * inv;
            double vy = ((bx - ax) * dv2 - (cx - ax) * dv1) * inv;
            double u0 = tri.UvA.U - ux * ax - uy * ay;
            double v0 = tri.UvA.V - vx * ax - vy * ay;
            return (x, y) => new Vec2(u0 + ux * x + uy * y, v0 + vx * x + vy * y);
        }

        Vec2 average = Vec2.Zero;
        int count = 0;
        foreach (int vertex in face.BoundaryLoop)
        {
            average += UvForVertex(source, face, vertex);
            count++;
        }
        if (count > 0) average = average * (1.0 / count);
        return (_, _) => average;
    }

    private Vec3 FaceNormal(IReadOnlyList<Triangle> source, ComposerMeshFace face)
    {
        Vec3 sum = Vec3.Zero;
        foreach (int triangleIndex in face.TriangleIndices)
            sum += source[triangleIndex].Normal;
        return sum.Normalize();
    }

    private Vec3 ResolveExteriorNormal(Vec3 faceNormal, Vec3 faceCenter)
    {
        Vec3 outwardNormal = faceNormal.Normalize();
        if (outwardNormal.Length() <= 1e-8)
            return outwardNormal;

        // For a closed/volumetric object, a face center normally lies farther from
        // the mesh centroid in the exterior direction. This remains stable when an
        // individual imported face has reversed winding. For a single open plane
        // the hint is coplanar/ambiguous, so preserve its authored normal instead.
        Vec3 meshCenter = Vec3.Zero;
        foreach (Vec3 vertex in Vertices) meshCenter += vertex;
        if (Vertices.Count > 0) meshCenter /= Vertices.Count;
        Vec3 outwardHint = faceCenter - meshCenter;
        if (outwardHint.Length() > 1e-8 && outwardNormal.Dot(outwardHint) < 0.0)
            outwardNormal *= -1.0;
        return outwardNormal;
    }

    private Vec2 UvForVertex(IReadOnlyList<Triangle> source, ComposerMeshFace face, int vertexId)
    {
        foreach (int triangleIndex in face.TriangleIndices)
        {
            Triangle tri = source[triangleIndex];
            if (triangleVertexIds[triangleIndex, 0] == vertexId) return tri.UvA;
            if (triangleVertexIds[triangleIndex, 1] == vertexId) return tri.UvB;
            if (triangleVertexIds[triangleIndex, 2] == vertexId) return tri.UvC;
        }
        return Vec2.Zero;
    }

    private static List<int[]>? BuildPrimitiveFaceGroups(
        string? kind,
        IReadOnlyDictionary<string, double>? p,
        int triangleCount)
    {
        if (string.IsNullOrWhiteSpace(kind) || p == null || triangleCount == 0)
            return null;
        string normalized = new(kind.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        List<int[]> groups = new();
        int cursor = 0;
        int ReadInt(string key, int fallback, int min, int max) =>
            Math.Clamp(p.TryGetValue(key, out double value) && double.IsFinite(value) ? (int)Math.Round(value) : fallback, min, max);
        double Read(string key, double fallback) => p.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;
        void One() { if (cursor < triangleCount) groups.Add([cursor++]); }
        void Pair() { if (cursor + 1 < triangleCount) groups.Add([cursor++, cursor++]); else One(); }
        void Range(int count)
        {
            count = Math.Min(count, triangleCount - cursor);
            if (count <= 0) return;
            groups.Add(Enumerable.Range(cursor, count).ToArray());
            cursor += count;
        }

        switch (normalized)
        {
            case "plane":
                Range(triangleCount);
                break;
            case "cube":
                for (int i = 0; i < 6 && cursor < triangleCount; i++) Pair();
                break;
            case "grid":
            case "torus":
                while (cursor < triangleCount) Pair();
                break;
            case "sphere":
            {
                int lon = ReadInt("longitudeSegments", 32, 3, 256);
                int lat = ReadInt("latitudeSegments", 16, 2, 128);
                for (int y = 0; y < lat && cursor < triangleCount; y++)
                    for (int x = 0; x < lon && cursor < triangleCount; x++)
                        if (y == 0 || y == lat - 1) One(); else Pair();
                break;
            }
            case "icosphere":
                while (cursor < triangleCount) One();
                break;
            case "cylinder":
            {
                int sides = ReadInt("sides", 32, 3, 512);
                for (int i = 0; i < sides && cursor < triangleCount; i++) Pair();
                bool caps = ReadInt("capFill", 1, 0, 1) != 0;
                int capSides = Math.Min(sides, 256); // AddDisk() has its own 256-side renderer clamp.
                if (caps) { Range(capSides); Range(capSides); }
                break;
            }
            case "cone":
            {
                int sides = ReadInt("sides", 32, 3, 512);
                double r1 = p.ContainsKey("radius1") ? Math.Max(0.0, Read("radius1", 0.5)) : Math.Max(0.0, Read("radius", 0.5));
                double r2 = Math.Max(0.0, Read("radius2", 0.0));
                for (int i = 0; i < sides && cursor < triangleCount; i++)
                    if (r1 > 1e-12 && r2 > 1e-12) Pair(); else One();
                bool caps = ReadInt("capFill", 1, 0, 1) != 0;
                int capSides = Math.Min(sides, 256); // AddDisk() has its own 256-side renderer clamp.
                if (caps && r1 > 1e-12) Range(capSides);
                if (caps && r2 > 1e-12) Range(capSides);
                break;
            }
            case "circle":
            {
                int fill = ReadInt("fillType", 1, 0, 2);
                if (fill == 1) Range(triangleCount);
                else while (cursor < triangleCount) One();
                break;
            }
            default:
                return null;
        }
        while (cursor < triangleCount) One();
        return groups;
    }

    /// <summary>
    /// Conservative fallback for triangle-only imports. We only merge triangles
    /// when there is strong evidence that the shared edge is renderer
    /// triangulation rather than an authored modeling edge: a closed planar fan,
    /// or a two-triangle quad whose shared edge is the diagonal in both triangles.
    /// Ambiguous coplanar grids therefore remain separate instead of being
    /// incorrectly collapsed into one giant face.
    /// </summary>
    private static List<int[]> BuildFallbackFaceGroups(
        IReadOnlyList<Triangle> triangles,
        int[,] ids,
        IReadOnlyList<Vec3> vertices,
        double tolerance)
    {
        List<int[]> groups = new();
        bool[] used = new bool[triangles.Count];
        Dictionary<EdgeKey, List<int>> byEdge = BuildTriangleEdgeMap(triangles.Count, ids);

        // A fan with a true interior center is an unambiguous triangulated N-gon.
        Dictionary<int, List<int>> byVertex = new();
        for (int i = 0; i < triangles.Count; i++)
        for (int c = 0; c < 3; c++)
        {
            int v = ids[i, c];
            if (!byVertex.TryGetValue(v, out List<int>? list))
                byVertex[v] = list = new List<int>();
            list.Add(i);
        }

        foreach (KeyValuePair<int, List<int>> entry in byVertex
                     .Where(pair => pair.Value.Count >= 3)
                     .OrderByDescending(pair => pair.Value.Count))
        {
            int[] candidates = entry.Value.Where(i => !used[i]).Distinct().ToArray();
            if (candidates.Length < 3 || !IsInteriorFanCenter(entry.Key, candidates, ids))
                continue;
            if (!FanBoundaryIsAFeatureBoundary(candidates, byEdge, triangles, ids, vertices, tolerance))
                continue;
            if (!TryBuildLogicalBoundary(
                    candidates, triangles, ids, vertices, tolerance,
                    requirePlanar: true, requireUvContinuity: true, out _))
            {
                continue;
            }

            foreach (int triangleIndex in candidates)
                used[triangleIndex] = true;
            groups.Add(candidates);
        }

        // For ordinary triangulated quads, the internal diagonal is normally the
        // longest edge of both triangles. Requiring that property prevents two
        // neighboring coplanar quads in a grid from being merged across their
        // real shared side.
        for (int i = 0; i < triangles.Count; i++)
        {
            if (used[i])
                continue;

            int best = -1;
            double bestDiagonalLengthSquared = -1.0;
            foreach (EdgeKey edge in TriangleEdges(i, ids))
            {
                if (!byEdge.TryGetValue(edge, out List<int>? adjacent) || adjacent.Count != 2)
                    continue;
                int j = adjacent[0] == i ? adjacent[1] : adjacent[0];
                if (j == i || used[j])
                    continue;
                if (!IsLikelyTriangulationDiagonal(i, j, edge, ids, vertices, tolerance))
                    continue;

                int[] candidate = [i, j];
                if (!TryBuildLogicalBoundary(
                        candidate, triangles, ids, vertices, tolerance,
                        requirePlanar: true, requireUvContinuity: true, out _))
                {
                    continue;
                }

                double lengthSquared = (vertices[edge.A] - vertices[edge.B]).Dot(vertices[edge.A] - vertices[edge.B]);
                if (lengthSquared > bestDiagonalLengthSquared)
                {
                    bestDiagonalLengthSquared = lengthSquared;
                    best = j;
                }
            }

            if (best >= 0)
            {
                used[i] = used[best] = true;
                groups.Add([i, best]);
            }
            else
            {
                used[i] = true;
                groups.Add([i]);
            }
        }

        return groups;
    }

    private static bool TryValidateFacePartition(
        IReadOnlyList<int[]> candidates,
        IReadOnlyList<Triangle> triangles,
        int[,] ids,
        IReadOnlyList<Vec3> vertices,
        double tolerance,
        bool requirePlanar,
        bool requireUvContinuity,
        out List<int[]> groups)
    {
        groups = new List<int[]>(candidates.Count);
        bool[] covered = new bool[triangles.Count];
        foreach (int[] candidate in candidates)
        {
            int[] normalized = candidate
                .Where(index => index >= 0 && index < triangles.Count)
                .Distinct()
                .ToArray();
            if (normalized.Length == 0 || normalized.Any(index => covered[index]))
            {
                groups.Clear();
                return false;
            }
            if (!TryBuildLogicalBoundary(
                    normalized, triangles, ids, vertices, tolerance,
                    requirePlanar, requireUvContinuity, out _))
            {
                groups.Clear();
                return false;
            }
            foreach (int index in normalized)
                covered[index] = true;
            groups.Add(normalized);
        }

        if (covered.Any(value => !value))
        {
            groups.Clear();
            return false;
        }
        return true;
    }

    /// <summary>
    /// Proves that a set of render triangles can behave as one editor polygon.
    /// The patch must be edge-connected, consistently wound, 2-manifold, and
    /// have exactly one simple boundary loop. Inferred faces additionally need
    /// to be planar and UV-continuous across every hidden triangulation edge.
    /// </summary>
    private static bool TryBuildLogicalBoundary(
        IReadOnlyList<int> triangleIndices,
        IReadOnlyList<Triangle> triangles,
        int[,] ids,
        IReadOnlyList<Vec3> vertices,
        double tolerance,
        bool requirePlanar,
        bool requireUvContinuity,
        out int[] boundaryLoop)
    {
        boundaryLoop = Array.Empty<int>();
        int[] group = triangleIndices.Distinct().ToArray();
        if (group.Length == 0 || group.Any(i => i < 0 || i >= triangles.Count))
            return false;

        Dictionary<EdgeKey, List<(int Triangle, int From, int To)>> edgeUses = new();
        foreach (int t in group)
        {
            int a = ids[t, 0], b = ids[t, 1], c = ids[t, 2];
            if (a == b || b == c || c == a || triangles[t].Normal.Length() <= 1e-10)
                return false;
            AddUse(t, a, b);
            AddUse(t, b, c);
            AddUse(t, c, a);
        }

        if (edgeUses.Values.Any(uses => uses.Count > 2))
            return false;

        Dictionary<int, HashSet<int>> triangleAdjacency = group.ToDictionary(i => i, _ => new HashSet<int>());
        foreach (KeyValuePair<EdgeKey, List<(int Triangle, int From, int To)>> pair in edgeUses)
        {
            EdgeKey edge = pair.Key;
            List<(int Triangle, int From, int To)> uses = pair.Value;
            if (uses.Count != 2)
                continue;

            (int Triangle, int From, int To) first = uses[0];
            (int Triangle, int From, int To) second = uses[1];
            if (first.From != second.To || first.To != second.From)
                return false; // same-direction use means inconsistent winding

            triangleAdjacency[first.Triangle].Add(second.Triangle);
            triangleAdjacency[second.Triangle].Add(first.Triangle);

            if (requireUvContinuity)
            {
                Material firstMaterial = triangles[first.Triangle].Material;
                Material secondMaterial = triangles[second.Triangle].Material;
                if (!MaterialsEquivalentForLogicalFace(firstMaterial, secondMaterial))
                    return false;
                // UV discontinuity is a real face boundary only when UVs are
                // actually consumed by a texture. Untextured triangle formats
                // often carry synthetic per-triangle UV defaults.
                if (firstMaterial.HasAnyTexture &&
                    !SharedEdgeUvIsContinuous(first.Triangle, second.Triangle, edge, triangles, ids))
                {
                    return false;
                }
            }
        }

        HashSet<int> reached = new();
        Stack<int> stack = new();
        stack.Push(group[0]);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            if (!reached.Add(current))
                continue;
            foreach (int adjacent in triangleAdjacency[current])
                stack.Push(adjacent);
        }
        if (reached.Count != group.Length)
            return false;

        if (requirePlanar && !IsPlanarPatch(group, triangles, ids, vertices, tolerance))
            return false;

        List<EdgeKey> boundaryEdges = edgeUses
            .Where(pair => pair.Value.Count == 1)
            .Select(pair => pair.Key)
            .ToList();
        if (boundaryEdges.Count < 3)
            return false;

        Dictionary<int, List<int>> boundaryAdjacency = new();
        foreach (EdgeKey edge in boundaryEdges)
        {
            AddNeighbor(edge.A, edge.B);
            AddNeighbor(edge.B, edge.A);
        }
        if (boundaryAdjacency.Values.Any(neighbors => neighbors.Count != 2))
            return false;

        int start = boundaryAdjacency.Keys.Min();
        List<int> loop = [start];
        HashSet<EdgeKey> walkedEdges = new();
        int previous = -1;
        int currentVertex = start;
        for (int guard = 0; guard <= boundaryEdges.Count; guard++)
        {
            List<int> neighbors = boundaryAdjacency[currentVertex];
            int next = neighbors[0] != previous ? neighbors[0] : neighbors[1];
            EdgeKey walked = new(currentVertex, next);
            if (!walkedEdges.Add(walked))
                return false;
            if (next == start)
                break;
            if (loop.Contains(next))
                return false;
            loop.Add(next);
            previous = currentVertex;
            currentVertex = next;
        }

        if (walkedEdges.Count != boundaryEdges.Count || loop.Count < 3)
            return false; // multiple loops/holes or an open/branched boundary

        Vec3 averageNormal = Vec3.Zero;
        foreach (int t in group)
            averageNormal += triangles[t].Normal;
        averageNormal = averageNormal.Normalize();
        if (averageNormal.Length() <= 1e-10)
            return false;

        Vec3 polygonNormal = Vec3.Zero;
        for (int i = 0; i < loop.Count; i++)
            polygonNormal += vertices[loop[i]].Cross(vertices[loop[(i + 1) % loop.Count]]);
        if (polygonNormal.Length() <= 1e-10)
            return false;
        if (polygonNormal.Dot(averageNormal) < 0.0)
            loop.Reverse();

        if (BoundarySelfIntersects(loop, vertices, averageNormal, tolerance))
            return false;

        boundaryLoop = loop.ToArray();
        return true;

        void AddUse(int triangle, int from, int to)
        {
            EdgeKey key = new(from, to);
            if (!edgeUses.TryGetValue(key, out List<(int Triangle, int From, int To)>? list))
                edgeUses[key] = list = new List<(int Triangle, int From, int To)>(2);
            list.Add((triangle, from, to));
        }

        void AddNeighbor(int from, int to)
        {
            if (!boundaryAdjacency.TryGetValue(from, out List<int>? list))
                boundaryAdjacency[from] = list = new List<int>(2);
            if (!list.Contains(to))
                list.Add(to);
        }
    }

    private static bool IsPlanarPatch(
        IReadOnlyList<int> group,
        IReadOnlyList<Triangle> triangles,
        int[,] ids,
        IReadOnlyList<Vec3> vertices,
        double tolerance)
    {
        Triangle reference = triangles[group[0]];
        Vec3 normal = reference.Normal.Normalize();
        if (normal.Length() <= 1e-10)
            return false;

        double scale = 0.0;
        HashSet<int> vertexIds = new();
        foreach (int t in group)
        {
            if (triangles[t].Normal.Dot(normal) < 0.99999)
                return false;
            vertexIds.Add(ids[t, 0]);
            vertexIds.Add(ids[t, 1]);
            vertexIds.Add(ids[t, 2]);
        }
        foreach (int vertexId in vertexIds)
            scale = Math.Max(scale, (vertices[vertexId] - reference.A).Length());
        double planeTolerance = Math.Max(tolerance * 8.0, Math.Max(1.0, scale) * 1e-6);
        return vertexIds.All(vertexId => Math.Abs((vertices[vertexId] - reference.A).Dot(normal)) <= planeTolerance);
    }

    private static bool MaterialsEquivalentForLogicalFace(Material a, Material b)
    {
        if (ReferenceEquals(a, b))
            return true;

        return SameVec(a.Color, b.Color) &&
               Nearly(a.Emission, b.Emission) &&
               string.Equals(a.LightId, b.LightId, StringComparison.Ordinal) &&
               SameVec(a.EmissionColor, b.EmissionColor) &&
               Nearly(a.Alpha, b.Alpha) && a.AlphaMode == b.AlphaMode &&
               Nearly(a.AlphaCutoff, b.AlphaCutoff) && a.DoubleSided == b.DoubleSided &&
               Nearly(a.Metallic, b.Metallic) && Nearly(a.Roughness, b.Roughness) &&
               Nearly(a.Transmission, b.Transmission) && Nearly(a.Ior, b.Ior) &&
               Nearly(a.Thickness, b.Thickness) && SameVec(a.AttenuationColor, b.AttenuationColor) &&
               Nearly(a.AttenuationDistance, b.AttenuationDistance) &&
               Nearly(a.Clearcoat, b.Clearcoat) && Nearly(a.ClearcoatRoughness, b.ClearcoatRoughness) &&
               a.ClearcoatUsesTransmissionTexture == b.ClearcoatUsesTransmissionTexture &&
               Nearly(a.NormalScale, b.NormalScale) && Nearly(a.OcclusionStrength, b.OcclusionStrength) &&
               SameTexture(a.Texture, b.Texture) &&
               SameTexture(a.MetallicRoughnessTexture, b.MetallicRoughnessTexture) &&
               SameTexture(a.NormalTexture, b.NormalTexture) &&
               SameTexture(a.EmissiveTexture, b.EmissiveTexture) &&
               SameTexture(a.TransmissionTexture, b.TransmissionTexture) &&
               SameTexture(a.OcclusionTexture, b.OcclusionTexture);

        static bool Nearly(double x, double y) => Math.Abs(x - y) <= 1e-9;
        static bool SameVec(Vec3 x, Vec3 y) =>
            Nearly(x.X, y.X) && Nearly(x.Y, y.Y) && Nearly(x.Z, y.Z);
        static bool SameTexture(TextureMap? x, TextureMap? y) => ReferenceEquals(x, y);
    }

    private static bool SharedEdgeUvIsContinuous(
        int firstTriangle,
        int secondTriangle,
        EdgeKey edge,
        IReadOnlyList<Triangle> triangles,
        int[,] ids)
    {
        return TryUv(firstTriangle, edge.A, out Vec2 firstA) &&
               TryUv(secondTriangle, edge.A, out Vec2 secondA) &&
               TryUv(firstTriangle, edge.B, out Vec2 firstB) &&
               TryUv(secondTriangle, edge.B, out Vec2 secondB) &&
               SameUv(firstA, secondA) && SameUv(firstB, secondB);

        bool TryUv(int triangleIndex, int vertexId, out Vec2 uv)
        {
            Triangle tri = triangles[triangleIndex];
            if (ids[triangleIndex, 0] == vertexId) { uv = tri.UvA; return true; }
            if (ids[triangleIndex, 1] == vertexId) { uv = tri.UvB; return true; }
            if (ids[triangleIndex, 2] == vertexId) { uv = tri.UvC; return true; }
            uv = Vec2.Zero;
            return false;
        }

        static bool SameUv(Vec2 a, Vec2 b) =>
            Math.Abs(a.U - b.U) <= 1e-6 && Math.Abs(a.V - b.V) <= 1e-6;
    }

    private static bool IsLikelyTriangulationDiagonal(
        int firstTriangle,
        int secondTriangle,
        EdgeKey sharedEdge,
        int[,] ids,
        IReadOnlyList<Vec3> vertices,
        double tolerance)
    {
        HashSet<int> union =
        [
            ids[firstTriangle, 0], ids[firstTriangle, 1], ids[firstTriangle, 2],
            ids[secondTriangle, 0], ids[secondTriangle, 1], ids[secondTriangle, 2]
        ];
        if (union.Count != 4)
            return false;

        double sharedLengthSquared = EdgeLengthSquared(sharedEdge, vertices);
        double epsilon = Math.Max(tolerance * tolerance * 4.0, sharedLengthSquared * 1e-8);
        return sharedLengthSquared + epsilon >= LongestTriangleEdgeSquared(firstTriangle, ids, vertices) &&
               sharedLengthSquared + epsilon >= LongestTriangleEdgeSquared(secondTriangle, ids, vertices);
    }

    private static double LongestTriangleEdgeSquared(int triangleIndex, int[,] ids, IReadOnlyList<Vec3> vertices)
    {
        return TriangleEdges(triangleIndex, ids).Max(edge => EdgeLengthSquared(edge, vertices));
    }

    private static double EdgeLengthSquared(EdgeKey edge, IReadOnlyList<Vec3> vertices)
    {
        Vec3 delta = vertices[edge.A] - vertices[edge.B];
        return delta.Dot(delta);
    }

    private static IEnumerable<EdgeKey> TriangleEdges(int triangleIndex, int[,] ids)
    {
        yield return new EdgeKey(ids[triangleIndex, 0], ids[triangleIndex, 1]);
        yield return new EdgeKey(ids[triangleIndex, 1], ids[triangleIndex, 2]);
        yield return new EdgeKey(ids[triangleIndex, 2], ids[triangleIndex, 0]);
    }

    private static Dictionary<EdgeKey, List<int>> BuildTriangleEdgeMap(int triangleCount, int[,] ids)
    {
        Dictionary<EdgeKey, List<int>> byEdge = new();
        for (int i = 0; i < triangleCount; i++)
        {
            foreach (EdgeKey edge in TriangleEdges(i, ids))
            {
                if (!byEdge.TryGetValue(edge, out List<int>? list))
                    byEdge[edge] = list = new List<int>(2);
                list.Add(i);
            }
        }
        return byEdge;
    }

    private static bool BoundarySelfIntersects(
        IReadOnlyList<int> loop,
        IReadOnlyList<Vec3> vertices,
        Vec3 normal,
        double tolerance)
    {
        if (loop.Count < 4)
            return false;

        int dropAxis = Math.Abs(normal.X) >= Math.Abs(normal.Y) && Math.Abs(normal.X) >= Math.Abs(normal.Z) ? 0 :
                       Math.Abs(normal.Y) >= Math.Abs(normal.Z) ? 1 : 2;
        (double X, double Y) Project(Vec3 p) => dropAxis switch
        {
            0 => (p.Y, p.Z),
            1 => (p.X, p.Z),
            _ => (p.X, p.Y)
        };

        (double X, double Y)[] points = loop.Select(index => Project(vertices[index])).ToArray();
        double eps = Math.Max(1e-10, tolerance * 2.0);
        for (int i = 0; i < points.Length; i++)
        {
            int i2 = (i + 1) % points.Length;
            for (int j = i + 1; j < points.Length; j++)
            {
                int j2 = (j + 1) % points.Length;
                if (i == j || i2 == j || j2 == i)
                    continue;
                if (i == 0 && j2 == 0)
                    continue;
                if (SegmentsIntersect(points[i], points[i2], points[j], points[j2], eps))
                    return true;
            }
        }
        return false;

        static bool SegmentsIntersect(
            (double X, double Y) a,
            (double X, double Y) b,
            (double X, double Y) c,
            (double X, double Y) d,
            double eps)
        {
            double o1 = Cross(a, b, c), o2 = Cross(a, b, d), o3 = Cross(c, d, a), o4 = Cross(c, d, b);
            return ((o1 > eps && o2 < -eps) || (o1 < -eps && o2 > eps)) &&
                   ((o3 > eps && o4 < -eps) || (o3 < -eps && o4 > eps));
        }

        static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool FanBoundaryIsAFeatureBoundary(
        IReadOnlyList<int> candidateTriangles,
        IReadOnlyDictionary<EdgeKey, List<int>> globalByEdge,
        IReadOnlyList<Triangle> triangles,
        int[,] ids,
        IReadOnlyList<Vec3> vertices,
        double tolerance)
    {
        HashSet<int> candidateSet = candidateTriangles.ToHashSet();
        Dictionary<EdgeKey, int> localCounts = new();
        foreach (int triangleIndex in candidateTriangles)
        {
            foreach (EdgeKey edge in TriangleEdges(triangleIndex, ids))
                localCounts[edge] = localCounts.TryGetValue(edge, out int count) ? count + 1 : 1;
        }

        foreach (KeyValuePair<EdgeKey, int> pair in localCounts)
        {
            if (pair.Value != 1 || !globalByEdge.TryGetValue(pair.Key, out List<int>? touching))
                continue;
            int inside = touching.FirstOrDefault(candidateSet.Contains, -1);
            if (inside < 0)
                continue;
            foreach (int outside in touching)
            {
                if (candidateSet.Contains(outside))
                    continue;
                // If the triangle on the other side could itself be part of the
                // same planar/material/UV surface, this outer ring is ambiguous
                // (for example an interior vertex in a triangulated grid), so do
                // not declare the one-ring a polygon face.
                if (TryBuildLogicalBoundary(
                        [inside, outside], triangles, ids, vertices, tolerance,
                        requirePlanar: true, requireUvContinuity: true, out _))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool IsInteriorFanCenter(int centerVertex, IReadOnlyList<int> triangleIndices, int[,] ids)
    {
        Dictionary<EdgeKey, int> edgeCounts = new();
        foreach (int t in triangleIndices)
        {
            Count(ids[t, 0], ids[t, 1]);
            Count(ids[t, 1], ids[t, 2]);
            Count(ids[t, 2], ids[t, 0]);
        }

        return !edgeCounts.Any(pair => pair.Value == 1 &&
            (pair.Key.A == centerVertex || pair.Key.B == centerVertex));

        void Count(int a, int b)
        {
            EdgeKey key = new(a, b);
            edgeCounts[key] = edgeCounts.TryGetValue(key, out int count) ? count + 1 : 1;
        }
    }

    private static double ComputeTolerance(IReadOnlyList<Triangle> triangles)
    {
        if (triangles.Count == 0) return 1e-7;
        bool initialized = false;
        Vec3 min = Vec3.Zero, max = Vec3.Zero;
        foreach (Triangle triangle in triangles) { Include(triangle.A); Include(triangle.B); Include(triangle.C); }
        double diagonal = initialized ? (max - min).Length() : 1.0;
        return Math.Clamp(diagonal * 1e-7, 1e-9, 1e-4);
        void Include(Vec3 point)
        {
            if (!initialized) { min = max = point; initialized = true; return; }
            min = new Vec3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Vec3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }
    }

    private readonly record struct VertexKey(long X, long Y, long Z)
    {
        public static VertexKey From(Vec3 value, double tolerance) => new(Quantize(value.X, tolerance), Quantize(value.Y, tolerance), Quantize(value.Z, tolerance));
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
        public EdgeKey(int a, int b) { A = Math.Min(a, b); B = Math.Max(a, b); }
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
