/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 *
 * `Parameterized_cube_exposes_six_polygon_faces` verifies that parameterized cube exposes six polygon faces. It
 * uses a real `ComposerSceneSession`, so registration, locking, history, and scene mutation follow production
 * paths rather than mocks. The assertions establish that the operation must explicitly report success; the
 * resulting value/state must exactly match the expected result. Representative cases include `Cube`.
 *
 * `Standard_primitives_recover_their_authored_polygon_faces` verifies that standard primitives recover their
 * authored polygon faces. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene
 * mutation follow production paths rather than mocks. The assertions establish that the resulting value/state
 * must exactly match the expected result. Representative cases include `Plane`, `Cube`, `Circle`, `UV Sphere`,
 * `Icosphere`, `Cylinder`, `Cone`.
 *
 * `High_sided_cylinder_keeps_each_cap_as_one_face_despite_renderer_cap_clamp` verifies that high sided cylinder
 * keeps each cap as one face despite renderer cap clamp. It uses a real `ComposerSceneSession`, so registration,
 * locking, history, and scene mutation follow production paths rather than mocks. Preview and commit are asserted
 * separately because interactive previews are transient, whereas commit must create the one durable edit the user
 * can undo. The assertions establish that required objects/resources must resolve; the operation must explicitly
 * report success; the resulting value/state must exactly match the expected result. Representative cases include
 * `Cylinder`, `sides`.
 *
 * `Both_render_triangles_of_a_cube_quad_select_the_same_logical_face` verifies that both render triangles of a
 * cube quad select the same logical face. It uses a real `ComposerSceneSession`, so registration, locking,
 * history, and scene mutation follow production paths rather than mocks. The assertions establish that the
 * operation must explicitly report success; the resulting value/state must exactly match the expected result.
 * Representative cases include `Cube`.
 *
 * `Extruding_a_cube_quad_treats_two_render_triangles_as_one_face` verifies that extruding a cube quad treats two
 * render triangles as one face. It uses a real `ComposerSceneSession`, so registration, locking, history, and
 * scene mutation follow production paths rather than mocks. Undo is exercised to prove the previous state was
 * actually captured, not merely that the forward edit looked correct. The assertions establish that the operation
 * must explicitly report success; the disallowed path must be rejected; the resulting value/state must exactly
 * match the expected result. Representative cases include `Cube`.
 *
 * `Signed_extrude_distance_uses_positive_outward_and_negative_inward_even_with_reversed_winding` verifies that
 * signed extrude distance uses positive outward and negative inward even with reversed winding. The assertions
 * establish that the expected entry must remain discoverable.
 *
 * `Insetting_a_cube_quad_operates_on_the_whole_polygon_face` verifies that insetting a cube quad operates on the
 * whole polygon face. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene
 * mutation follow production paths rather than mocks. The assertions establish that the operation must explicitly
 * report success; the disallowed path must be rejected; the resulting value/state must exactly match the expected
 * result. Representative cases include `Cube`.
 *
 * `Recessed_inset_adds_reveal_walls_and_moves_the_inner_cap_off_the_source_plane` verifies that recessed inset
 * adds reveal walls and moves the inner cap off the source plane. The assertions establish that the resulting
 * value/state must exactly match the expected result; the expected entry must remain discoverable.
 *
 * `Signed_inset_depth_uses_positive_inward_and_negative_outward` verifies that signed inset depth uses positive
 * inward and negative outward. The assertions establish that the expected entry must remain discoverable.
 *
 * `Session_accepts_negative_inset_depth_for_a_protruding_face` verifies that session accepts negative inset depth
 * for a protruding face. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene
 * mutation follow production paths rather than mocks. The assertions establish that the operation must explicitly
 * report success; the disallowed path must be rejected; the resulting value/state must exactly match the expected
 * result. Representative cases include `Cube`.
 *
 * `Ui_style_recessed_inset_on_a_cube_creates_extra_logical_reveal_faces` verifies that ui style recessed inset on
 * a cube creates extra logical reveal faces. It uses a real `ComposerSceneSession`, so registration, locking,
 * history, and scene mutation follow production paths rather than mocks. The assertions establish that the
 * operation must explicitly report success; the disallowed path must be rejected; the resulting value/state must
 * exactly match the expected result. Representative cases include `Cube`.
 *
 * `Converted_cube_retains_six_explicit_logical_faces_and_twelve_logical_edges` verifies that converted cube
 * retains six explicit logical faces and twelve logical edges. It uses a real `ComposerSceneSession`, so
 * registration, locking, history, and scene mutation follow production paths rather than mocks. The assertions
 * establish that the operation must explicitly report success; the disallowed path must be rejected; the
 * resulting value/state must exactly match the expected result. Representative cases include `Cube`.
 *
 * `Inferred_quad_merges_only_when_shared_edge_is_a_real_triangulation_diagonal` verifies that inferred quad
 * merges only when shared edge is a real triangulation diagonal. The assertions establish that the resulting
 * value/state must exactly match the expected result.
 *
 * `Inferred_planar_grid_keeps_each_cell_as_a_quad_instead_of_merging_a_vertex_fan` verifies that inferred planar
 * grid keeps each cell as a quad instead of merging a vertex fan. The assertions establish that the resulting
 * value/state must exactly match the expected result.
 *
 * `Inferred_face_does_not_cross_a_fold_or_a_textured_uv_seam` verifies that inferred face does not cross a fold
 * or a textured uv seam. The assertions establish that the resulting value/state must exactly match the expected
 * result. Representative cases include `uv-seam`.
 *
 * `Explicit_logical_face_metadata_can_retain_a_nonplanar_authored_quad` verifies that explicit logical face
 * metadata can retain a nonplanar authored quad. The assertions establish that the resulting value/state must
 * exactly match the expected result. Representative cases include `Authored quad`.
 *
 * `Logical_faces_round_trip_through_native_scene_save` verifies that logical faces round trip through native
 * scene save. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene mutation follow
 * production paths rather than mocks. Temporary filesystem output is inspected/cleaned so persistence behavior is
 * tested end-to-end. The assertions establish that the operation must explicitly report success; the resulting
 * value/state must exactly match the expected result. Representative cases include `Cube`.
 *
 * `Multiple_sibling_objects_can_be_grouped_and_ungrouped_together` verifies that multiple sibling objects can be
 * grouped and ungrouped together. It uses a real `ComposerSceneSession`, so registration, locking, history, and
 * scene mutation follow production paths rather than mocks. The assertions establish that the operation must
 * explicitly report success; the resulting value/state must exactly match the expected result; the expected entry
 * must remain discoverable. Representative cases include `Cube`, `Cylinder`.
 */
