using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Owns editor selection state, hierarchy-tree projection and inspector loading.
/// Viewport picking supplies object/face ids to this controller; it keeps the
/// session, tree, inspector and menu enabled-state synchronized.
/// </summary>
internal sealed class ComposerSelectionController
{
    private const int TrianglePageSize = 200;

    private readonly ComposerSceneSession session;
    private readonly ComposerRenderController renderer;
    private readonly ComposerDialogController dialogs;
    private readonly ComposerMenuController menu;
    private readonly StackPanel objectTreePanel;
    private readonly ComboBox selectionModeBox;
    private readonly TextBlock statusText;
    private readonly TextBox nameBox;
    private readonly CheckBox visibleBox;
    private readonly TextBox positionX;
    private readonly TextBox positionY;
    private readonly TextBox positionZ;
    private readonly TextBox rotationX;
    private readonly TextBox rotationY;
    private readonly TextBox rotationZ;
    private readonly TextBox scaleX;
    private readonly TextBox scaleY;
    private readonly TextBox scaleZ;
    private readonly Button parametersButton;
    private readonly Button materialButton;
    private readonly Button applyButton;
    private readonly Button frameButton;
    private readonly Button resetTransformButton;
    private readonly Button duplicateButton;
    private readonly Button groupButton;
    private readonly Button ungroupButton;
    private readonly Button deleteButton;
    private readonly Func<ComposerSelectionMode> selectedMode;
    private readonly Action<ComposerSelectionMode> selectMode;
    private readonly Action updateHistory;

    private readonly HashSet<int> expandedObjectIds = new();
    private readonly Dictionary<int, int> trianglePageOffsets = new();
    private bool treeExpansionInitialized;
    private int? selectedObjectId;
    private readonly HashSet<int> selectedObjectIds = new();
    private int? selectedTriangleGroupId;
    private int? selectedTriangleIndex;

    public ComposerSelectionController(
        ComposerSceneSession session,
        ComposerRenderController renderer,
        ComposerDialogController dialogs,
        ComposerMenuController menu,
        StackPanel objectTreePanel,
        ComboBox selectionModeBox,
        TextBlock statusText,
        TextBox nameBox,
        CheckBox visibleBox,
        TextBox positionX,
        TextBox positionY,
        TextBox positionZ,
        TextBox rotationX,
        TextBox rotationY,
        TextBox rotationZ,
        TextBox scaleX,
        TextBox scaleY,
        TextBox scaleZ,
        Button parametersButton,
        Button materialButton,
        Button applyButton,
        Button frameButton,
        Button resetTransformButton,
        Button duplicateButton,
        Button groupButton,
        Button ungroupButton,
        Button deleteButton,
        Func<ComposerSelectionMode> selectedMode,
        Action<ComposerSelectionMode> selectMode,
        Action updateHistory)
    {
        this.session = session;
        this.renderer = renderer;
        this.dialogs = dialogs;
        this.menu = menu;
        this.objectTreePanel = objectTreePanel;
        this.selectionModeBox = selectionModeBox;
        this.statusText = statusText;
        this.nameBox = nameBox;
        this.visibleBox = visibleBox;
        this.positionX = positionX;
        this.positionY = positionY;
        this.positionZ = positionZ;
        this.rotationX = rotationX;
        this.rotationY = rotationY;
        this.rotationZ = rotationZ;
        this.scaleX = scaleX;
        this.scaleY = scaleY;
        this.scaleZ = scaleZ;
        this.parametersButton = parametersButton;
        this.materialButton = materialButton;
        this.applyButton = applyButton;
        this.frameButton = frameButton;
        this.resetTransformButton = resetTransformButton;
        this.duplicateButton = duplicateButton;
        this.groupButton = groupButton;
        this.ungroupButton = ungroupButton;
        this.deleteButton = deleteButton;
        this.selectedMode = selectedMode;
        this.selectMode = selectMode;
        this.updateHistory = updateHistory;
    }

    public int? ActiveObjectId
    {
        get => selectedObjectId;
        set => selectedObjectId = value;
    }

    public HashSet<int> SelectedObjectIds => selectedObjectIds;
    public int SelectedObjectCount => selectedObjectIds.Count;

    public void ResetForScene()
    {
        selectedObjectId = null;
        selectedObjectIds.Clear();
        ClearVirtualTriangleSelection();
        expandedObjectIds.Clear();
        trianglePageOffsets.Clear();
        treeExpansionInitialized = false;
    }

