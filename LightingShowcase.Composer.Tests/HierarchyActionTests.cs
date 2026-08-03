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
