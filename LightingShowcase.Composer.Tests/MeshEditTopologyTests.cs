using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class MeshEditTopologyTests
{
    [Fact]
    public void BuildWeldsSharedCornersAndCreatesUniqueEdges()
    {
        Material material = new(new Vec3(0.8, 0.8, 0.8));
        Triangle[] triangles =
        [
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), material, 7),
            new Triangle(new Vec3(0, 0, 0), new Vec3(1 + 1e-8, 1, 0), new Vec3(0, 1, 0), material, 7)
        ];

        ComposerMeshTopology topology = ComposerMeshTopology.Build(triangles);

        Assert.Equal(4, topology.Vertices.Count);
        Assert.Equal(4, topology.Edges.Count);
        Assert.Equal(1, topology.Faces.Count);
    }

    [Fact]
    public void TriangleMovesReturnEveryAffectedTriangleAndCornerMask()
    {
        Material material = new(new Vec3(0.8, 0.8, 0.8));
        Triangle[] triangles =
        [
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), material, 9),
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0), material, 9)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(triangles);
        int sharedOrigin = topology.Vertices
            .Select((value, index) => (value, index))
            .Single(item => item.value.Length() < 1e-9)
            .index;

        IReadOnlyList<ComposerMeshTriangleMove> moves = topology.TriangleMoves(
            new ComposerMeshSelection(9, ComposerSelectionMode.Vertex, sharedOrigin));

        Assert.Equal(2, moves.Count);
        Assert.All(moves, move => Assert.Equal((byte)1, move.CornerMask));
        Assert.Equal(new[] { 0, 1 }, moves.Select(move => move.TriangleIndex).ToArray());
    }

    [Fact]
    public void MovingOneWeldedVertexUpdatesEveryCornerThatUsesIt()
    {
        Material material = new(new Vec3(0.8, 0.8, 0.8));
        Triangle[] triangles =
        [
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), material, 9),
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0), material, 9)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(triangles);
        int sharedOrigin = topology.Vertices
            .Select((value, index) => (value, index))
            .Single(item => item.value.Length() < 1e-9)
            .index;

        List<Triangle> moved = topology.CreateMovedTriangles(
            triangles,
            new ComposerMeshSelection(9, ComposerSelectionMode.Vertex, sharedOrigin),
            new Vec3(0, 0, 2));

        Assert.Equal(2.0, moved[0].A.Z, 9);
        Assert.Equal(2.0, moved[1].A.Z, 9);
        Assert.Equal(0.0, moved[0].B.Z, 9);
        Assert.Equal(0.0, moved[1].C.Z, 9);
    }


    [Fact]
    public void WeldSearchCrossesSpatialCellBoundariesButChecksRealDistance()
    {
        Material material = new(new Vec3(0.8, 0.8, 0.8));
        Triangle[] triangles =
        [
            new Triangle(new Vec3(0.049, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), material),
            new Triangle(new Vec3(0.051, 0, 0), new Vec3(2, 0, 0), new Vec3(2, 1, 0), material),
            new Triangle(new Vec3(3.049, 0.049, 0.049), new Vec3(4, 0, 0), new Vec3(4, 1, 0), material),
            new Triangle(new Vec3(3.051, -0.049, -0.049), new Vec3(5, 0, 0), new Vec3(5, 1, 0), material)
        ];

        ComposerMeshTopology topology = ComposerMeshTopology.Build(triangles, requestedTolerance: 0.1);

        // The first pair straddles adjacent cells but is only 0.002 apart, so it welds.
        // The second pair can occupy neighboring cells but is more than 0.1 apart in 3D.
        Assert.Equal(11, topology.Vertices.Count);
    }


    [Fact]
    public void ComponentModesDoNotExposeTheObjectBoundingBoxBeforeAComponentIsPicked()
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive("Cube");
        Assert.True(session.SetSelectedObject(id));

        Assert.True(session.SetSelectionMode(ComposerSelectionMode.Edge));
        Assert.Null(session.GetActiveSelectionBounds());

        Assert.True(session.SetSelectionMode(ComposerSelectionMode.Face));
        Assert.Null(session.GetActiveSelectionBounds());

        Assert.True(session.SetSelectedTriangle(id, 0));
        Assert.NotNull(session.GetActiveSelectionBounds());
    }


    [Fact]
    public void ComponentMoveAxisLockAppliesToVertexEdgeAndFaceModesAndResetsInObjectMode()
    {
        using ComposerSceneSession session = new();
        session.InsertPrimitive("Cube");

        foreach (ComposerSelectionMode mode in new[]
                 {
                     ComposerSelectionMode.Vertex,
                     ComposerSelectionMode.Edge,
                     ComposerSelectionMode.Face
                 })
        {
            Assert.True(session.SetSelectionMode(mode));
            Assert.True(session.SetMeshMoveAxisLock(ComposerGizmoAxis.X));
            Assert.Equal(ComposerGizmoAxis.X, session.MeshMoveAxisLock);
            Assert.True(session.SetMeshMoveAxisLock(ComposerGizmoAxis.Y));
            Assert.Equal(ComposerGizmoAxis.Y, session.MeshMoveAxisLock);
            Assert.True(session.SetMeshMoveAxisLock(ComposerGizmoAxis.Z));
            Assert.Equal(ComposerGizmoAxis.Z, session.MeshMoveAxisLock);
            Assert.True(session.SetMeshMoveAxisLock(ComposerGizmoAxis.None));
            Assert.Equal(ComposerGizmoAxis.None, session.MeshMoveAxisLock);
        }

        Assert.True(session.SetSelectionMode(ComposerSelectionMode.Object));
        Assert.Equal(ComposerGizmoAxis.None, session.MeshMoveAxisLock);
    }

    [Fact]
    public void HoverPulseDoesNothingUntilAComponentIsNearThePointer()
    {
        using ComposerSceneSession session = new();
        session.InsertPrimitive("Cube");
        session.SetSelectionMode(ComposerSelectionMode.Edge);

        Assert.False(session.ToggleMeshHoverPulse());
        Assert.False(session.ClearMeshHover());
    }

    [Fact]
    public void PrimitiveInsertionCreatesAnEditableMeshObject()
    {
        using ComposerSceneSession session = new();

        int id = session.InsertPrimitive("Cube");

        Assert.True(session.HasRenderableScene);
        Assert.NotNull(session.GetObjectState(id));
        Assert.True(session.TriangleCount >= 12);
        Assert.Equal(ComposerSelectionMode.Object, session.SelectionMode);
    }

    [Fact]
    public void SquareInsetDepthKeepsPlanarRingAndAddsPerpendicularReveal()
    {
        Material material = new(new Vec3(0.8, 0.8, 0.8));
        Triangle[] triangles =
        [
            new Triangle(new Vec3(-1, -1, 0), new Vec3(1, -1, 0), new Vec3(1, 1, 0), material),
            new Triangle(new Vec3(-1, -1, 0), new Vec3(1, 1, 0), new Vec3(-1, 1, 0), material)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(triangles);

        ComposerMeshTopologyEditResult edit = topology.CreateInsetFaceEdit(
            triangles,
            faceIndex: 0,
            insetMeters: 0.25,
            recessDepthMeters: 0.2,
            profile: ComposerInsetProfile.Square);

        Assert.Equal(20, edit.Triangles.Count);
        Assert.Equal(9, edit.LogicalFaceTriangleGroups.Count);
        Assert.All(edit.Triangles.Take(8), triangle =>
        {
            Assert.InRange(Math.Abs(triangle.A.Z), 0.0, 1e-9);
            Assert.InRange(Math.Abs(triangle.B.Z), 0.0, 1e-9);
            Assert.InRange(Math.Abs(triangle.C.Z), 0.0, 1e-9);
        });
    }

    [Fact]
    public void SlopedInsetDepthConnectsOuterBoundaryDirectlyToDisplacedInset()
    {
        Material material = new(new Vec3(0.8, 0.8, 0.8));
        Triangle[] triangles =
        [
            new Triangle(new Vec3(-1, -1, 0), new Vec3(1, -1, 0), new Vec3(1, 1, 0), material),
            new Triangle(new Vec3(-1, -1, 0), new Vec3(1, 1, 0), new Vec3(-1, 1, 0), material)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(triangles);

        ComposerMeshTopologyEditResult edit = topology.CreateInsetFaceEdit(
            triangles,
            faceIndex: 0,
            insetMeters: 0.25,
            recessDepthMeters: 0.2,
            profile: ComposerInsetProfile.Sloped);

        Assert.Equal(12, edit.Triangles.Count);
        Assert.Equal(5, edit.LogicalFaceTriangleGroups.Count);
        Assert.All(edit.Triangles.Take(8), triangle =>
        {
            double[] depths = [Math.Abs(triangle.A.Z), Math.Abs(triangle.B.Z), Math.Abs(triangle.C.Z)];
            Assert.Contains(depths, depth => depth <= 1e-9);
            Assert.Contains(depths, depth => depth > 1e-9);
        });
    }
}