    public void SetSingleSelection(int? objectId, bool expand = false)
    {
        selectedObjectIds.Clear();
        selectedObjectId = objectId;
        if (objectId is int id)
        {
            selectedObjectIds.Add(id);
            if (expand)
                expandedObjectIds.Add(id);
        }
        ClearVirtualTriangleSelection();
    }

    public void SetMultipleSelection(IEnumerable<int> ids, int? active)
    {
        selectedObjectIds.Clear();
        foreach (int id in ids)
            selectedObjectIds.Add(id);
        selectedObjectId = active;
        ClearVirtualTriangleSelection();
    }

    public void AddExpanded(int id) => expandedObjectIds.Add(id);

    public void RemoveCachedObject(int id)
    {
        expandedObjectIds.Remove(id);
        trianglePageOffsets.Remove(id);
    }

    public void FrameSelected()
    {
        if (selectedObjectId is not int id || !session.FrameObject(id))
            return;
        _ = renderer.RequestRenderAsync(interactive: false);
    }

    public void RefreshObjectTree(int? preferredSelection = null, bool syncSessionSelection = true)
    {
        IReadOnlyList<SceneObjectInfo> infos = session.GetObjectInfos();
        List<ObjectTreeNode> roots = ComposerObjectTree.Build(infos);
        HashSet<int> validIds = infos.Select(info => info.Id).ToHashSet();
        selectedObjectIds.RemoveWhere(id => !validIds.Contains(id));

        int? target = preferredSelection ?? selectedObjectId;
        if (target.HasValue && ComposerObjectTree.Find(roots, target.Value) == null)
            target = null;
        selectedObjectId = target;
        if (preferredSelection.HasValue && selectedObjectId.HasValue && selectedObjectIds.Count <= 1)
        {
            selectedObjectIds.Clear();
            selectedObjectIds.Add(selectedObjectId.Value);
        }
        else if (selectedObjectId.HasValue && selectedObjectIds.Count == 0)
        {
            selectedObjectIds.Add(selectedObjectId.Value);
        }

        if (!treeExpansionInitialized)
            treeExpansionInitialized = true;

        expandedObjectIds.RemoveWhere(id => !validIds.Contains(id));
        foreach (int staleId in trianglePageOffsets.Keys.Where(id => !validIds.Contains(id)).ToList())
            trianglePageOffsets.Remove(staleId);

        objectTreePanel.Children.Clear();
        foreach (ObjectTreeNode root in roots)
            objectTreePanel.Children.Add(BuildObjectTreeControl(root, depth: 0));

        if (syncSessionSelection)
        {
            if (selectedTriangleGroupId is int triangleGroupId &&
                selectedTriangleIndex is int triangleIndex &&
                selectedObjectId == triangleGroupId &&
                session.SetSelectedTriangle(triangleGroupId, triangleIndex))
            {
            }
            else if (session.SelectionMode == ComposerSelectionMode.Object || !session.HasMeshComponentSelection)
            {
                selectedTriangleGroupId = null;
                selectedTriangleIndex = null;
                session.SetSelectedObject(selectedObjectId);
            }
        }

        LoadInspectorFromSelection();
        updateHistory();
    }

    private Control BuildObjectTreeControl(ObjectTreeNode node, int depth)
    {
        StackPanel branch = new() { Spacing = 2 };
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("24,*"),
            ColumnSpacing = 2,
            Margin = new Thickness(depth * 14, 0, 0, 0)
        };

        bool hasTriangleDetails = node.LocalTriangleCount > 0;
        bool hasExpandableContent = node.Children.Count > 0 || hasTriangleDetails;
        if (hasExpandableContent)
        {
            bool expanded = expandedObjectIds.Contains(node.Id);
            Button toggle = new()
            {
                Content = expanded ? "▾" : "▸",
                Width = 22,
                Height = 26,
                Padding = new Thickness(0)
            };
            toggle.Click += (_, _) =>
            {
                ComposerObjectTree.ToggleExpanded(expandedObjectIds, node.Id);
                RefreshObjectTree(selectedObjectId);
            };
            row.Children.Add(toggle);
        }
        else
        {
            row.Children.Add(new TextBlock
            {
                Text = "△",
                Width = 22,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Opacity = 0.55
            });
        }

