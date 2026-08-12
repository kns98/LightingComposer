/*
 * The tests in this file are executable statements of editor behavior. They intentionally use real scene/session
 * objects and inspect externally meaningful results—geometry, hierarchy, material state, serialized output, cache
 * stamps, or timing—so refactors can change implementation details without weakening the contract being tested.
 *
 * `Inserted_model_is_a_parent_node_with_expandable_children` verifies that inserted model is a parent node with
 * expandable children. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene
 * mutation follow production paths rather than mocks. The assertions establish that the operation must explicitly
 * report success; the resulting value/state must exactly match the expected result; the expected entry must
 * remain discoverable.
 *
 * `Expand_and_collapse_change_the_visible_tree_rows` verifies that expand and collapse change the visible tree
 * rows. It uses a real `ComposerSceneSession`, so registration, locking, history, and scene mutation follow
 * production paths rather than mocks. The assertions establish that the operation must explicitly report success;
 * the disallowed path must be rejected; the resulting value/state must exactly match the expected result; the
 * expected entry must remain discoverable; forbidden/unwanted entries must not be exposed.
 */
using LightingShowcase.Composer;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer.Tests;

public sealed class HierarchyActionTests
{
    [Fact]
    public void Inserted_model_is_a_parent_node_with_expandable_children()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);

        IReadOnlyList<SceneObjectInfo> infos = session.GetObjectInfos();
        SceneObjectInfo rootInfo = Assert.Single(infos, info => info.Id == rootId);
        Assert.Equal(0, rootInfo.Depth);
        Assert.True(rootInfo.ChildCount > 0);
        Assert.Contains(infos, info => info.Depth > 0);

        List<ObjectTreeNode> roots = ComposerObjectTree.Build(infos);
        ObjectTreeNode root = Assert.Single(roots, node => node.Id == rootId);
        Assert.NotEmpty(root.Children);
    }

    [Fact]
    public void Expand_and_collapse_change_the_visible_tree_rows()
    {
        using TestModel model = new();
        using ComposerSceneSession session = new();
        int rootId = session.Insert(model.ModelPath, CancellationToken.None);
        List<ObjectTreeNode> roots = ComposerObjectTree.Build(session.GetObjectInfos());
        ObjectTreeNode root = Assert.Single(roots, node => node.Id == rootId);

        IReadOnlyList<int> collapsed = ComposerObjectTree.FlattenVisible(roots, new HashSet<int>());
        Assert.Contains(rootId, collapsed);
        Assert.DoesNotContain(root.Children[0].Id, collapsed);

        HashSet<int> expansion = new();
        Assert.True(ComposerObjectTree.ToggleExpanded(expansion, rootId));
        IReadOnlyList<int> expanded = ComposerObjectTree.FlattenVisible(roots, expansion);
        Assert.Contains(rootId, expanded);
        Assert.Contains(root.Children[0].Id, expanded);
        Assert.True(expanded.Count > collapsed.Count);

        Assert.False(ComposerObjectTree.ToggleExpanded(expansion, rootId));
        IReadOnlyList<int> collapsedAgain = ComposerObjectTree.FlattenVisible(roots, expansion);
        Assert.Equal(collapsed.ToArray(), collapsedAgain.ToArray());
    }
}
