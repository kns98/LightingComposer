using Avalonia.Controls;
using Avalonia.Layout;

namespace LightingShowcase.Composer;

/// <summary>
/// Builds and synchronizes the application menu. It owns all MenuItem instances
/// so ComposerWindow does not carry menu implementation state.
/// </summary>
internal sealed class ComposerMenuController
{
    private readonly ComboBox rendererBox;
    private readonly ComboBox selectionModeBox;
    private readonly ComboBox gizmoModeBox;
    private readonly ComboBox moveAxisBox;

    private readonly Button newButton;
    private readonly Button openButton;
    private readonly Button insertButton;
    private readonly Button addPrimitiveButton;
    private readonly Button saveButton;
    private readonly Button exportButton;
    private readonly Button undoButton;
    private readonly Button redoButton;
    private readonly Button duplicateButton;
    private readonly Button groupButton;
    private readonly Button ungroupButton;
    private readonly Button deleteButton;
    private readonly Button parametersButton;
    private readonly Button materialButton;
    private readonly Button applyButton;
    private readonly Button frameButton;
    private readonly Button resetTransformButton;
    private readonly Button renderSettingsButton;

    private readonly Func<bool> objectMode;

    private readonly MenuItem newMenuItem;
    private readonly MenuItem openMenuItem;
    private readonly MenuItem insertMenuItem;
    private readonly MenuItem saveMenuItem;
    private readonly MenuItem exportMenuItem;
    private readonly MenuItem undoMenuItem;
    private readonly MenuItem redoMenuItem;
    private readonly MenuItem duplicateMenuItem;
    private readonly MenuItem groupMenuItem;
    private readonly MenuItem ungroupMenuItem;
    private readonly MenuItem deleteMenuItem;
    private readonly MenuItem parametersMenuItem;
    private readonly MenuItem materialMenuItem;
    private readonly MenuItem applyTransformMenuItem;
    private readonly MenuItem frameSelectedMenuItem;
    private readonly MenuItem resetTransformMenuItem;
    private readonly MenuItem renderSettingsMenuItem;
    private readonly MenuItem[] primitiveMenuItems;
    private readonly MenuItem[] rendererMenuItems;
    private readonly MenuItem[] selectionModeMenuItems;
    private readonly MenuItem[] gizmoModeMenuItems;
    private readonly MenuItem[] moveAxisMenuItems;

