/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 *
 * `MultiTriangleModel` is a caller/UI-facing snapshot of domain state; it deliberately avoids handing out the
 * live mutable scene object that produced it.
 *
 * `Triangle_details_are_retrieved_in_pages_without_adding_scene_nodes` verifies that triangle details are
 * retrieved in pages without adding scene nodes. It uses a real `ComposerSceneSession`, so registration, locking,
 * history, and scene mutation follow production paths rather than mocks. The assertions establish that the
 * resulting value/state must exactly match the expected result; the expected entry must remain discoverable.
 * Representative cases include `Triangle`.
 *
 * `Root_node_can_be_ungrouped_and_undo_restores_it` verifies that root node can be ungrouped and undo restores
 * it. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene mutation follow
 * production paths rather than mocks. Undo is exercised to prove the previous state was actually captured, not
 * merely that the forward edit looked correct. The assertions establish that required objects/resources must
 * resolve; the absent case must remain absent; the operation must explicitly report success; the resulting
 * value/state must exactly match the expected result; the expected entry must remain discoverable.
 *
 * `Nested_mesh_node_can_be_ungrouped_without_first_ungrouping_the_root` verifies that nested mesh node can be
 * ungrouped without first ungrouping the root. It uses a real `ComposerSceneSession`, so registration, locking,
 * history, and scene mutation follow production paths rather than mocks. Undo is exercised to prove the previous
 * state was actually captured, not merely that the forward edit looked correct. The assertions establish that
 * required objects/resources must resolve; the absent case must remain absent; the operation must explicitly
 * report success; the resulting value/state must exactly match the expected result.
 *
 * The `MultiTriangleModel` constructor establishes a valid default state before the instance can be used.
 *
 * `Dispose` ends this object’s active lifetime: owned cancellations/resources/listeners are released so completed
 * windows/renderers do not keep receiving work or retain unmanaged memory.
 */
using LightingShowcase.Composer;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class HierarchyLazyTriangleTests
{
    [Fact]
    public void Triangle_details_are_retrieved_in_pages_without_adding_scene_nodes()
    {
        using MultiTriangleModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        SceneObjectInfo mesh = session.GetObjectInfos()
            .First(info => info.LocalTriangleCount >= 5);
        int objectCountBefore = session.ObjectCount;

        IReadOnlyList<ComposerTriangleInfo> firstPage = session.GetTriangleInfos(mesh.Id, 0, 2);
        IReadOnlyList<ComposerTriangleInfo> secondPage = session.GetTriangleInfos(mesh.Id, 2, 2);

        Assert.Equal(2, firstPage.Count);
        Assert.Equal(2, secondPage.Count);
        Assert.Equal(0, firstPage[0].Index);
        Assert.Equal(2, secondPage[0].Index);
        Assert.All(firstPage.Concat(secondPage), item => Assert.Contains("Triangle", item.Label));
        Assert.Equal(objectCountBefore, session.ObjectCount);
        Assert.Equal(rootId, session.GetObjectInfos().First(info => info.Id == rootId).Id);
    }

    [Fact]
    public void Root_node_can_be_ungrouped_and_undo_restores_it()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        Assert.True(session.CanUngroupObject(rootId));

        IReadOnlyList<int> promoted = session.UngroupObject(rootId);
        Assert.NotEmpty(promoted);
        Assert.Null(session.GetObjectState(rootId));
        Assert.All(promoted, id => Assert.Equal(0, session.GetObjectInfos().Single(info => info.Id == id).Depth));

        Assert.Equal(rootId, session.Undo());
        Assert.NotNull(session.GetObjectState(rootId));
        Assert.Contains(session.GetObjectInfos(), info => info.Id == rootId && info.Depth == 0);
    }


    [Fact]
    public void Nested_mesh_node_can_be_ungrouped_without_first_ungrouping_the_root()
    {
        using MultiTriangleModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        SceneObjectInfo mesh = session.GetObjectInfos()
            .First(info => info.ParentId == rootId && info.LocalTriangleCount >= 5);
        Assert.True(session.CanUngroupObject(mesh.Id));

        IReadOnlyList<int> promoted = session.UngroupObject(mesh.Id);

        Assert.NotEmpty(promoted);
        Assert.Null(session.GetObjectState(mesh.Id));
        Assert.All(promoted, id => Assert.Equal((int?)rootId, session.GetObjectInfos().Single(info => info.Id == id).ParentId));
        Assert.Equal(mesh.Id, session.Undo());
        Assert.NotNull(session.GetObjectState(mesh.Id));
    }

    private sealed class MultiTriangleModel : IDisposable
    {
        public MultiTriangleModel()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "LightingShowcaseComposerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            ModelPath = Path.Combine(DirectoryPath, "five-triangles.obj");
            File.WriteAllText(ModelPath, """
                o Mesh
                v 0 0 0
                v 1 0 0
                v 1 1 0
                v 0 1 0
                v 0.5 0.5 1
                f 1 2 5
                f 2 3 5
                f 3 4 5
                f 4 1 5
                f 1 4 3
                """);
        }

        public string DirectoryPath { get; }
        public string ModelPath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DirectoryPath, recursive: true); }
            catch { }
        }
    }
}
