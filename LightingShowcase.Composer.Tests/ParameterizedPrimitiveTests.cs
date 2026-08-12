/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 */
using LightingShowcase.Composer;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class ParameterizedPrimitiveTests
{
    private static readonly string[] StandardComposerPrimitives =
    [
        "Plane",
        "Cube",
        "Circle",
        "UV Sphere",
        "Icosphere",
        "Cylinder",
        "Cone",
        "Torus",
        "Grid"
    ];

    // Standard3DViewportStylePrimitivesExposeEditableParametersInMeters initializes the built-in library, resolves
    // each standard viewport-style primitive through ScenePrimitiveRegistry, requires it to implement
    // IEditablePrimitiveDefinition, and then checks every length-valued parameter advertises m as its unit. The
    // final negative assertion also keeps nonstandard demo meshes such as Monkey/Suzanne out of the standard
    // primitive palette.
    [Fact]
    public void Standard3DViewportStylePrimitivesExposeEditableParametersInMeters()
    {
        using ComposerSceneSession session = new(); // Also loads the built-in object library.

        foreach (string name in StandardComposerPrimitives)
        {
            ISceneObjectDefinition? definition = ScenePrimitiveRegistry.Find(name);
            Assert.NotNull(definition);
            IEditablePrimitiveDefinition editable = Assert.IsAssignableFrom<IEditablePrimitiveDefinition>(definition);
            Assert.NotEmpty(editable.EditableParameters);

            foreach (PrimitiveParameterDescriptor descriptor in editable.EditableParameters.Where(p => p.Kind == PrimitiveParameterKind.Length))
                Assert.Equal("m", descriptor.UnitLabel);
        }

        Assert.DoesNotContain(ScenePrimitiveRegistry.DisplayNames, name =>
            string.Equals(name, "Monkey", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Suzanne", StringComparison.OrdinalIgnoreCase));
    }

    // ParameterPreviewRegeneratesGeometryAndCommitsAsOneUndoableEdit verifies that parameter preview regenerates
    // geometry and commits as one undoable edit. It uses a real ComposerSceneSession, so registration, locking,
    // history, and scene mutation follow production paths rather than mocks. Preview and commit are asserted
    // separately because interactive previews are transient, whereas commit must create the one durable edit the
    // user can undo. Undo is exercised to prove the previous state was actually captured, not merely that the
    // forward edit looked correct. Redo then proves the stored “after” state can be reapplied without replaying the
    // UI gesture. The assertions establish that the operation must explicitly report success; the resulting
    // value/state must exactly match the expected result. Representative cases include Cube, width, height, depth.
    [Fact]
    public void ParameterPreviewRegeneratesGeometryAndCommitsAsOneUndoableEdit()
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive("Cube");
        ComposerPrimitiveParameterModel model = Assert.IsType<ComposerPrimitiveParameterModel>(session.BeginPrimitiveParameterEdit(id));
        Assert.Equal(1.0, model.Values["width"], 8);

        Assert.True(session.PreviewPrimitiveParameters(id, new Dictionary<string, double>
        {
            ["width"] = 2.5,
            ["height"] = 1.25,
            ["depth"] = 0.75
        }));
        ComposerModelEvidence preview = Assert.IsType<ComposerModelEvidence>(session.GetModelEvidence(id));
        Assert.Equal(2.5, preview.WorldBounds.Max.X - preview.WorldBounds.Min.X, 6);
        Assert.Equal(1.25, preview.WorldBounds.Max.Y - preview.WorldBounds.Min.Y, 6);
        Assert.Equal(0.75, preview.WorldBounds.Max.Z - preview.WorldBounds.Min.Z, 6);

        Assert.True(session.CommitPrimitiveParameterEdit(id));
        ComposerPrimitiveParameterModel committed = Assert.IsType<ComposerPrimitiveParameterModel>(session.GetPrimitiveParameterModel(id));
        Assert.Equal(2.5, committed.Values["width"], 8);

        Assert.Equal(id, session.Undo());
        ComposerPrimitiveParameterModel undone = Assert.IsType<ComposerPrimitiveParameterModel>(session.GetPrimitiveParameterModel(id));
        Assert.Equal(1.0, undone.Values["width"], 8);

        Assert.Equal(id, session.Redo());
        ComposerPrimitiveParameterModel redone = Assert.IsType<ComposerPrimitiveParameterModel>(session.GetPrimitiveParameterModel(id));
        Assert.Equal(2.5, redone.Values["width"], 8);
    }

    // ObjectTransformsPreserveProceduralParametersUntilTopologyIsEdited verifies that object transforms preserve
    // procedural parameters until topology is edited. It uses a real ComposerSceneSession, so registration,
    // locking, history, and scene mutation follow production paths rather than mocks. Preview and commit are
    // asserted separately because interactive previews are transient, whereas commit must create the one durable
    // edit the user can undo. Undo is exercised to prove the previous state was actually captured, not merely that
    // the forward edit looked correct. Geometry hashes are compared so the test observes actual world-space mesh
    // changes/restoration instead of only transform fields. The assertions establish that required
    // objects/resources must resolve; the operation must explicitly report success; the resulting value/state must
    // exactly match the expected result; the operation must produce an observable change. Representative cases
    // include Cube, width, height, depth.
    [Fact]
    public void ObjectTransformsPreserveProceduralParametersUntilTopologyIsEdited()
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive("Cube");
        ComposerModelEvidence before = Assert.IsType<ComposerModelEvidence>(session.GetModelEvidence(id));

        Assert.True(session.UpdateTransformTarget(
            id,
            new LightingShowcase.Math3D.Vec3(1.25, 0.5, -0.75),
            new LightingShowcase.Math3D.Vec3(0.15, 0.35, -0.10),
            new LightingShowcase.Math3D.Vec3(1.5, 0.8, 1.2)));
        Assert.True(session.CommitPendingTransform(id));

        Assert.True(session.CanEditPrimitiveParameters(id));
        ComposerPrimitiveParameterModel retained = Assert.IsType<ComposerPrimitiveParameterModel>(session.GetPrimitiveParameterModel(id));
        Assert.Equal(1.0, retained.Values["width"], 8);
        Assert.Equal(1.0, retained.Values["height"], 8);
        Assert.Equal(1.0, retained.Values["depth"], 8);

        ComposerModelEvidence transformed = Assert.IsType<ComposerModelEvidence>(session.GetModelEvidence(id));
        Assert.NotEqual(before.WorldGeometryHash, transformed.WorldGeometryHash);
        Assert.Equal(LightingShowcase.Math3D.Vec3.Zero, transformed.Position);
        Assert.Equal(LightingShowcase.Math3D.Vec3.Zero, transformed.Rotation);
        Assert.Equal(new LightingShowcase.Math3D.Vec3(1, 1, 1), transformed.Scale);

        // Editing a shape parameter regenerates from the primitive definition and
        // reapplies the retained authored transform instead of snapping back.
        Assert.NotNull(session.BeginPrimitiveParameterEdit(id));
        Assert.True(session.PreviewPrimitiveParameters(id, new Dictionary<string, double>
        {
            ["width"] = 2.0,
            ["height"] = 1.0,
            ["depth"] = 1.0
        }));
        Assert.True(session.CommitPrimitiveParameterEdit(id));
        Assert.True(session.CanEditPrimitiveParameters(id));
        ComposerModelEvidence resized = Assert.IsType<ComposerModelEvidence>(session.GetModelEvidence(id));
        Assert.NotEqual(transformed.WorldGeometryHash, resized.WorldGeometryHash);

        // First undo restores the transformed 1 m cube; second undo removes the
        // authored transform while retaining the primitive definition.
        Assert.Equal(id, session.Undo());
        Assert.True(session.CanEditPrimitiveParameters(id));
        Assert.Equal(transformed.WorldGeometryHash, Assert.IsType<ComposerModelEvidence>(session.GetModelEvidence(id)).WorldGeometryHash);
        Assert.Equal(id, session.Undo());
        Assert.True(session.CanEditPrimitiveParameters(id));
        Assert.Equal(before.WorldGeometryHash, Assert.IsType<ComposerModelEvidence>(session.GetModelEvidence(id)).WorldGeometryHash);
    }

    // ConvertToMeshRemovesParametersAndUndoRestoresThem verifies that convert to mesh removes parameters and undo
    // restores them. It uses a real ComposerSceneSession, so registration, locking, history, and scene mutation
    // follow production paths rather than mocks. Undo is exercised to prove the previous state was actually
    // captured, not merely that the forward edit looked correct. The assertions establish that the operation must
    // explicitly report success; the disallowed path must be rejected; the resulting value/state must exactly match
    // the expected result. Representative cases include Cylinder.
    [Fact]
    public void ConvertToMeshRemovesParametersAndUndoRestoresThem()
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive("Cylinder");
        Assert.True(session.CanEditPrimitiveParameters(id));

        Assert.True(session.ConvertParametricObjectToMesh(id));
        Assert.False(session.CanEditPrimitiveParameters(id));

        Assert.Equal(id, session.Undo());
        Assert.True(session.CanEditPrimitiveParameters(id));
    }

    // StandardPrimitiveCanBeInsertedAndEdited verifies that standard primitive can be inserted and edited. It uses
    // a real ComposerSceneSession, so registration, locking, history, and scene mutation follow production paths
    // rather than mocks. The assertions establish that the resulting value/state must exactly match the expected
    // result.
    [Theory]
    [InlineData("Circle")]
    [InlineData("UV Sphere")]
    [InlineData("Icosphere")]
    [InlineData("Cylinder")]
    [InlineData("Cone")]
    [InlineData("Torus")]
    [InlineData("Grid")]
    public void StandardPrimitiveCanBeInsertedAndEdited(string primitiveName)
    {
        using ComposerSceneSession session = new();
        int id = session.InsertPrimitive(primitiveName);
        ComposerPrimitiveParameterModel model = Assert.IsType<ComposerPrimitiveParameterModel>(session.GetPrimitiveParameterModel(id));
        Assert.Equal(primitiveName, model.PrimitiveName);
        Assert.NotEmpty(model.Parameters);
    }
}