using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class FaceGroupingAndObjectGroupingTests
{
    [Fact]
    public void Parameterized_cube_exposes_six_polygon_faces()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");

        Assert.Equal(6, session.GetMeshFaceGroupCountForTests(cube));
        Assert.Equal(6, session.GetFaceInfos(cube, 0, 200).Count);
        Assert.True(session.SetSelectedTriangle(cube, 0));
        Assert.True(session.CanEditSelectedFace(cube));
    }

    [Fact]
    public void Standard_primitives_recover_their_authored_polygon_faces()
    {
        using ComposerSceneSession session = new();
        Dictionary<string, int> expected = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Plane"] = 1,
            ["Cube"] = 6,
            ["Circle"] = 1,
            ["UV Sphere"] = 512,
            ["Icosphere"] = 80,
            ["Cylinder"] = 34,
            ["Cone"] = 33,
            ["Torus"] = 768,
            ["Grid"] = 100
        };

        foreach ((string primitive, int faceCount) in expected)
        {
            int id = session.InsertPrimitive(primitive);
            Assert.Equal(faceCount, session.GetMeshFaceGroupCountForTests(id));
        }
    }


    [Fact]
    public void High_sided_cylinder_keeps_each_cap_as_one_face_despite_renderer_cap_clamp()
    {
        using ComposerSceneSession session = new();
        int cylinder = session.InsertPrimitive("Cylinder");
        Assert.NotNull(session.BeginPrimitiveParameterEdit(cylinder));
        Assert.True(session.PreviewPrimitiveParameters(cylinder, new Dictionary<string, double> { ["sides"] = 400 }));
        Assert.True(session.CommitPrimitiveParameterEdit(cylinder));

        Assert.Equal(402, session.GetFaceCount(cylinder));
    }

    [Fact]
    public void Both_render_triangles_of_a_cube_quad_select_the_same_logical_face()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");

        Assert.True(session.SetSelectedTriangle(cube, 0));
        Assert.True(session.CanEditSelectedFace(cube));
        Assert.True(session.SetSelectedTriangle(cube, 1));
        Assert.True(session.CanEditSelectedFace(cube));
        Assert.Equal(6, session.GetMeshFaceGroupCountForTests(cube));
    }

    [Fact]
    public void Extruding_a_cube_quad_treats_two_render_triangles_as_one_face()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");
        int before = session.TriangleCount;

        Assert.True(session.SetSelectedTriangle(cube, 0));
        Assert.True(session.ExtrudeSelectedFace(cube, 0.25));
        Assert.Equal(before + 8, session.TriangleCount);
        Assert.False(session.CanEditPrimitiveParameters(cube));
        Assert.Equal(10, session.GetFaceCount(cube));

        Assert.Equal(cube, session.Undo());
        Assert.True(session.CanEditPrimitiveParameters(cube));
        Assert.Equal(6, session.GetMeshFaceGroupCountForTests(cube));
    }

    [Fact]
    public void Signed_extrude_distance_uses_positive_outward_and_negative_inward_even_with_reversed_winding()
    {
        Material material = new(new Vec3(0.7, 0.7, 0.7));
        Triangle[] mesh =
        [
            // Top face deliberately wound inward (-Z).
            new Triangle(new Vec3(0, 0, 1), new Vec3(1, 1, 1), new Vec3(1, 0, 1), material),
            new Triangle(new Vec3(0, 0, 1), new Vec3(0, 1, 1), new Vec3(1, 1, 1), material),
            // Lower face establishes +Z as the exterior direction for the top face.
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(1, 0, 0), material),
            new Triangle(new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(1, 1, 0), material)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(mesh);

        ComposerMeshTopologyEditResult outward = topology.CreateExtrudedFaceEdit(mesh, 0, 0.20);
        ComposerMeshTopologyEditResult inward = topology.CreateExtrudedFaceEdit(mesh, 0, -0.20);

        double[] outwardZ = outward.Triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).ToArray();
        double[] inwardZ = inward.Triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).ToArray();
        Assert.Contains(outwardZ, z => Math.Abs(z - 1.20) <= 1e-6);
        Assert.Contains(inwardZ, z => Math.Abs(z - 0.80) <= 1e-6);
    }

    [Fact]
    public void Insetting_a_cube_quad_operates_on_the_whole_polygon_face()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");
        int before = session.TriangleCount;

        Assert.True(session.SetSelectedTriangle(cube, 0));
        Assert.True(session.InsetSelectedFace(cube, 0.08));
        Assert.Equal(before + 10, session.TriangleCount);
        Assert.False(session.CanEditPrimitiveParameters(cube));
        Assert.Equal(10, session.GetFaceCount(cube));
    }


    [Fact]
    public void Recessed_inset_adds_reveal_walls_and_moves_the_inner_cap_off_the_source_plane()
    {
        Material material = new(new Vec3(0.7, 0.7, 0.7));
        Triangle[] quad =
        [
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), material),
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0), material)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(quad);

        ComposerMeshTopologyEditResult edit = topology.CreateInsetFaceEdit(quad, 0, 0.10, 0.02);

        Assert.Equal(20, edit.Triangles.Count);
        Assert.Equal(9, edit.LogicalFaceTriangleGroups.Count);
        Assert.Contains(edit.Triangles, tri =>
            Math.Abs(tri.A.Z) > 1e-6 || Math.Abs(tri.B.Z) > 1e-6 || Math.Abs(tri.C.Z) > 1e-6);
        Assert.Contains(edit.Triangles, tri =>
            Math.Abs(tri.A.Z) <= 1e-9 || Math.Abs(tri.B.Z) <= 1e-9 || Math.Abs(tri.C.Z) <= 1e-9);
    }

    [Fact]
    public void Signed_inset_depth_uses_positive_inward_and_negative_outward()
    {
        Material material = new(new Vec3(0.7, 0.7, 0.7));
        Triangle[] quad =
        [
            // Deliberately wind the top face inward (-Z). Signed inset depth
            // must still use the object exterior (+Z), not source winding.
            new Triangle(new Vec3(0, 0, 1), new Vec3(1, 1, 1), new Vec3(1, 0, 1), material),
            new Triangle(new Vec3(0, 0, 1), new Vec3(0, 1, 1), new Vec3(1, 1, 1), material),
            // A second face below the edited one makes the mesh centroid establish
            // +Z as the exterior direction independent of source face winding.
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(1, 0, 0), material),
            new Triangle(new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(1, 1, 0), material)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(quad);

        ComposerMeshTopologyEditResult inward = topology.CreateInsetFaceEdit(quad, 0, 0.10, 0.02);
        ComposerMeshTopologyEditResult outward = topology.CreateInsetFaceEdit(quad, 0, 0.10, -0.02);

        double[] inwardZ = inward.Triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).ToArray();
        double[] outwardZ = outward.Triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).ToArray();
        Assert.Contains(inwardZ, z => Math.Abs(z - 0.98) <= 1e-6);
        Assert.Contains(outwardZ, z => Math.Abs(z - 1.02) <= 1e-6);
    }

    [Fact]
    public void Session_accepts_negative_inset_depth_for_a_protruding_face()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");

        Assert.True(session.SetSelectedTriangle(cube, 0));
        Assert.True(session.InsetSelectedFace(cube, 0.08, -0.02));
        Assert.False(session.CanEditPrimitiveParameters(cube));
        Assert.Equal(14, session.GetFaceCount(cube));
    }

    [Fact]
    public void Ui_style_recessed_inset_on_a_cube_creates_extra_logical_reveal_faces()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");
        int before = session.TriangleCount;

        Assert.True(session.SetSelectedTriangle(cube, 0));
        Assert.True(session.InsetSelectedFace(cube, 0.08, 0.02));
        Assert.Equal(before + 18, session.TriangleCount);
        Assert.Equal(14, session.GetFaceCount(cube));
        Assert.False(session.CanEditPrimitiveParameters(cube));
    }

    [Fact]
    public void Converted_cube_retains_six_explicit_logical_faces_and_twelve_logical_edges()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");

        Assert.True(session.ConvertParametricObjectToMesh(cube));
        Assert.False(session.CanEditPrimitiveParameters(cube));
        Assert.Equal(6, session.GetMeshFaceGroupCountForTests(cube));
        Assert.Equal(12, session.GetMeshEdgeCountForTests(cube));
        Assert.Equal(6, session.GetFaceCount(cube));
    }

    [Fact]
    public void Inferred_quad_merges_only_when_shared_edge_is_a_real_triangulation_diagonal()
    {
        Material material = new(new Vec3(0.7, 0.7, 0.7));
        Triangle[] quad =
        [
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), material),
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0), material)
        ];
        ComposerMeshTopology topology = ComposerMeshTopology.Build(quad);
        Assert.Single(topology.Faces);
        Assert.Equal(4, topology.Edges.Count);

        // These coplanar triangles share a short authored side rather than the
        // longest diagonal. Geometry alone cannot prove they were one polygon,
        // so Composer conservatively keeps them as two faces.
        Triangle[] ambiguous =
        [
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 3, 0), material),
            new Triangle(new Vec3(1, 0, 0), new Vec3(0, 0, 0), new Vec3(1, -3, 0), material)
        ];
        Assert.Equal(2, ComposerMeshTopology.Build(ambiguous).Faces.Count);
    }

    [Fact]
    public void Inferred_planar_grid_keeps_each_cell_as_a_quad_instead_of_merging_a_vertex_fan()
    {
        Material material = new(new Vec3(0.75, 0.75, 0.75));
        List<Triangle> triangles = new();
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
        {
            Vec3 a = new(x, y, 0), b = new(x + 1, y, 0), c = new(x + 1, y + 1, 0), d = new(x, y + 1, 0);
            triangles.Add(new Triangle(a, b, c, material));
            triangles.Add(new Triangle(a, c, d, material));
        }

        ComposerMeshTopology topology = ComposerMeshTopology.Build(triangles);
        Assert.Equal(4, topology.Faces.Count);
        Assert.Equal(12, topology.Edges.Count); // 4 outer + 4 inner? Unique logical grid edges = 12.
    }

    [Fact]
    public void Inferred_face_does_not_cross_a_fold_or_a_textured_uv_seam()
    {
        Material plain = new(new Vec3(0.8, 0.8, 0.8));
        Triangle[] folded =
        [
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), plain),
            new Triangle(new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0.2), plain)
        ];
        Assert.Equal(2, ComposerMeshTopology.Build(folded).Faces.Count);

        TextureMap texture = TextureMap.FromRgbaBytes("uv-seam", 1, 1, [255, 255, 255, 255]);
        Material textured = new(new Vec3(1, 1, 1), texture: texture);
        Triangle[] uvSeam =
        [
            new Triangle(
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0),
                new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), textured),
            new Triangle(
                new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
                new Vec2(0.25, 0), new Vec2(0.75, 1), new Vec2(0, 1), textured)
        ];
        Assert.Equal(2, ComposerMeshTopology.Build(uvSeam).Faces.Count);
    }

    [Fact]
    public void Explicit_logical_face_metadata_can_retain_a_nonplanar_authored_quad()
    {
        Material material = new(new Vec3(0.6, 0.6, 0.6));
        SceneObjectGroup group = new(42, "Authored quad");
        group.LocalTriangles.Add(new Triangle(
            new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0.05), material, group.Id));
        group.LocalTriangles.Add(new Triangle(
            new Vec3(0, 0, 0), new Vec3(1, 1, 0.05), new Vec3(0, 1, 0), material, group.Id));
        group.SetLogicalFaceTriangleGroups(new[] { new[] { 0, 1 } });

        ComposerMeshTopology topology = ComposerMeshTopology.Build(group);
        Assert.Single(topology.Faces);
        Assert.Equal(4, topology.Edges.Count);
    }

    [Fact]
    public void Logical_faces_round_trip_through_native_scene_save()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lighting-composer-logical-face-{Guid.NewGuid():N}.lscene");
        try
        {
            using (ComposerSceneSession writer = new())
            {
                int cube = writer.InsertPrimitive("Cube");
                Assert.True(writer.SetSelectedTriangle(cube, 0));
                Assert.True(writer.InsetSelectedFace(cube, 0.08));
                Assert.Equal(10, writer.GetFaceCount(cube));
                writer.Save(path, CancellationToken.None);
            }

            using ComposerSceneSession reader = new();
            reader.Load(path, CancellationToken.None);
            int loaded = Assert.Single(reader.GetObjectInfos()).Id;
            Assert.Equal(10, reader.GetFaceCount(loaded));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Multiple_sibling_objects_can_be_grouped_and_ungrouped_together()
    {
        using ComposerSceneSession session = new();
        int cube = session.InsertPrimitive("Cube");
        int cylinder = session.InsertPrimitive("Cylinder");

        Assert.True(session.CanGroupObjects(new[] { cube, cylinder }));
        int? grouped = session.GroupObjects(new[] { cube, cylinder });
        Assert.True(grouped.HasValue);
        int groupId = grouped.Value;
        Assert.Contains(session.GetObjectInfos(), info => info.Id == groupId && info.ChildCount == 2);

        IReadOnlyList<int> promoted = session.UngroupObjects(new[] { groupId });
        Assert.Equal(2, promoted.Count);
        Assert.Contains(cube, promoted);
        Assert.Contains(cylinder, promoted);
    }
}
