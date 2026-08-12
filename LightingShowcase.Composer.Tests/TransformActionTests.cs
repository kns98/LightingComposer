/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 *
 * `Apply_button_bakes_transform_into_authoritative_local_geometry` verifies that apply button bakes transform
 * into authoritative local geometry. It uses a real `ComposerSceneSession`, so registration, locking, history,
 * and scene mutation follow production paths rather than mocks. Geometry hashes are compared so the test observes
 * actual world-space mesh changes/restoration instead of only transform fields. The assertions establish that the
 * operation must explicitly report success; the resulting value/state must exactly match the expected result; the
 * operation must produce an observable change. Representative cases include `2.5`, `-1.25`, `3`, `0`, `90`, `2`,
 * `1.5`.
 *
 * `Baking_converts_parametric_metadata_to_mesh_and_undo_state_restores_it` verifies that baking converts
 * parametric metadata to mesh and undo state restores it. The assertions establish that the absent case must
 * remain absent; the resulting value/state must exactly match the expected result. Representative cases include
 * `Parametric box`, `cuboid`, `Box`, `width`.
 *
 * `Undo_and_redo_restore_exact_baked_geometry_hashes` verifies that undo and redo restore exact baked geometry
 * hashes. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene mutation follow
 * production paths rather than mocks. Undo is exercised to prove the previous state was actually captured, not
 * merely that the forward edit looked correct. Redo then proves the stored “after” state can be reapplied without
 * replaying the UI gesture. Geometry hashes are compared so the test observes actual world-space mesh
 * changes/restoration instead of only transform fields. The assertions establish that the operation must
 * explicitly report success; the resulting value/state must exactly match the expected result; the operation must
 * produce an observable change. Representative cases include `4`, `2`, `-3`, `15`, `25`, `35`, `1.2`.
 *
 * `Blank_transform_fields_mean_identity_after_the_editor_clears_them` verifies that blank transform fields mean
 * identity after the editor clears them. Representative cases include `, null, `.
 *
 * `Invalid_position_text_is_rejected_before_model_mutation` verifies that invalid position text is rejected
 * before model mutation. The assertions establish that invalid input must fail through the specified exception
 * contract. Representative cases include `0`, `1`.
 *
 * `Non_positive_scale_is_rejected_before_model_mutation` verifies that non positive scale is rejected before
 * model mutation. The assertions establish that invalid input must fail through the specified exception contract.
 * Representative cases include `0`.
 *
 * `RequireEvidence` resolves evidence but treats absence as a programming/state error. This is used after
 * preconditions should already guarantee the object exists, making broken invariants fail close to their source.
 */
