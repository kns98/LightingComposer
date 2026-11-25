using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Xunit;

namespace LightingShowcase.Composer.Tests;

public sealed class MultiSelectTests
{
    private static void Place(ComposerSceneSession session, int id, Vec3 center)
    {
        Aabb? bounds = session.GetTransformTargetBounds(id);
        Assert.NotNull(bounds);
        Aabb box = bounds!.Value;
        Vec3 offset = center - (box.Min + box.Max) * 0.5;
        Assert.True(session.UpdateTransformTarget(id, offset, Vec3.Zero, new Vec3(1, 1, 1)));
        Assert.True(session.CommitPendingTransform(id));
    }

    private static Aabb WorldBounds(ComposerSceneSession session, int id)
    {
        ComposerModelEvidence? evidence = session.GetModelEvidence(id);
        Assert.NotNull(evidence);
        return evidence!.WorldBounds;
    }

    private static void AssertIdentityBake(ComposerSceneSession session, int id)
    {
        ComposerObjectState? state = session.GetObjectState(id);
        Assert.NotNull(state);
        Assert.True(Math.Abs(state!.Position.X) <= 1e-8 && Math.Abs(state.Position.Y) <= 1e-8 && Math.Abs(state.Position.Z) <= 1e-8);
        Assert.True(Math.Abs(state.Rotation.X) <= 1e-8 && Math.Abs(state.Rotation.Y) <= 1e-8 && Math.Abs(state.Rotation.Z) <= 1e-8);
        Assert.True(Math.Abs(state.Scale.X - 1.0) <= 1e-8 && Math.Abs(state.Scale.Y - 1.0) <= 1e-8 && Math.Abs(state.Scale.Z - 1.0) <= 1e-8);
    }

    [Fact]
    public void BatchMove_ShiftsEveryObjectByTheSameDelta()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Place(session, first, new Vec3(0, 0, 0));
        Place(session, second, new Vec3(10, 0, 0));
        string firstName = session.GetObjectState(first)!.Name;
        string secondName = session.GetObjectState(second)!.Name;
        Aabb firstBefore = WorldBounds(session, first);
        Aabb secondBefore = WorldBounds(session, second);

        int updated = session.UpdateObjects(new[] { first, second }, true, new Vec3(2, 0, 0), Vec3.Zero, new Vec3(1, 1, 1));

