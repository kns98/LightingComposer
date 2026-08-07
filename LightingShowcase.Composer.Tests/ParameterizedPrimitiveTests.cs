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

    [Fact]
    public void StandardBlenderStylePrimitivesExposeEditableParametersInMeters()
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
