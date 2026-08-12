/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 *
 * `Transform_work_item_contains_only_plain_data_and_no_Avalonia_objects` verifies that transform work item
 * contains only plain data and no avalonia objects. The assertions establish that forbidden/unwanted entries must
 * not be exposed. Representative cases include `Avalonia`.
 *
 * `Captured_transform_work_item_can_execute_on_a_worker_thread` verifies that captured transform work item can
 * execute on a worker thread. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene
 * mutation follow production paths rather than mocks. The assertions establish that the operation must explicitly
 * report success; the resulting value/state must exactly match the expected result; the operation must produce an
 * observable change. Representative cases include `3`, `-2`, `1`, `0`, `30`, `1.25`, `Worker transformed`.
 */
using LightingShowcase.Composer;

namespace LightingShowcase.Composer.Tests;

public sealed class InspectorThreadSafetyTests
{
    [Fact]
    public void Transform_work_item_contains_only_plain_data_and_no_Avalonia_objects()
    {
        Type type = typeof(ComposerTransformWorkItem);
        Type[] propertyTypes = type.GetProperties().Select(property => property.PropertyType).ToArray();

        Assert.DoesNotContain(propertyTypes, propertyType =>
            propertyType.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Captured_transform_work_item_can_execute_on_a_worker_thread()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        ComposerModelEvidence before = session.GetModelEvidence(rootId)!;

        ComposerTransformRequest request = ComposerTransformRequest.Parse(
            "3", "-2", "1",
            "0", "30", "0",
            "1.25", "1.25", "1.25");
        ComposerTransformWorkItem workItem = new(rootId, "Worker transformed", true, request);

        bool updated = await Task.Run(() => workItem.Apply(session));
        ComposerModelEvidence after = session.GetModelEvidence(rootId)!;

        Assert.True(updated);
        Assert.True(after.SceneRevision > before.SceneRevision);
        Assert.NotEqual(before.LocalGeometryHash, after.LocalGeometryHash);
        Assert.Equal("Worker transformed", session.GetObjectState(rootId)!.Name);
    }
}