    public ComposerMenuController(
        ComboBox rendererBox,
        ComboBox selectionModeBox,
        ComboBox gizmoModeBox,
        ComboBox moveAxisBox,
        Button newButton,
        Button openButton,
        Button insertButton,
        Button addPrimitiveButton,
        Button saveButton,
        Button exportButton,
        Button undoButton,
        Button redoButton,
        Button duplicateButton,
        Button groupButton,
        Button ungroupButton,
        Button deleteButton,
        Button parametersButton,
        Button materialButton,
        Button applyButton,
        Button frameButton,
        Button resetTransformButton,
        Button renderSettingsButton,
        IReadOnlyList<string> primitiveLabels,
        IReadOnlyList<string> rendererLabels,
        IReadOnlyList<string> selectionLabels,
        IReadOnlyList<string> gizmoLabels,
        IReadOnlyList<string> axisLabels,
        Func<bool> objectMode,
        Func<Task> newScene,
        Func<Task> open,
        Func<Task> insert,
        Func<Task> save,
        Func<Task> export,
        Func<Task> undo,
        Func<Task> redo,
        Func<Task> duplicate,
        Func<Task> group,
        Func<Task> ungroup,
        Func<Task> delete,
        Func<Task> parameters,
        Func<Task> material,
        Func<Task> applyTransform,
        Func<Task> resetTransform,
        Func<Task> frameSelected,
        Func<int, Task> addPrimitive,
        Action<int> selectRenderer,
        Action<int> selectSelectionMode,
        Action<int> selectGizmo,
        Action<int> selectAxis,
        Func<Task> renderSettings)
    {
        this.rendererBox = rendererBox;
        this.selectionModeBox = selectionModeBox;
        this.gizmoModeBox = gizmoModeBox;
        this.moveAxisBox = moveAxisBox;
        this.newButton = newButton;
        this.openButton = openButton;
        this.insertButton = insertButton;
        this.addPrimitiveButton = addPrimitiveButton;
        this.saveButton = saveButton;
        this.exportButton = exportButton;
        this.undoButton = undoButton;
        this.redoButton = redoButton;
        this.duplicateButton = duplicateButton;
        this.groupButton = groupButton;
        this.ungroupButton = ungroupButton;
        this.deleteButton = deleteButton;
        this.parametersButton = parametersButton;
        this.materialButton = materialButton;
        this.applyButton = applyButton;
        this.frameButton = frameButton;
        this.resetTransformButton = resetTransformButton;
        this.renderSettingsButton = renderSettingsButton;
        this.objectMode = objectMode;

        newMenuItem = MenuCommand("_New", newScene);
        openMenuItem = MenuCommand("_Open…", open);
        insertMenuItem = MenuCommand("_Insert model…", insert);
        saveMenuItem = MenuCommand("_Save scene…", save);
        exportMenuItem = MenuCommand("_Export package…", export);
        undoMenuItem = MenuCommand("_Undo", undo);
        redoMenuItem = MenuCommand("_Redo", redo);
        duplicateMenuItem = MenuCommand("_Duplicate", duplicate);
        groupMenuItem = MenuCommand("_Group", group);
        ungroupMenuItem = MenuCommand("_Ungroup", ungroup);
        deleteMenuItem = MenuCommand("_Delete", delete);
        parametersMenuItem = MenuCommand("_Parameters…", parameters);
        materialMenuItem = MenuCommand("_Material…", material);
        applyTransformMenuItem = MenuCommand("_Apply transform", applyTransform);
        resetTransformMenuItem = MenuCommand("_Reset transform", resetTransform);
        frameSelectedMenuItem = MenuCommand("_Frame selected", frameSelected);
        renderSettingsMenuItem = MenuCommand("_Settings…", renderSettings);

        primitiveMenuItems = CreateCommands(primitiveLabels, addPrimitive);
        rendererMenuItems = CreateRadioItems(rendererLabels, "Renderer", selectRenderer);
        selectionModeMenuItems = CreateRadioItems(selectionLabels, "SelectionMode", selectSelectionMode);
        gizmoModeMenuItems = CreateRadioItems(gizmoLabels, "GizmoMode", selectGizmo);
        moveAxisMenuItems = CreateRadioItems(axisLabels, "MoveAxis", selectAxis);

        Menu = BuildMenu();
        SyncChecks();
        SyncEnabledState();
    }

    public Menu Menu { get; }