using LightingShowcase.Composer;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class TransformActionTests
{
    [Fact]
    public void Apply_button_bakes_transform_into_authoritative_local_geometry()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        ComposerModelEvidence before = RequireEvidence(session, rootId);

        ComposerTransformRequest request = ComposerTransformRequest.Parse(
            "2.5", "-1.25", "3",
            "0", "0", "90",
            "2", "1.5", "1");

        Assert.True(request.Apply(session, rootId, "Moved model", visible: true));
        ComposerModelEvidence after = RequireEvidence(session, rootId);
        ComposerObjectState state = session.GetObjectState(rootId)!;

        Assert.True(after.SceneRevision > before.SceneRevision);
        Assert.NotEqual(before.WorldGeometryHash, after.WorldGeometryHash);
        Assert.NotEqual(before.LocalGeometryHash, after.LocalGeometryHash);
        Assert.NotEqual(Center(before.WorldBounds), Center(after.WorldBounds));
        AssertVec3(Vec3.Zero, state.Position);
        AssertVec3(Vec3.Zero, state.Rotation);
        AssertVec3(new Vec3(1, 1, 1), state.Scale);
        Assert.Equal(before.TriangleCount, after.TriangleCount);
        Assert.Equal("Moved model", state.Name);
    }


    [Fact]
    public void Baking_converts_parametric_metadata_to_mesh_and_undo_state_restores_it()
    {
        Scene scene = new();
        scene.Clear();
        SceneObjectGroup group = new(1, "Parametric box");
        group.PrimitiveKind = "cuboid";
        group.PrimitiveSourceName = "Box";
        group.PrimitiveParameters["width"] = 2.0;
        group.AddTriangle(
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(0, 1, 0),
            new Material(new Vec3(0.8, 0.8, 0.8)));
        group.RecalculatePivot();
        scene.ObjectGroups.Add(group);
        BakedGeometryState before = BakedGeometryState.Capture(group);

        group.BakeTransform(new Vec3(3, 0, 0), Vec3.Zero, new Vec3(1, 1, 1));

        Assert.Null(group.PrimitiveKind);
        Assert.Null(group.PrimitiveSourceName);
        Assert.Empty(group.PrimitiveParameters);

        before.Restore(scene);
        Assert.Equal("cuboid", group.PrimitiveKind);
        Assert.Equal("Box", group.PrimitiveSourceName);
        Assert.Equal(2.0, group.PrimitiveParameters["width"]);
    }

    [Fact]
    public void Undo_and_redo_restore_exact_baked_geometry_hashes()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        ComposerModelEvidence original = RequireEvidence(session, rootId);

        ComposerTransformRequest request = ComposerTransformRequest.Parse(
            "4", "2", "-3",
            "15", "25", "35",
            "1.2", "1.3", "1.4");
        Assert.True(request.Apply(session, rootId, "Model", visible: true));
        ComposerModelEvidence transformed = RequireEvidence(session, rootId);
        Assert.NotEqual(original.LocalGeometryHash, transformed.LocalGeometryHash);
        Assert.True(session.CanUndo);

        Assert.Equal(rootId, session.Undo());
        ComposerModelEvidence undone = RequireEvidence(session, rootId);
        Assert.Equal(original.LocalGeometryHash, undone.LocalGeometryHash);
        Assert.Equal(original.WorldGeometryHash, undone.WorldGeometryHash);
        Assert.True(session.CanRedo);

        Assert.Equal(rootId, session.Redo());
        ComposerModelEvidence redone = RequireEvidence(session, rootId);
        Assert.Equal(transformed.LocalGeometryHash, redone.LocalGeometryHash);
        Assert.Equal(transformed.WorldGeometryHash, redone.WorldGeometryHash);
    }

    [Fact]
    public void Blank_transform_fields_mean_identity_after_the_editor_clears_them()
    {
        ComposerTransformRequest request = ComposerTransformRequest.Parse(
            "", null, " ",
            "", null, " ",
            "", null, " ");

        AssertVec3(Vec3.Zero, request.Position);
        AssertVec3(Vec3.Zero, request.RotationRadians);
        AssertVec3(new Vec3(1, 1, 1), request.Scale);
    }

    [Theory]
    [InlineData("NaN", "0", "0")]
    [InlineData("Infinity", "0", "0")]
    public void Invalid_position_text_is_rejected_before_model_mutation(string x, string y, string z)
    {
        Assert.Throws<FormatException>(() => ComposerTransformRequest.Parse(
            x, y, z,
            "0", "0", "0",
            "1", "1", "1"));
    }

    [Theory]
    [InlineData("0", "1", "1")]
    [InlineData("-1", "1", "1")]
    public void Non_positive_scale_is_rejected_before_model_mutation(string x, string y, string z)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ComposerTransformRequest.Parse(
            "0", "0", "0",
            "0", "0", "0",
            x, y, z));
    }

    private static ComposerModelEvidence RequireEvidence(ComposerSceneSession session, int objectId) =>
        session.GetModelEvidence(objectId)
        ?? throw new Xunit.Sdk.XunitException($"No model evidence was returned for object {objectId}.");

    private static Vec3 Center(LightingShowcase.SceneGraph.Aabb bounds) => (bounds.Min + bounds.Max) * 0.5;

    private static void AssertVec3(Vec3 expected, Vec3 actual, double tolerance = 1e-8)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0, tolerance);
    }
}