        Button select = new()
        {
            Content = new TextBlock
            {
                Text = node.Label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            MinHeight = 28,
            Padding = new Thickness(6, 3),
            BorderThickness = new Thickness(0),
            Background = selectedObjectIds.Contains(node.Id)
                ? new SolidColorBrush(Color.FromArgb(110, 255, 125, 40))
                : Brushes.Transparent
        };
        select.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(select).Properties.IsLeftButtonPressed)
                return;
            SelectObject(node.Id, args.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control));
            args.Handled = true;
        };
        row.Children.Add(select);
        Grid.SetColumn(select, 1);
        branch.Children.Add(row);

        if (hasExpandableContent && expandedObjectIds.Contains(node.Id))
        {
            foreach (ObjectTreeNode child in node.Children)
                branch.Children.Add(BuildObjectTreeControl(child, depth + 1));

            if (hasTriangleDetails)
                AddLazyFaceRows(branch, node, depth + 1);
        }

        return branch;
    }

    private void AddLazyFaceRows(StackPanel branch, ObjectTreeNode node, int depth)
    {
        int faceCount = session.GetFaceCount(node.Id);
        if (faceCount <= 0)
            return;

        bool open = trianglePageOffsets.TryGetValue(node.Id, out int pageOffset);
        pageOffset = Math.Clamp(pageOffset, 0, Math.Max(0, faceCount - 1));

        if (!open)
        {
            Button show = new()
            {
                Content = $"… show faces ({faceCount:N0})",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Margin = new Thickness(depth * 14 + 20, 2, 4, 2),
                Padding = new Thickness(8, 3),
                MinHeight = 26
            };
            show.Click += (_, _) =>
            {
                trianglePageOffsets[node.Id] = 0;
                RefreshObjectTree(selectedObjectId);
            };
            branch.Children.Add(show);
            return;
        }

        IReadOnlyList<ComposerFaceInfo> page = session.GetFaceInfos(node.Id, pageOffset, TrianglePageSize);
        foreach (ComposerFaceInfo face in page)
        {
            bool selectedFace = selectedTriangleGroupId == node.Id &&
                                selectedTriangleIndex == face.PrimaryTriangleIndex;
            Button faceRow = new()
            {
                Content = new TextBlock
                {
                    Text = $"▱ {face.Label}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = 12
                },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Margin = new Thickness(depth * 14 + 20, 1, 4, 1),
                Padding = new Thickness(8, 2),
                MinHeight = 24,
                BorderThickness = new Thickness(0),
                Background = selectedFace
                    ? new SolidColorBrush(Color.FromArgb(95, 255, 125, 40))
                    : Brushes.Transparent
            };
            faceRow.Click += (_, _) => SelectTriangle(node.Id, face.PrimaryTriangleIndex);
            branch.Children.Add(faceRow);
        }

        int pageEnd = pageOffset + page.Count;
        Grid controls = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 6,
            Margin = new Thickness(depth * 14 + 20, 2, 4, 2)
        };

        Button previous = new() { Content = "… previous", IsEnabled = pageOffset > 0, Padding = new Thickness(8, 3) };
        previous.Click += (_, _) =>
        {
            trianglePageOffsets[node.Id] = Math.Max(0, pageOffset - TrianglePageSize);
            RefreshObjectTree(selectedObjectId);
        };
        controls.Children.Add(previous);

        Button next = new()
        {
            Content = $"… next ({pageEnd:N0}/{faceCount:N0})",
            IsEnabled = pageEnd < faceCount,
            Padding = new Thickness(8, 3)
        };
        next.Click += (_, _) =>
        {
            trianglePageOffsets[node.Id] = Math.Min(Math.Max(0, faceCount - 1), pageOffset + TrianglePageSize);
            RefreshObjectTree(selectedObjectId);
        };
        controls.Children.Add(next);
        Grid.SetColumn(next, 1);

        Button hide = new()
        {
            Content = "… hide faces",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Thickness(8, 3)
        };
        hide.Click += (_, _) =>
        {
            trianglePageOffsets.Remove(node.Id);
            if (selectedTriangleGroupId == node.Id)
                ClearVirtualTriangleSelection();
            RefreshObjectTree(selectedObjectId);
        };
        controls.Children.Add(hide);
        Grid.SetColumn(hide, 2);
        branch.Children.Add(controls);
    }

    public void LoadInspectorFromSelection()
    {
        if (selectedObjectId is not int id || session.GetObjectState(id) is not ComposerObjectState state)
        {
            SetInspectorEnabled(false);
            return;
        }

        ComposerObjectState transformState = session.GetTransformTargetState(id) ?? state;
        SetInspectorEnabled(true);
        nameBox.Text = state.Name;
        visibleBox.IsChecked = state.Visible;
        WriteVector(transformState.Position, positionX, positionY, positionZ);
        WriteVector(transformState.Rotation * (180.0 / Math.PI), rotationX, rotationY, rotationZ);
        WriteVector(transformState.Scale, scaleX, scaleY, scaleZ);
    }

    public void SetInspectorEnabled(bool enabled)
    {
        nameBox.IsEnabled = enabled;
        visibleBox.IsEnabled = enabled;
        parametersButton.IsEnabled = enabled && selectedObjectId is int parameterId && session.CanEditPrimitiveParameters(parameterId);
        materialButton.IsEnabled = enabled && selectedObjectId is int materialId && session.GetMaterialModel(materialId) != null;
        foreach (TextBox box in new[]
                 {
                     positionX, positionY, positionZ,
                     rotationX, rotationY, rotationZ,
                     scaleX, scaleY, scaleZ
                 })
            box.IsEnabled = enabled;
        applyButton.IsEnabled = enabled;
        frameButton.IsEnabled = enabled;
        resetTransformButton.IsEnabled = enabled;
        duplicateButton.IsEnabled = enabled && selectedObjectIds.Count <= 1;
        groupButton.IsEnabled = enabled && !renderer.IsRendering && selectedObjectIds.Count >= 2 && session.CanGroupObjects(selectedObjectIds);
        IEnumerable<int> ungroupTargets = selectedObjectIds.Count > 0
            ? selectedObjectIds
            : selectedObjectId is int id ? new[] { id } : Array.Empty<int>();
        ungroupButton.IsEnabled = enabled && session.CanUngroupObjects(ungroupTargets);
        deleteButton.IsEnabled = enabled;
        menu.SyncEnabledState();
    }

    public void DeselectObjectFromViewport()
    {
        dialogs.CloseEditors();
        selectedObjectId = null;
        selectedObjectIds.Clear();
        ClearVirtualTriangleSelection();
        session.SetSelectedObject(null);
        RefreshObjectTree();
        statusText.Text = "Selection cleared.";
    }

    public void SelectObject(int id, bool toggle = false)
    {
        if (session.GetObjectState(id) == null)
            return;

        if (toggle)
        {
            if (!selectedObjectIds.Add(id))
            {
                selectedObjectIds.Remove(id);
                if (selectedObjectId == id)
                    selectedObjectId = selectedObjectIds.Count > 0 ? selectedObjectIds.Last() : null;
            }
            else
            {
                selectedObjectId = id;
            }
        }
        else
        {
            selectedObjectIds.Clear();
            selectedObjectIds.Add(id);
            selectedObjectId = id;
        }

        if (selectedObjectId is not int activeId)
        {
            session.SetSelectedObject(null);
            dialogs.CloseEditors();
            RefreshObjectTree();
            statusText.Text = "Selection cleared.";
            _ = renderer.RequestRenderAsync(interactive: false);
            return;
        }

        if (!dialogs.HasPrimitiveEditorFor(activeId))
            dialogs.ClosePrimitiveParameters();
        if (!dialogs.HasMaterialEditorFor(activeId))
            dialogs.CloseMaterialEditor();
        selectMode(ComposerSelectionMode.Object);
        ClearVirtualTriangleSelection();
        session.SetSelectedObject(activeId);
        RefreshObjectTree(activeId);
        statusText.Text = selectedObjectIds.Count > 1
            ? $"{selectedObjectIds.Count} objects selected. Ctrl-click toggles selection; Group combines sibling objects."
            : "Object selected.";
        _ = renderer.RequestRenderAsync(interactive: false);
    }

    public void SelectTriangle(int groupId, int triangleIndex)
    {
        if (!session.SetSelectedTriangle(groupId, triangleIndex))
            return;

        selectMode(ComposerSelectionMode.Face);
        selectedObjectId = groupId;
        selectedObjectIds.Clear();
        selectedObjectIds.Add(groupId);
        selectedTriangleGroupId = groupId;
        selectedTriangleIndex = triangleIndex;
        RefreshObjectTree(groupId);
        statusText.Text = "Selected logical polygon face. Right-click it for Extrude/Inset, or drag the move gizmo to move the whole face.";
        _ = renderer.RequestRenderAsync(interactive: false);
    }

    public void ClearVirtualTriangleSelection()
    {
        selectedTriangleGroupId = null;
        selectedTriangleIndex = null;
    }

    private static void WriteVector(Vec3 value, TextBox x, TextBox y, TextBox z)
    {
        x.Text = value.X.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        y.Text = value.Y.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        z.Text = value.Z.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }
}