        Assert.Equal(2, updated);
        Aabb firstAfter = WorldBounds(session, first);
        Aabb secondAfter = WorldBounds(session, second);
        Assert.True(Math.Abs(firstAfter.Min.X - firstBefore.Min.X - 2.0) < 1e-6);
        Assert.True(Math.Abs(secondAfter.Min.X - secondBefore.Min.X - 2.0) < 1e-6);
        AssertIdentityBake(session, first);
        AssertIdentityBake(session, second);
        Assert.Equal(firstName, session.GetObjectState(first)!.Name);
        Assert.Equal(secondName, session.GetObjectState(second)!.Name);
    }

    [Fact]
    public void BatchRotate_TurnsEachObjectAboutItsOwnPivot()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Place(session, first, new Vec3(0, 0, 0));
        Place(session, second, new Vec3(10, 0, 0));
        Aabb firstBefore = WorldBounds(session, first);
        Aabb secondBefore = WorldBounds(session, second);

        // A 180-degree turn about each cube's own center maps each box onto
        // itself. Orbiting a shared centroid would swap the two boxes instead.
        int updated = session.UpdateObjects(new[] { first, second }, true, Vec3.Zero, new Vec3(0, Math.PI, 0), new Vec3(1, 1, 1));

        Assert.Equal(2, updated);
        Aabb firstAfter = WorldBounds(session, first);
        Aabb secondAfter = WorldBounds(session, second);
        Assert.True(Math.Abs(firstAfter.Min.X - firstBefore.Min.X) < 1e-6);
        Assert.True(Math.Abs(firstAfter.Max.X - firstBefore.Max.X) < 1e-6);
        Assert.True(Math.Abs(secondAfter.Min.X - secondBefore.Min.X) < 1e-6);
        Assert.True(Math.Abs(secondAfter.Max.X - secondBefore.Max.X) < 1e-6);
    }

    [Fact]
    public void BatchTransform_UndoRestoresAllObjectsInOneStep()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Place(session, first, new Vec3(0, 0, 0));
        Place(session, second, new Vec3(10, 0, 0));
        Aabb firstBefore = WorldBounds(session, first);
        Aabb secondBefore = WorldBounds(session, second);

        Assert.Equal(2, session.UpdateObjects(new[] { first, second }, true, new Vec3(0, 3, 0), Vec3.Zero, new Vec3(1, 1, 1)));
        Assert.StartsWith("Transform 2 objects", session.UndoDescription);

        session.Undo();

        Aabb firstRestored = WorldBounds(session, first);
        Aabb secondRestored = WorldBounds(session, second);
        Assert.True(Math.Abs(firstRestored.Min.Y - firstBefore.Min.Y) < 1e-6);
        Assert.True(Math.Abs(secondRestored.Min.Y - secondBefore.Min.Y) < 1e-6);
    }

    [Fact]
    public void BatchBaseColor_ColorsEveryObjectWithOneUndo()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Vec3 beforeFirst = session.GetMaterialModel(first)!.BaseColor;
        Vec3 beforeSecond = session.GetMaterialModel(second)!.BaseColor;
        Vec3 red = new(0.8, 0.05, 0.05);

        int updated = session.SetObjectBaseColor(new[] { first, second }, red);

        Assert.Equal(2, updated);
        Assert.Equal(red.X, session.GetMaterialModel(first)!.BaseColor.X, 6);
        Assert.Equal(red.X, session.GetMaterialModel(second)!.BaseColor.X, 6);

        session.Undo();
        Assert.Equal(beforeFirst.X, session.GetMaterialModel(first)!.BaseColor.X, 6);
        Assert.Equal(beforeSecond.X, session.GetMaterialModel(second)!.BaseColor.X, 6);
    }

    [Fact]
    public void BatchMaterialProperties_PreservesBaseColors()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Assert.True(session.SetObjectBaseColor(first, new Vec3(0.8, 0.05, 0.05)));
        Assert.True(session.SetObjectBaseColor(second, new Vec3(0.05, 0.05, 0.8)));
        ComposerMaterialProperties properties = session.GetMaterialModel(first)!.DirectProperties with { Metallic = 0.9 };

        int updated = session.SetObjectMaterialProperties(new[] { first, second }, properties);

        Assert.Equal(2, updated);
        Assert.Equal(0.8, session.GetMaterialModel(first)!.BaseColor.X, 6);
        Assert.Equal(0.8, session.GetMaterialModel(second)!.BaseColor.Z, 6);
        Assert.Equal(0.9, session.GetMaterialModel(first)!.Metallic, 6);
        Assert.Equal(0.9, session.GetMaterialModel(second)!.Metallic, 6);
    }

    [Fact]
    public void TopmostSelection_DropsNestedChildren()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        int? parent = session.GroupObjects(new[] { first, second });
        Assert.NotNull(parent);

        IReadOnlyList<int> topmost = session.GetTopmostSelection(new[] { parent!.Value, first, second });

        Assert.Single(topmost);
        Assert.Equal(parent.Value, topmost[0]);
    }

    [Fact]
    public void BatchDelete_RemovesEveryTarget()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        int third = session.InsertPrimitive("Cube");
        session.SetSelectedObjects(new[] { first, second }, first);
        Assert.Equal(3, session.ObjectCount);

        int deleted = session.DeleteObjects(new[] { first, second });

        Assert.Equal(2, deleted);
        Assert.Equal(1, session.ObjectCount);
        Assert.NotNull(session.GetObjectState(third));
    }

    [Fact]
    public void BatchDuplicate_ClonesEveryTarget()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");

        IReadOnlyList<int> created = session.DuplicateObjects(new[] { first, second });

        Assert.Equal(2, created.Count);
        Assert.DoesNotContain(first, created);
        Assert.DoesNotContain(second, created);
        Assert.Equal(4, session.ObjectCount);
        Assert.NotNull(session.GetObjectState(created[0]));
        Assert.NotNull(session.GetObjectState(created[1]));
    }

    [Fact]
    public void FrameObjects_AimsAtUnionCenter()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Place(session, first, new Vec3(0, 0, 0));
        Place(session, second, new Vec3(10, 0, 0));
        Aabb firstBox = WorldBounds(session, first);
        Aabb secondBox = WorldBounds(session, second);
        Vec3 expected = new(
            (Math.Min(firstBox.Min.X, secondBox.Min.X) + Math.Max(firstBox.Max.X, secondBox.Max.X)) * 0.5,
            (Math.Min(firstBox.Min.Y, secondBox.Min.Y) + Math.Max(firstBox.Max.Y, secondBox.Max.Y)) * 0.5,
            (Math.Min(firstBox.Min.Z, secondBox.Min.Z) + Math.Max(firstBox.Max.Z, secondBox.Max.Z)) * 0.5);

        Assert.True(session.FrameObjects(new[] { first, second }));

        Vec3 target = session.Camera.Snapshot().Target;
        Assert.True(Math.Abs(target.X - expected.X) < 1e-6);
        Assert.True(Math.Abs(target.Y - expected.Y) < 1e-6);
        Assert.True(Math.Abs(target.Z - expected.Z) < 1e-6);
        Assert.False(session.FrameObjects(Array.Empty<int>()));
    }

    [Fact]
    public void BatchGizmoPreviewAndCommit_BakesEveryTarget()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Place(session, first, new Vec3(0, 0, 0));
        Place(session, second, new Vec3(10, 0, 0));
        Aabb firstBefore = WorldBounds(session, first);
        Aabb secondBefore = WorldBounds(session, second);

        int previewed = session.UpdateTransformTargets(new[]
        {
            (first, new Vec3(0, 1, 0), Vec3.Zero, new Vec3(1, 1, 1)),
            (second, new Vec3(0, 1, 0), Vec3.Zero, new Vec3(1, 1, 1))
        });
        Assert.Equal(2, previewed);
        Assert.Equal(1.0, session.GetTransformTargetState(first)!.Position.Y, 9);

        int committed = session.CommitPendingTransforms(new[] { first, second });

        Assert.Equal(2, committed);
        Aabb firstAfter = WorldBounds(session, first);
        Aabb secondAfter = WorldBounds(session, second);
        Assert.True(Math.Abs(firstAfter.Min.Y - firstBefore.Min.Y - 1.0) < 1e-6);
        Assert.True(Math.Abs(secondAfter.Min.Y - secondBefore.Min.Y - 1.0) < 1e-6);
        session.Undo();
        Assert.True(Math.Abs(WorldBounds(session, first).Min.Y - firstBefore.Min.Y) < 1e-6);
    }

    [Fact]
    public void BatchReset_ClearsPendingTransforms()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");
        Assert.Equal(2, session.UpdateTransformTargets(new[]
        {
            (first, new Vec3(0, 1, 0), Vec3.Zero, new Vec3(1, 1, 1)),
            (second, new Vec3(0, 2, 0), Vec3.Zero, new Vec3(1, 1, 1))
        }));

        int reset = session.ResetObjectTransforms(new[] { first, second });

        Assert.Equal(2, reset);
        AssertIdentityBake(session, first);
        AssertIdentityBake(session, second);
    }

    [Fact]
    public void BatchVisibility_AppliesToEveryTarget()
    {
        using ComposerSceneSession session = new();
        int first = session.InsertPrimitive("Cube");
        int second = session.InsertPrimitive("Cube");

        int updated = session.UpdateObjects(new[] { first, second }, false, Vec3.Zero, Vec3.Zero, new Vec3(1, 1, 1));

        Assert.Equal(2, updated);
        Assert.False(session.GetObjectState(first)!.Visible);
        Assert.False(session.GetObjectState(second)!.Visible);
    }
}
