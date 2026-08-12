/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 *
 * `ComposerWindow` owns temporary Avalonia presentation/edit state. Values become durable only when accepted and
 * routed through the relevant session/controller operation, preserving validation and cancellation semantics.
 *
 * `RendererChoice` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Kind`, `Label`, `Description`) travel together because consumers need a
 * consistent snapshot rather than reading those values independently from mutable objects.
 *
 * `GizmoModeChoice` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Mode`, `Label`) travel together because consumers need a consistent
 * snapshot rather than reading those values independently from mutable objects.
 *
 * `SelectionModeChoice` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Mode`, `Label`) travel together because consumers need a consistent
 * snapshot rather than reading those values independently from mutable objects.
 *
 * `MoveAxisChoice` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Axis`, `Label`) travel together because consumers need a consistent
 * snapshot rather than reading those values independently from mutable objects.
 *
 * `index` is derived rather than separately stored: it evaluates `{ primitiveBox.SelectedIndex = index`. Keeping
 * the value computed from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `SelectedRenderer` is derived rather than separately stored: it evaluates `rendererBox.SelectedItem as
 * RendererChoice ?? rendererChoices[0]`. Keeping the value computed from its source fields prevents a second
 * cached flag/value from drifting out of sync.
 *
 * `SelectedGizmoMode` is derived rather than separately stored: it evaluates `(gizmoModeBox.SelectedItem as
 * GizmoModeChoice)?.Mode ?? ComposerGizmoMode.Translate`. Keeping the value computed from its source fields
 * prevents a second cached flag/value from drifting out of sync.
 *
 * `SelectedSelectionMode` is derived rather than separately stored: it evaluates `(selectionModeBox.SelectedItem
 * as SelectionModeChoice)?.Mode ?? ComposerSelectionMode.Object`. Keeping the value computed from its source
 * fields prevents a second cached flag/value from drifting out of sync.
 *
 * `SelectedMoveAxisLock` is derived rather than separately stored: it evaluates `(moveAxisBox.SelectedItem as
 * MoveAxisChoice)?.Axis ?? ComposerGizmoAxis.None`. Keeping the value computed from its source fields prevents a
 * second cached flag/value from drifting out of sync.
 *
 * `ToString` returns the human-facing label/name for this value so Avalonia controls display meaningful text
 * instead of the generated record/type representation.
 *
 * `ToString` returns the human-facing label/name for this value so Avalonia controls display meaningful text
 * instead of the generated record/type representation.
 *
 * `ToString` returns the human-facing label/name for this value so Avalonia controls display meaningful text
 * instead of the generated record/type representation.
 *
 * `ToString` returns the human-facing label/name for this value so Avalonia controls display meaningful text
 * instead of the generated record/type representation.
 *
 * The `ComposerWindow` constructor captures `startupArguments`. Those are the dependencies/initial values the
 * instance needs for its lifetime, so callbacks and later operations use the same objects/configuration rather
 * than looking them up globally.
 *
 * `WireEvents` connects the window’s controls and pointer/keyboard lifecycle events to their handlers after
 * construction. Centralizing the wiring makes it easier to see which user actions can trigger editor commands and
 * avoids duplicate subscriptions.
 *
 * `OpenRenderSettingsAsync` opens render settings async using the current selection/session as its initial state.
 * The window/dialog is a temporary editor; durable changes still flow through the session operation it invokes.
 *
 * `UpdateMeshHover` updates mesh hover from the newest input while preserving the identities/metadata/caches that
 * remain valid and invalidating only what the change makes stale.
 *
 * `ClearMeshHoverOverlay` removes/resets mesh hover overlay to its empty/default state. This is an explicit state
 * transition rather than leaving old values around for later code to accidentally reuse.
 *
 * `RunFaceOperationAsync` executes face operation async as one coordinated action and centralizes success/failure
 * handling so callers do not each implement inconsistent exception/UI behavior. Potentially blocking/CPU work
 * runs on a worker task rather than Avalonia’s UI thread.
 *
 * `OpenPrimitiveParameters` opens primitive parameters using the current selection/session as its initial state.
 * The window/dialog is a temporary editor; durable changes still flow through the session operation it invokes.
 *
 * `OpenMaterialEditor` opens material editor using the current selection/session as its initial state. The
 * window/dialog is a temporary editor; durable changes still flow through the session operation it invokes.
 *
 * `SelectGizmoMode` changes the editor’s current gizmo mode choice and synchronizes the controls/overlay behavior
 * that depend on that mode.
 *
 * `UpdateHistoryButtons` updates history buttons from the newest input while preserving the
 * identities/metadata/caches that remain valid and invalidating only what the change makes stale.
 *
 * `SetBusy` sets busy through the owning abstraction instead of exposing a mutable field. That gives the method
 * one place to validate the value and perform any history/cache/UI side effects required by the change.
 */
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class ComposerWindow : Window
{
    private sealed record RendererChoice(ComposerRendererKind Kind, string Label, string Description)
    {
        public override string ToString() => Label;
    }

    private sealed record GizmoModeChoice(ComposerGizmoMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SelectionModeChoice(ComposerSelectionMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record MoveAxisChoice(ComposerGizmoAxis Axis, string Label)
    {
        public override string ToString() => Label;
    }


    private readonly RendererChoice[] rendererChoices =
    [
        new(ComposerRendererKind.Raster, "Raster", "Software z-buffer rasterizer; best for responsive composition."),
        new(ComposerRendererKind.VulkanRaster, "Vulkan raster", "Hardware rasterizer using the complete scene."),
        new(ComposerRendererKind.VulkanCompute, "Vulkan compute", "Compute ray preview using the complete scene."),
        new(ComposerRendererKind.Cpu, "CPU", "CPU ray preview; renders after camera movement finishes.")
    ];

    private readonly GizmoModeChoice[] gizmoModeChoices =
    [
        new(ComposerGizmoMode.Translate, "Move (G)"),
        new(ComposerGizmoMode.Rotate, "Rotate (R)"),
        new(ComposerGizmoMode.Scale, "Scale (S)")
    ];

    private readonly SelectionModeChoice[] selectionModeChoices =
    [
        new(ComposerSelectionMode.Object, "Object (4)"),
        new(ComposerSelectionMode.Vertex, "Vertex (1)"),
        new(ComposerSelectionMode.Edge, "Edge (2)"),
        new(ComposerSelectionMode.Face, "Face (3)")
    ];

    private readonly MoveAxisChoice[] moveAxisChoices =
    [
        new(ComposerGizmoAxis.None, "Axis: Auto (A)"),
        new(ComposerGizmoAxis.X, "Lock X"),
        new(ComposerGizmoAxis.Y, "Lock Y"),
        new(ComposerGizmoAxis.Z, "Lock Z")
    ];

    private readonly string[] primitiveChoices =
    [
        "Plane",
        "Cube",
        "Circle",
        "UV Sphere",
        "Icosphere",
        "Cylinder",
        "Cone",
        "Torus",
        "Grid"
    ];

    private readonly ComposerSceneSession session = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ComposerRenderController renderController;
    private readonly ComposerDialogController dialogController;
    private readonly ComposerFileController fileController;
    private readonly ComposerSceneCommandController sceneCommandController;
    private readonly ViewportNavigationController navigationController;
    private readonly ComposerMenuController menuController;
    private readonly ComposerSelectionController selectionController;
    private readonly ComposerTransformController transformController;
    private readonly ComposerCommandCoordinator commandCoordinator;

    private readonly TextBlock pathText;
    private readonly TextBlock statusText;
    private readonly TextBlock detailsText;
    private readonly ComboBox rendererBox;
    private readonly ComboBox gizmoModeBox;
    private readonly ComboBox selectionModeBox;
    private readonly ComboBox moveAxisBox;
    private readonly ComboBox primitiveBox;
    private readonly ScrollViewer objectTree;
    private readonly StackPanel objectTreePanel;
    private readonly Border viewport;
    private readonly Image image;
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

    private bool leftPressed;
    private Point leftPressPoint;
    private readonly DispatcherTimer hoverPulseTimer;
    private long lastHoverProbeTimestamp;

    public ComposerWindow(string[] startupArguments)
    {
        Title = "LightingShowcase Avalonia Composer";
        Width = 1500;
        Height = 900;
        MinWidth = 980;
        MinHeight = 620;

        newButton = NewButton("New");
        openButton = NewButton("Open…");
        insertButton = NewButton("Insert model…");
        addPrimitiveButton = NewButton("Add primitive");
        saveButton = NewButton("Save scene…");
        exportButton = NewButton("Export package…");
        undoButton = NewButton("Undo");
        redoButton = NewButton("Redo");
        duplicateButton = NewButton("Duplicate");
        groupButton = NewButton("Group");
        ungroupButton = NewButton("Ungroup");
        deleteButton = NewButton("Delete");
        parametersButton = NewButton("Parameters…");
        materialButton = NewButton("Material…");
        applyButton = NewButton("Apply transform");
        frameButton = NewButton("Frame selected");
        resetTransformButton = NewButton("Reset transform");
        renderSettingsButton = NewButton("Settings…");

        pathText = new TextBlock
        {
            Text = "Untitled composition",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        rendererBox = new ComboBox
        {
            ItemsSource = rendererChoices,
            SelectedIndex = 0,
            MinWidth = 150
        };
        gizmoModeBox = new ComboBox
        {
            ItemsSource = gizmoModeChoices,
            SelectedIndex = 0,
            MinWidth = 112
        };
        selectionModeBox = new ComboBox
        {
            ItemsSource = selectionModeChoices,
            SelectedIndex = 0,
            MinWidth = 112
        };
        moveAxisBox = new ComboBox
        {
            ItemsSource = moveAxisChoices,
            SelectedIndex = 0,
            MinWidth = 108,
            IsEnabled = false
        };
        primitiveBox = new ComboBox
        {
            ItemsSource = primitiveChoices,
            SelectedIndex = 0,
            MinWidth = 96
        };
        statusText = new TextBlock
        {
            Text = "Insert or open a model to begin.",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        detailsText = new TextBlock
        {
            Text = rendererChoices[0].Description,
            Opacity = 0.72,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        objectTreePanel = new StackPanel
        {
            Spacing = 2
        };
        objectTree = new ScrollViewer
        {
            Content = objectTreePanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        nameBox = NewTextBox();
        visibleBox = new CheckBox { Content = "Visible", IsChecked = true };
        positionX = NumberBox("0");
        positionY = NumberBox("0");
        positionZ = NumberBox("0");
        rotationX = NumberBox("0");
        rotationY = NumberBox("0");
        rotationZ = NumberBox("0");
        scaleX = NumberBox("1");
        scaleY = NumberBox("1");
        scaleZ = NumberBox("1");

        image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        viewport = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 17, 22)),
            Child = image,
            Focusable = true,
            ClipToBounds = true
        };



        hoverPulseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        hoverPulseTimer.Tick += (_, _) =>
        {
            if (SelectedSelectionMode == ComposerSelectionMode.Object ||
                SelectedRenderer.Kind is not (ComposerRendererKind.Raster or ComposerRendererKind.VulkanRaster))
            {
                return;
            }
            if (session.ToggleMeshHoverPulse())
                _ = renderController.RequestRenderAsync(interactive: true);
        };
        hoverPulseTimer.Start();

        renderController = new ComposerRenderController(
            session,
            image,
            statusText,
            detailsText,
            () => SelectedRenderer.Kind,
            () => SelectedRenderer.Label,
            () => SelectedGizmoMode,
            lifetimeCancellation.Token);

        dialogController = new ComposerDialogController(this, session);
        fileController = new ComposerFileController(this, session, lifetimeCancellation.Token);
        sceneCommandController = new ComposerSceneCommandController(session, lifetimeCancellation.Token);

        navigationController = new ViewportNavigationController(
            this,
            viewport,
            session,
            renderController,
            statusText,
            () => SelectedRenderer.Kind,
            () => SelectedRenderer.Label,
            requestRender => ClearMeshHoverOverlay(requestRender),
            ShowFaceContextMenuAsync,
            lifetimeCancellation.Token);

        menuController = new ComposerMenuController(
            rendererBox, selectionModeBox, gizmoModeBox, moveAxisBox,
            newButton, openButton, insertButton, addPrimitiveButton, saveButton, exportButton,
            undoButton, redoButton, duplicateButton, groupButton, ungroupButton, deleteButton,
            parametersButton, materialButton, applyButton, frameButton, resetTransformButton, renderSettingsButton,
            primitiveChoices,
            rendererChoices.Select(choice => choice.Label).ToArray(),
            selectionModeChoices.Select(choice => choice.Label).ToArray(),
            gizmoModeChoices.Select(choice => choice.Label).ToArray(),
            moveAxisChoices.Select(choice => choice.Label).ToArray(),
            () => SelectedSelectionMode == ComposerSelectionMode.Object,
            NewSceneAsync, BrowseAndOpenAsync, BrowseAndInsertAsync, SaveSceneAsync, ExportPackageAsync,
            UndoAsync, RedoAsync, DuplicateSelectedAsync, GroupSelectedAsync, UngroupSelectedAsync, DeleteSelectedAsync,
            () => { OpenPrimitiveParameters(); return Task.CompletedTask; },
            () => { OpenMaterialEditor(); return Task.CompletedTask; },
            ApplyInspectorAsync, ResetSelectedTransformAsync,
            () => { FrameSelected(); return Task.CompletedTask; },
            async index => { primitiveBox.SelectedIndex = index; await AddPrimitiveAsync(); },
            index => rendererBox.SelectedIndex = index,
            index => selectionModeBox.SelectedIndex = index,
            index => gizmoModeBox.SelectedIndex = index,
            index => moveAxisBox.SelectedIndex = index,
            OpenRenderSettingsAsync);

        selectionController = new ComposerSelectionController(
            session, renderController, dialogController, menuController,
            objectTreePanel, selectionModeBox, statusText,
            nameBox, visibleBox,
            positionX, positionY, positionZ,
            rotationX, rotationY, rotationZ,
            scaleX, scaleY, scaleZ,
            parametersButton, materialButton, applyButton, frameButton, resetTransformButton,
            duplicateButton, groupButton, ungroupButton, deleteButton,
            () => SelectedSelectionMode,
            SelectSelectionMode,
            UpdateHistoryButtons);

        transformController = new ComposerTransformController(
            session, renderController, selectionController, dialogController, viewport, pathText, statusText,
            nameBox, visibleBox,
            positionX, positionY, positionZ, rotationX, rotationY, rotationZ, scaleX, scaleY, scaleZ,
            () => SelectedSelectionMode, () => SelectedGizmoMode, () => SelectedMoveAxisLock,
            () => SelectedRenderer.Kind, () => SelectedRenderer.Label,
            SetBusy, UpdateHistoryButtons, ReportOperationFailure, lifetimeCancellation.Token);
        renderController.ObjectGizmoOnlyProvider = () => transformController.ObjectGizmoOnly;

        commandCoordinator = new ComposerCommandCoordinator(
            session, fileController, sceneCommandController, renderController, dialogController, selectionController,
            pathText, statusText, detailsText, selectionModeBox, primitiveBox, primitiveChoices,
            () => SelectedRenderer.Description,
            SetBusy, UpdateHistoryButtons, transformController.ClearTransformTextBoxes, OpenPrimitiveParameters,
            ReportOperationFailure, lifetimeCancellation.Token);

        Content = ComposerWindowLayout.Build(new ComposerWindowLayout.Controls(
            menuController.Menu, viewport, objectTree,
            duplicateButton, groupButton, ungroupButton, deleteButton,
            nameBox, visibleBox, parametersButton, materialButton,
            positionX, positionY, positionZ,
            rotationX, rotationY, rotationZ,
            scaleX, scaleY, scaleZ,
            applyButton, frameButton, resetTransformButton,
            pathText, statusText, detailsText));
        WireEvents();
        selectionController.RefreshObjectTree();
        selectionController.SetInspectorEnabled(false);

        Opened += async (_, _) =>
        {
            navigationController.AttachWindowsTrackpadInput();
            string? startupPath = startupArguments.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(startupPath))
                await LoadSceneAsync(startupPath);
        };
        Closed += (_, _) => DisposeWindowResources();
    }

    private void WireEvents()
    {
        newButton.Click += async (_, _) => await NewSceneAsync();
        openButton.Click += async (_, _) => await BrowseAndOpenAsync();
        insertButton.Click += async (_, _) => await BrowseAndInsertAsync();
        addPrimitiveButton.Click += async (_, _) => await AddPrimitiveAsync();
        saveButton.Click += async (_, _) => await SaveSceneAsync();
        exportButton.Click += async (_, _) => await ExportPackageAsync();
        undoButton.Click += async (_, _) => await UndoAsync();
        redoButton.Click += async (_, _) => await RedoAsync();
        duplicateButton.Click += async (_, _) => await DuplicateSelectedAsync();
        groupButton.Click += async (_, _) => await GroupSelectedAsync();
        ungroupButton.Click += async (_, _) => await UngroupSelectedAsync();
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        parametersButton.Click += (_, _) => OpenPrimitiveParameters();
        materialButton.Click += (_, _) => OpenMaterialEditor();
        applyButton.Click += async (_, _) => await ApplyInspectorAsync();
        frameButton.Click += (_, _) => FrameSelected();
        resetTransformButton.Click += async (_, _) => await ResetSelectedTransformAsync();
        renderSettingsButton.Click += async (_, _) => await OpenRenderSettingsAsync();

        rendererBox.SelectionChanged += (_, _) =>
        {
            menuController.SyncChecks();
            menuController.SyncEnabledState();
            ComposerRendererKind kind = SelectedRenderer.Kind;
            detailsText.Text = $"{SelectedRenderer.Description} {renderController.GetOptions(kind).Describe(kind)}";
            _ = renderController.RequestRenderAsync(interactive: false);
        };
        selectionModeBox.SelectionChanged += (_, _) =>
        {
            menuController.SyncChecks();
            ComposerSelectionMode mode = SelectedSelectionMode;
            if (mode != ComposerSelectionMode.Object)
            {
                dialogController.ClosePrimitiveParameters();
                dialogController.CloseMaterialEditor();
            }
            session.SetSelectionMode(mode);
            if (mode != ComposerSelectionMode.Object)
            {
                // Component editing has one mesh owner. Collapse any Object-mode
                // Ctrl multi-selection to the active object before editing faces.
                if (selectionController.ActiveObjectId is int activeId &&
                    (selectionController.SelectedObjectIds.Count != 1 || !selectionController.SelectedObjectIds.Contains(activeId)))
                {
                    selectionController.SelectedObjectIds.Clear();
                    selectionController.SelectedObjectIds.Add(activeId);
                    selectionController.RefreshObjectTree(activeId, syncSessionSelection: false);
                }
                SelectGizmoMode(ComposerGizmoMode.Translate);
            }
            else
                SelectMoveAxisLock(ComposerGizmoAxis.None);
            gizmoModeBox.IsEnabled = mode == ComposerSelectionMode.Object;
            moveAxisBox.IsEnabled = mode != ComposerSelectionMode.Object;
            session.SetMeshMoveAxisLock(SelectedMoveAxisLock);
            menuController.SyncEnabledState();
            statusText.Text = mode == ComposerSelectionMode.Object
                ? "Object selection mode."
                : $"{mode} mode: move near a component to preview it, click to select, then drag an axis. X/Y/Z lock movement.";
            _ = renderController.RequestRenderAsync(interactive: false);
        };
        gizmoModeBox.SelectionChanged += (_, _) =>
        {
            menuController.SyncChecks();
            statusText.Text = $"{SelectedGizmoMode} gizmo selected.";
            _ = renderController.RequestRenderAsync(interactive: false);
        };
        moveAxisBox.SelectionChanged += (_, _) =>
        {
            menuController.SyncChecks();
            session.SetMeshMoveAxisLock(SelectedMoveAxisLock);
            if (SelectedSelectionMode != ComposerSelectionMode.Object)
            {
                statusText.Text = SelectedMoveAxisLock == ComposerGizmoAxis.None
                    ? "Move axis unlocked. Click the X, Y, or Z gizmo axis."
                    : $"Movement locked to {SelectedMoveAxisLock}. Only that gizmo axis is active.";
                _ = renderController.RequestRenderAsync(interactive: false);
            }
        };

        viewport.PointerPressed += OnViewportPointerPressed;
        viewport.PointerMoved += OnViewportPointerMoved;
        viewport.PointerReleased += OnViewportPointerReleased;
        viewport.PointerExited += (_, _) => ClearMeshHoverOverlay(requestRender: true);
        viewport.PointerCaptureLost += (_, _) =>
        {
            navigationController.HandleCaptureLost();
            leftPressed = false;
            transformController.CancelActiveDrag();
            _ = renderController.RequestRenderAsync(interactive: false);
        };
        viewport.PointerWheelChanged += (_, e) => navigationController.HandleWheel(e);
        viewport.SizeChanged += (_, _) => renderController.ScheduleResizeRender();
        foreach (TextBox box in transformController.TransformTextBoxes())
            box.KeyDown += OnTransformBoxKeyDown;
        KeyDown += OnWindowKeyDown;
    }

    private RendererChoice SelectedRenderer => rendererBox.SelectedItem as RendererChoice ?? rendererChoices[0];
    private ComposerGizmoMode SelectedGizmoMode =>
        (gizmoModeBox.SelectedItem as GizmoModeChoice)?.Mode ?? ComposerGizmoMode.Translate;
    private ComposerSelectionMode SelectedSelectionMode =>
        (selectionModeBox.SelectedItem as SelectionModeChoice)?.Mode ?? ComposerSelectionMode.Object;
    private ComposerGizmoAxis SelectedMoveAxisLock =>
        (moveAxisBox.SelectedItem as MoveAxisChoice)?.Axis ?? ComposerGizmoAxis.None;


private async Task OpenRenderSettingsAsync()
{
    RendererChoice renderer = SelectedRenderer;
    ComposerRendererKind kind = renderer.Kind;
    renderSettingsButton.IsEnabled = false;
    menuController.SetRenderSettingsEnabled(false);
    try
    {
        ComposerRenderOptions? updated = await dialogController.ShowRenderSettingsAsync(
            kind, renderer.Label, renderController.GetOptions(kind));
        if (updated == null)
            return;

        renderController.SetOptions(kind, updated);
        detailsText.Text = $"{renderer.Label} settings: {updated.Describe(kind)}";

        if (SelectedRenderer.Kind == kind && session.HasRenderableScene)
        {
            renderController.CancelCurrentRender();
            await renderController.RequestRenderAsync(interactive: false);
        }
    }
    finally
    {
        renderSettingsButton.IsEnabled = true;
        menuController.SetRenderSettingsEnabled(true);
    }
}

    private Task NewSceneAsync() => commandCoordinator.NewSceneAsync();

    private Task BrowseAndOpenAsync() => commandCoordinator.BrowseAndOpenAsync();

    private Task BrowseAndInsertAsync() => commandCoordinator.BrowseAndInsertAsync();

    private Task LoadSceneAsync(string path) => commandCoordinator.LoadSceneAsync(path);

    private Task InsertModelAsync(string path) => commandCoordinator.InsertModelAsync(path);

    private Task AddPrimitiveAsync() => commandCoordinator.AddPrimitiveAsync();

    private Task SaveSceneAsync() => commandCoordinator.SaveSceneAsync();

    private Task ExportPackageAsync() => commandCoordinator.ExportPackageAsync();

    private void OnTransformBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _ = ApplyInspectorAsync();
        e.Handled = true;
    }

    private Task ApplyInspectorAsync() => transformController.ApplyInspectorAsync();

    private Task ResetSelectedTransformAsync() => transformController.ResetSelectedTransformAsync();

    private Task UndoAsync() => commandCoordinator.UndoAsync();

    private Task RedoAsync() => commandCoordinator.RedoAsync();

    private Task GroupSelectedAsync() => commandCoordinator.GroupSelectedAsync();

    private Task UngroupSelectedAsync() => commandCoordinator.UngroupSelectedAsync();

    private Task DuplicateSelectedAsync() => commandCoordinator.DuplicateSelectedAsync();

    private Task DeleteSelectedAsync() => commandCoordinator.DeleteSelectedAsync();

    private void FrameSelected() => selectionController.FrameSelected();

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!session.HasRenderableScene)
            return;

        Point position = e.GetPosition(viewport);
        viewport.Focus();

        if (navigationController.TryHandlePointerPressed(e))
            return;

        PointerPoint point = e.GetCurrentPoint(viewport);
        if (point.Properties.IsLeftButtonPressed)
        {
            if (transformController.TryBeginGizmoDrag(position))
            {
                ClearMeshHoverOverlay(requestRender: false);
                e.Pointer.Capture(viewport);
                e.Handled = true;
                return;
            }

            leftPressed = true;
            leftPressPoint = position;
            e.Pointer.Capture(viewport);
            e.Handled = true;
        }
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (navigationController.TryHandlePointerMoved(e))
            return;

        Point current = e.GetPosition(viewport);
        if (transformController.HasActiveDrag)
        {
            transformController.UpdateGizmoDrag(current, e.KeyModifiers);
            e.Handled = true;
            return;
        }

        UpdateMeshHover(current);
    }

    private void UpdateMeshHover(Point viewportPoint)
    {
        if (SelectedSelectionMode == ComposerSelectionMode.Object ||
            leftPressed ||
            transformController.HasActiveDrag ||
            navigationController.IsNavigating)
        {
            ClearMeshHoverOverlay(requestRender: false);
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long minimumInterval = Math.Max(1, Stopwatch.Frequency / 30);
        if (now - lastHoverProbeTimestamp < minimumInterval)
            return;
        lastHoverProbeTimestamp = now;

        if (!transformController.TryViewportToImagePoint(viewportPoint, out Point imagePoint))
        {
            ClearMeshHoverOverlay(requestRender: true);
            return;
        }

        double normalizedX = imagePoint.X / Math.Max(1, renderController.LastRenderWidth);
        double normalizedY = imagePoint.Y / Math.Max(1, renderController.LastRenderHeight);
        ComposerMeshPickResult? hover = session.UpdateMeshHover(
            session.Camera.Snapshot(),
            normalizedX,
            normalizedY,
            renderController.LastRenderWidth,
            renderController.LastRenderHeight,
            SelectedSelectionMode,
            out bool changed);
        if (!changed)
            return;

        if (hover != null)
        {
            string axisHint = SelectedMoveAxisLock == ComposerGizmoAxis.None
                ? "X/Y/Z can lock movement after selection"
                : $"movement locked to {SelectedMoveAxisLock}";
            statusText.Text = $"Nearby {hover.Label} — click to select; {axisHint}.";
        }
        else
        {
            statusText.Text = $"{SelectedSelectionMode} mode: move near a component to preview it.";
        }
        _ = renderController.RequestRenderAsync(interactive: true);
    }

    private void ClearMeshHoverOverlay(bool requestRender)
    {
        if (!session.ClearMeshHover())
            return;
        if (requestRender && session.HasRenderableScene)
            _ = renderController.RequestRenderAsync(interactive: true);
    }

    private async void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (await navigationController.TryHandlePointerReleasedAsync(e))
            return;

        Point releasePoint = e.GetPosition(viewport);
        if (transformController.HasActiveDrag)
        {
            bool handledTransform = await transformController.CommitActiveDragAsync(releasePoint, e.KeyModifiers);
            e.Pointer.Capture(null);
            if (handledTransform)
            {
                e.Handled = true;
                return;
            }
        }

        if (leftPressed)
        {
            leftPressed = false;
            e.Pointer.Capture(null);
            Vector movement = releasePoint - leftPressPoint;
            if (movement.Length <= 5.0)
            {
                if (!transformController.TryViewportToImagePoint(releasePoint, out Point imagePoint))
                {
                    if (SelectedSelectionMode == ComposerSelectionMode.Object)
                    {
                        selectionController.DeselectObjectFromViewport();
                        await renderController.RequestRenderAsync(interactive: false);
                    }
                }
                else
                {
                    double normalizedX = imagePoint.X / Math.Max(1, renderController.LastRenderWidth);
                    double normalizedY = imagePoint.Y / Math.Max(1, renderController.LastRenderHeight);
                    CameraDefinition camera = session.Camera.Snapshot();
                    if (SelectedSelectionMode == ComposerSelectionMode.Object)
                    {
                        int? hitId = await Task.Run(() => session.PickObject(
                            camera,
                            normalizedX,
                            normalizedY,
                            renderController.LastRenderWidth,
                            renderController.LastRenderHeight));
                        if (hitId.HasValue)
                        {
                            selectionController.SelectObject(hitId.Value, e.KeyModifiers.HasFlag(KeyModifiers.Control));
                        }
                        else
                        {
                            // A normal viewport click on empty space clears object
                            // selection, matching common DCC viewport behavior.
                            selectionController.DeselectObjectFromViewport();
                            await renderController.RequestRenderAsync(interactive: false);
                        }
                    }
                    else
                    {
                        ComposerSelectionMode mode = SelectedSelectionMode;
                        ComposerMeshPickResult? picked = await Task.Run(() => session.PickMeshElement(
                            camera,
                            normalizedX,
                            normalizedY,
                            renderController.LastRenderWidth,
                            renderController.LastRenderHeight,
                            mode));
                        if (picked != null)
                        {
                            selectionController.ActiveObjectId = picked.GroupId;
                            selectionController.SelectedObjectIds.Clear();
                            selectionController.SelectedObjectIds.Add(picked.GroupId);
                            // Viewport face selection now targets a polygon face group, not a
                            // raw render triangle. Keep the virtual triangle-tree selection clear.
                            selectionController.ClearVirtualTriangleSelection();
                            selectionController.RefreshObjectTree(picked.GroupId);
                            string axisHint = SelectedMoveAxisLock == ComposerGizmoAxis.None
                                ? "drag X, Y, or Z; press X/Y/Z to lock"
                                : $"movement is locked to {SelectedMoveAxisLock}";
                            statusText.Text = picked.Mode == ComposerSelectionMode.Face
                                ? $"Selected {picked.Label}; right-click for Extrude/Inset, or {axisHint}."
                                : $"Selected {picked.Label}; {axisHint}. Shift is precise and Ctrl snaps.";
                            await renderController.RequestRenderAsync(interactive: false);
                        }
                    }
                }
            }
            e.Handled = true;
        }
    }

    private async Task ShowFaceContextMenuAsync(Point viewportPoint)
    {
        if (SelectedSelectionMode != ComposerSelectionMode.Face ||
            !transformController.TryViewportToImagePoint(viewportPoint, out Point imagePoint))
        {
            return;
        }

        double normalizedX = imagePoint.X / Math.Max(1, renderController.LastRenderWidth);
        double normalizedY = imagePoint.Y / Math.Max(1, renderController.LastRenderHeight);
        ComposerMeshPickResult? picked = await Task.Run(() => session.PickMeshElement(
            session.Camera.Snapshot(),
            normalizedX,
            normalizedY,
            renderController.LastRenderWidth,
            renderController.LastRenderHeight,
            ComposerSelectionMode.Face));
        if (picked == null)
            return;

        selectionController.ActiveObjectId = picked.GroupId;
        selectionController.SelectedObjectIds.Clear();
        selectionController.SelectedObjectIds.Add(picked.GroupId);
        selectionController.ClearVirtualTriangleSelection();
        selectionController.RefreshObjectTree(picked.GroupId);
        _ = renderController.RequestRenderAsync(interactive: true);

        MenuItem extrude = new() { Header = "Extrude Face…" };
        MenuItem inset = new() { Header = "Inset Face…" };
        extrude.Click += async (_, _) => await RunFaceOperationAsync(insetOperation: false);
        inset.Click += async (_, _) => await RunFaceOperationAsync(insetOperation: true);
        ContextMenu menu = new()
        {
            ItemsSource = new object[] { extrude, inset }
        };
        bool enabled = session.CanEditSelectedFace(picked.GroupId);
        extrude.IsEnabled = enabled;
        inset.IsEnabled = enabled;
        menu.Open(viewport);
    }

    private async Task RunFaceOperationAsync(bool insetOperation)
    {
        if (selectionController.ActiveObjectId is not int id || !session.CanEditSelectedFace(id))
            return;

        FaceOperationDialog dialog = insetOperation
            ? new FaceOperationDialog(
                "Inset Face",
                "Inset distance (m)",
                0.05,
                allowNegative: false,
                secondaryLabel: "Signed depth (m) — + inward, - outward, 0 planar",
                secondaryDefaultMeters: 0.02,
                allowSecondaryNegative: true,
                allowSecondaryZero: true,
                showInsetProfile: true)
            : new FaceOperationDialog("Extrude Face", "Signed extrusion distance (m) — + outward, - inward", 0.25, allowNegative: true);
        FaceOperationValues? operation = await dialog.ShowForResultAsync(this);
        if (!operation.HasValue)
            return;

        double amount = operation.Value.AmountMeters;
        double recessDepth = operation.Value.SecondaryMeters;
        ComposerInsetProfile insetProfile = operation.Value.InsetProfile;
        await renderController.StopCurrentRenderAsync();
        SetBusy(true, insetOperation ? "Insetting face…" : "Extruding face…");
        try
        {
            bool changed = await Task.Run(() => insetOperation
                ? session.InsetSelectedFace(id, amount, recessDepth, insetProfile)
                : session.ExtrudeSelectedFace(id, amount), lifetimeCancellation.Token);
            if (!changed)
            {
                statusText.Text = "The selected face could not be edited with that amount.";
                return;
            }

            selectionController.ClearVirtualTriangleSelection();
            selectionController.SelectedObjectIds.Clear();
            selectionController.SelectedObjectIds.Add(id);
            selectionController.ActiveObjectId = id;
            pathText.Text = "Untitled composition (modified)";
            selectionController.RefreshObjectTree(id);
            UpdateHistoryButtons();
            string profileLabel = insetProfile == ComposerInsetProfile.Sloped
                ? "sloped Blender-style profile"
                : "square 90° reveal";
            statusText.Text = insetOperation
                ? recessDepth > 1e-9
                    ? $"Inset face by {amount:0.###} m with {recessDepth:0.###} m inward depth ({profileLabel}). The object is now an editable mesh."
                    : recessDepth < -1e-9
                        ? $"Inset face by {amount:0.###} m with {Math.Abs(recessDepth):0.###} m outward depth ({profileLabel}). The object is now an editable mesh."
                        : $"Inset face by {amount:0.###} m (planar; profile has no effect at zero depth). The object is now an editable mesh."
                : amount > 1e-9
                    ? $"Extruded face outward by {amount:0.###} m. The object is now an editable mesh."
                    : $"Extruded face inward by {Math.Abs(amount):0.###} m. The object is now an editable mesh.";
            await renderController.RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Face edit failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenPrimitiveParameters()
    {
        if (selectionController.ActiveObjectId is not int id)
            return;

        dialogController.OpenPrimitiveParameters(
            id,
            () => selectionModeBox.SelectedIndex = 0,
            message => statusText.Text = message,
            () => pathText.Text = "Untitled composition (modified)",
            () => _ = renderController.RequestRenderAsync(interactive: true),
            () => _ = renderController.RequestRenderAsync(interactive: false),
            selectionController.LoadInspectorFromSelection,
            UpdateHistoryButtons,
            objectId => selectionController.RefreshObjectTree(objectId));
    }

    private void OpenMaterialEditor()
    {
        if (selectionController.ActiveObjectId is not int id)
            return;

        dialogController.OpenMaterialEditor(
            id,
            () => selectionModeBox.SelectedIndex = 0,
            message => statusText.Text = message,
            () => pathText.Text = "Untitled composition (modified)",
            () => _ = renderController.RequestRenderAsync(interactive: false),
            selectionController.LoadInspectorFromSelection,
            UpdateHistoryButtons,
            objectId => selectionController.RefreshObjectTree(objectId));
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
        {
            _ = UndoAsync();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
        {
            _ = RedoAsync();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.D)
        {
            _ = DuplicateSelectedAsync();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.G)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                _ = UngroupSelectedAsync();
            else
                _ = GroupSelectedAsync();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
        {
            _ = SaveSceneAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete)
        {
            _ = DeleteSelectedAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F)
        {
            FrameSelected();
            e.Handled = true;
            return;
        }

        if (e.Source is not TextBox && e.KeyModifiers == KeyModifiers.None)
        {
            ComposerSelectionMode? selectionMode = e.Key switch
            {
                Key.D1 or Key.NumPad1 => ComposerSelectionMode.Vertex,
                Key.D2 or Key.NumPad2 => ComposerSelectionMode.Edge,
                Key.D3 or Key.NumPad3 => ComposerSelectionMode.Face,
                Key.D4 or Key.NumPad4 => ComposerSelectionMode.Object,
                _ => null
            };
            if (selectionMode.HasValue)
            {
                SelectSelectionMode(selectionMode.Value);
                e.Handled = true;
                return;
            }

            if (SelectedSelectionMode != ComposerSelectionMode.Object)
            {
                ComposerGizmoAxis? moveAxis = e.Key switch
                {
                    Key.X => ComposerGizmoAxis.X,
                    Key.Y => ComposerGizmoAxis.Y,
                    Key.Z => ComposerGizmoAxis.Z,
                    Key.A => ComposerGizmoAxis.None,
                    _ => null
                };
                if (moveAxis.HasValue)
                {
                    SelectMoveAxisLock(moveAxis.Value);
                    e.Handled = true;
                    return;
                }
            }

            ComposerGizmoMode? mode = e.Key switch
            {
                Key.G => ComposerGizmoMode.Translate,
                Key.R => ComposerGizmoMode.Rotate,
                Key.S => ComposerGizmoMode.Scale,
                _ => null
            };
            if (mode.HasValue)
            {
                SelectGizmoMode(mode.Value);
                e.Handled = true;
                return;
            }
        }

        if (!session.HasRenderableScene)
            return;

        const double keyStep = 18.0;
        bool pan = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool changed = true;
        switch (e.Key)
        {
            case Key.Left:
                if (pan) session.Camera.Pan(-keyStep, 0, viewport.Bounds.Height);
                else session.Camera.Orbit(-keyStep, 0);
                break;
            case Key.Right:
                if (pan) session.Camera.Pan(keyStep, 0, viewport.Bounds.Height);
                else session.Camera.Orbit(keyStep, 0);
                break;
            case Key.Up:
                if (pan) session.Camera.Pan(0, -keyStep, viewport.Bounds.Height);
                else session.Camera.Orbit(0, -keyStep);
                break;
            case Key.Down:
                if (pan) session.Camera.Pan(0, keyStep, viewport.Bounds.Height);
                else session.Camera.Orbit(0, keyStep);
                break;
            case Key.Add:
            case Key.OemPlus: session.Camera.Zoom(1); break;
            case Key.Subtract:
            case Key.OemMinus: session.Camera.Zoom(-1); break;
            default: changed = false; break;
        }

        if (changed)
        {
            _ = renderController.RequestRenderAsync(interactive: false);
            e.Handled = true;
        }
    }

    private void SelectSelectionMode(ComposerSelectionMode mode)
    {
        int index = Array.FindIndex(selectionModeChoices, choice => choice.Mode == mode);
        if (index >= 0)
            selectionModeBox.SelectedIndex = index;
    }

    private void SelectMoveAxisLock(ComposerGizmoAxis axis)
    {
        int index = Array.FindIndex(moveAxisChoices, choice => choice.Axis == axis);
        if (index >= 0)
            moveAxisBox.SelectedIndex = index;
    }

    private void SelectGizmoMode(ComposerGizmoMode mode)
    {
        if (SelectedSelectionMode != ComposerSelectionMode.Object && mode != ComposerGizmoMode.Translate)
        {
            statusText.Text = "Vertex, edge, and face editing currently support move only.";
            mode = ComposerGizmoMode.Translate;
        }

        int index = Array.FindIndex(gizmoModeChoices, choice => choice.Mode == mode);
        if (index >= 0)
            gizmoModeBox.SelectedIndex = index;
    }



    private void UpdateHistoryButtons()
    {
        undoButton.IsEnabled = session.CanUndo;
        redoButton.IsEnabled = session.CanRedo;
        undoButton.Content = session.UndoDescription is string undoDescription
            ? $"Undo {undoDescription}"
            : "Undo";
        redoButton.Content = session.RedoDescription is string redoDescription
            ? $"Redo {redoDescription}"
            : "Redo";

        menuController.UpdateHistory(
            undoButton.Content?.ToString() ?? "Undo",
            undoButton.IsEnabled,
            redoButton.Content?.ToString() ?? "Redo",
            redoButton.IsEnabled);
    }


    private void SetBusy(bool busy, string? message = null)
    {
        newButton.IsEnabled = !busy;
        openButton.IsEnabled = !busy;
        insertButton.IsEnabled = !busy;
        addPrimitiveButton.IsEnabled = !busy;
        primitiveBox.IsEnabled = !busy;
        saveButton.IsEnabled = !busy;
        exportButton.IsEnabled = !busy;
        rendererBox.IsEnabled = !busy;
        renderSettingsButton.IsEnabled = !busy;
        selectionModeBox.IsEnabled = !busy;
        gizmoModeBox.IsEnabled = !busy && SelectedSelectionMode == ComposerSelectionMode.Object;
        moveAxisBox.IsEnabled = !busy && SelectedSelectionMode != ComposerSelectionMode.Object;
        objectTree.IsEnabled = !busy;
        if (busy)
            parametersButton.IsEnabled = false;
        if (selectionController.ActiveObjectId.HasValue)
            selectionController.SetInspectorEnabled(!busy);
        if (busy)
        {
            undoButton.IsEnabled = false;
            redoButton.IsEnabled = false;
        }
        else
        {
            UpdateHistoryButtons();
        }
        if (!string.IsNullOrWhiteSpace(message))
            statusText.Text = message;
        menuController.SyncEnabledState();
    }

    private void ReportOperationFailure(string operation, Exception exception)
    {
        string message = $"{operation}: {exception.Message}";
        statusText.Text = message;
        Trace.WriteLine($"{message}{Environment.NewLine}{exception}");
        Console.Error.WriteLine($"{message}{Environment.NewLine}{exception}");
    }

    private void DisposeWindowResources()
    {
        hoverPulseTimer.Stop();
        dialogController.Dispose();
        navigationController.Dispose();
        lifetimeCancellation.Cancel();
        renderController.Dispose();
        session.Dispose();
        lifetimeCancellation.Dispose();
    }

    private static Button NewButton(string content) => new() { Content = content, MinHeight = 32 };
    private static TextBox NewTextBox() => new() { MinHeight = 30 };
    private static TextBox NumberBox(string value) => new()
    {
        Text = value,
        TextAlignment = TextAlignment.Right,
        MinWidth = 65
    };

}