    private Menu BuildMenu()
    {
        MenuItem fileMenu = new()
        {
            Header = "_File",
            ItemsSource = new object[] { newMenuItem, openMenuItem, insertMenuItem, new Separator(), saveMenuItem, exportMenuItem }
        };
        MenuItem editMenu = new()
        {
            Header = "_Edit",
            ItemsSource = new object[] { undoMenuItem, redoMenuItem, new Separator(), duplicateMenuItem, groupMenuItem, ungroupMenuItem, deleteMenuItem }
        };
        MenuItem addMenu = new() { Header = "_Add", ItemsSource = primitiveMenuItems.Cast<object>().ToArray() };
        MenuItem objectMenu = new()
        {
            Header = "_Object",
            ItemsSource = new object[] { parametersMenuItem, materialMenuItem, new Separator(), applyTransformMenuItem, resetTransformMenuItem, frameSelectedMenuItem }
        };
        MenuItem selectionMenu = new() { Header = "_Selection mode", ItemsSource = selectionModeMenuItems.Cast<object>().ToArray() };
        MenuItem gizmoMenu = new() { Header = "_Transform gizmo", ItemsSource = gizmoModeMenuItems.Cast<object>().ToArray() };
        MenuItem axisMenu = new() { Header = "Move _axis lock", ItemsSource = moveAxisMenuItems.Cast<object>().ToArray() };
        MenuItem modeMenu = new() { Header = "_Mode", ItemsSource = new object[] { selectionMenu, gizmoMenu, axisMenu } };
        MenuItem rendererMenu = new() { Header = "_Renderer", ItemsSource = rendererMenuItems.Cast<object>().ToArray() };
        MenuItem renderMenu = new() { Header = "_Render", ItemsSource = new object[] { rendererMenu, new Separator(), renderSettingsMenuItem } };

        return new Menu
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new object[] { fileMenu, editMenu, addMenu, objectMenu, modeMenu, renderMenu }
        };
    }

    public void SyncChecks()
    {
        SetChecked(rendererMenuItems, rendererBox.SelectedIndex);
        SetChecked(selectionModeMenuItems, selectionModeBox.SelectedIndex);
        SetChecked(gizmoModeMenuItems, gizmoModeBox.SelectedIndex);
        SetChecked(moveAxisMenuItems, moveAxisBox.SelectedIndex);
    }

    public void SyncEnabledState()
    {
        newMenuItem.IsEnabled = newButton.IsEnabled;
        openMenuItem.IsEnabled = openButton.IsEnabled;
        insertMenuItem.IsEnabled = insertButton.IsEnabled;
        saveMenuItem.IsEnabled = saveButton.IsEnabled;
        exportMenuItem.IsEnabled = exportButton.IsEnabled;
        undoMenuItem.IsEnabled = undoButton.IsEnabled;
        redoMenuItem.IsEnabled = redoButton.IsEnabled;
        duplicateMenuItem.IsEnabled = duplicateButton.IsEnabled;
        groupMenuItem.IsEnabled = groupButton.IsEnabled;
        ungroupMenuItem.IsEnabled = ungroupButton.IsEnabled;
        deleteMenuItem.IsEnabled = deleteButton.IsEnabled;
        parametersMenuItem.IsEnabled = parametersButton.IsEnabled;
        materialMenuItem.IsEnabled = materialButton.IsEnabled;
        applyTransformMenuItem.IsEnabled = applyButton.IsEnabled;
        frameSelectedMenuItem.IsEnabled = frameButton.IsEnabled;
        resetTransformMenuItem.IsEnabled = resetTransformButton.IsEnabled;

        foreach (MenuItem item in primitiveMenuItems)
            item.IsEnabled = addPrimitiveButton.IsEnabled;
        foreach (MenuItem item in rendererMenuItems)
            item.IsEnabled = rendererBox.IsEnabled;
        foreach (MenuItem item in selectionModeMenuItems)
            item.IsEnabled = selectionModeBox.IsEnabled;
        foreach (MenuItem item in gizmoModeMenuItems)
            item.IsEnabled = gizmoModeBox.IsEnabled && objectMode();
        foreach (MenuItem item in moveAxisMenuItems)
            item.IsEnabled = moveAxisBox.IsEnabled && !objectMode();

        renderSettingsMenuItem.IsEnabled = renderSettingsButton.IsEnabled;
    }

    public void SetRenderSettingsEnabled(bool enabled)
    {
        renderSettingsMenuItem.IsEnabled = enabled;
    }

    public void UpdateHistory(string undoLabel, bool canUndo, string redoLabel, bool canRedo)
    {
        undoMenuItem.Header = undoLabel;
        undoMenuItem.IsEnabled = canUndo;
        redoMenuItem.Header = redoLabel;
        redoMenuItem.IsEnabled = canRedo;
    }

    private static MenuItem[] CreateCommands(IReadOnlyList<string> labels, Func<int, Task> action)
    {
        MenuItem[] items = new MenuItem[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            items[i] = MenuCommand(labels[i], () => action(index));
        }
        return items;
    }

    private static MenuItem[] CreateRadioItems(IReadOnlyList<string> labels, string groupName, Action<int> action)
    {
        MenuItem[] items = new MenuItem[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            MenuItem item = new()
            {
                Header = labels[i],
                ToggleType = MenuItemToggleType.Radio,
                GroupName = groupName
            };
            item.Click += (_, _) => action(index);
            items[i] = item;
        }
        return items;
    }

    private static MenuItem MenuCommand(string header, Func<Task> action)
    {
        MenuItem item = new() { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static void SetChecked(MenuItem[] items, int selectedIndex)
    {
        for (int i = 0; i < items.Length; i++)
            items[i].IsChecked = i == selectedIndex;
    }
}
