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
