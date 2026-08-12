using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class ObjectTreeNode
{
    public ObjectTreeNode(int id, string label, int localTriangleCount)
    {
        Id = id;
        Label = label;
        LocalTriangleCount = localTriangleCount;
    }

    public int Id { get; }
    public string Label { get; }
    public int LocalTriangleCount { get; }
    public List<ObjectTreeNode> Children { get; } = new();
    public override string ToString() => Label;
}

/// <summary>One hierarchy projection used by both the UI and tests.</summary>
internal static class ComposerObjectTree
{
    public static List<ObjectTreeNode> Build(IReadOnlyList<SceneObjectInfo> infos)
    {
        List<ObjectTreeNode> roots = new();
        List<ObjectTreeNode> ancestors = new();

        foreach (SceneObjectInfo info in infos)
        {
            ObjectTreeNode node = new(
                info.Id,
                info.ChildCount > 0
                    ? $"{(info.Visible ? "●" : "○")} {info.Name}  [{info.TriangleCount:N0} tris, {info.ChildCount} children]"
                    : $"{(info.Visible ? "●" : "○")} {info.Name}  [{info.TriangleCount:N0} tris]",
                info.LocalTriangleCount);

            int depth = Math.Min(Math.Max(0, info.Depth), ancestors.Count);
            while (ancestors.Count > depth)
                ancestors.RemoveAt(ancestors.Count - 1);

            if (depth == 0)
                roots.Add(node);
            else
                ancestors[depth - 1].Children.Add(node);

            if (ancestors.Count == depth)
                ancestors.Add(node);
            else
                ancestors[depth] = node;
        }

        return roots;
    }

    public static ObjectTreeNode? Find(IEnumerable<ObjectTreeNode> nodes, int id)
    {
        foreach (ObjectTreeNode node in nodes)
        {
            if (node.Id == id)
                return node;

            ObjectTreeNode? child = Find(node.Children, id);
            if (child != null)
                return child;
        }

        return null;
    }

    public static bool ToggleExpanded(ISet<int> expandedIds, int objectId)
    {
        ArgumentNullException.ThrowIfNull(expandedIds);
        if (expandedIds.Add(objectId))
            return true;

        expandedIds.Remove(objectId);
        return false;
    }

    public static IReadOnlyList<int> FlattenVisible(
        IEnumerable<ObjectTreeNode> roots,
        IReadOnlySet<int> expandedIds)
    {
        List<int> result = new();
        foreach (ObjectTreeNode root in roots)
            AddVisible(root, expandedIds, result);
        return result;
    }

    private static void AddVisible(ObjectTreeNode node, IReadOnlySet<int> expandedIds, List<int> result)
    {
        result.Add(node.Id);
        if (!expandedIds.Contains(node.Id))
            return;

        foreach (ObjectTreeNode child in node.Children)
            AddVisible(child, expandedIds, result);
    }
}
