using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class ComposerWindow : Window
{
    private sealed record RendererChoice(ComposerRendererKind Kind, string Label, string Description)
    {
        public override string ToString() => Label;
    }

    private sealed class ObjectTreeNode
    {
        public ObjectTreeNode(int id, string label)
        {
            Id = id;
            Label = label;
        }

        public int Id { get; }
        public string Label { get; }
        public List<ObjectTreeNode> Children { get; } = new();
        public override string ToString() => Label;
    }

    private enum ViewportDragMode
    {
        None,
        Orbit,
        Pan
    }

    private readonly RendererChoice[] rendererChoices =
    [
        new(ComposerRendererKind.Raster, "Raster", "Software z-buffer rasterizer; best for responsive composition."),
        new(ComposerRendererKind.VulkanRaster, "Vulkan raster", "Hardware rasterizer using the complete scene."),
        new(ComposerRendererKind.VulkanCompute, "Vulkan compute", "Compute ray preview using the complete scene."),
        new(ComposerRendererKind.Cpu, "CPU", "CPU ray preview; renders after camera movement finishes.")
    ];

    private readonly ComposerSceneSession session = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly Dictionary<ComposerRendererKind, double> lastFrameTimes = new();

    private readonly TextBlock pathText;
    private readonly TextBlock statusText;
    private readonly TextBlock detailsText;
    private readonly ComboBox rendererBox;
    private readonly TreeView objectTree;
    private readonly Border viewport;
    private readonly Image image;
    private readonly Button newButton;
    private readonly Button openButton;
    private readonly Button insertButton;
    private readonly Button saveButton;
    private readonly Button duplicateButton;
    private readonly Button deleteButton;
    private readonly Button gridButton;
    private readonly Button applyButton;
    private readonly Button frameButton;
    private readonly Button resetTransformButton;
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
    private readonly TextBox copyCountBox;
    private readonly TextBox spacingBox;

    private WriteableBitmap? bitmap;
    private int? selectedObjectId;
    private bool refreshingObjectTree;
    private ViewportDragMode viewportDragMode;
    private bool leftPressed;
    private Point previousPointer;
    private Point leftPressPoint;
    private bool rendering;
    private bool renderAgain;
    private bool pendingInteractive;
    private long renderVersion;
    private int lastRenderWidth = 1;
    private int lastRenderHeight = 1;
    private CancellationTokenSource? activeRenderCancellation;
    private CancellationTokenSource? resizeDebounceCancellation;

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
        saveButton = NewButton("Save scene…");
        duplicateButton = NewButton("Duplicate");
        deleteButton = NewButton("Delete");
        gridButton = NewButton("Generate grid");
        applyButton = NewButton("Apply transform");
        frameButton = NewButton("Frame selected");
        resetTransformButton = NewButton("Reset transform");

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

        objectTree = new TreeView
        {
            SelectionMode = SelectionMode.Single,
            AutoScrollToSelectedItem = true,
            ItemTemplate = new FuncTreeDataTemplate<ObjectTreeNode>(
                (node, _) => new TextBlock
                {
                    Text = node.Label,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                node => node.Children)
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
        copyCountBox = NumberBox("100");
        spacingBox = NumberBox("2.5");

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

        Content = BuildLayout();
        WireEvents();
        RefreshObjectTree();
        SetInspectorEnabled(false);

        Opened += async (_, _) =>
        {
            string? startupPath = startupArguments.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(startupPath))
                await LoadSceneAsync(startupPath);
        };
        Closed += (_, _) => DisposeWindowResources();
    }

    private Control BuildLayout()
    {
        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        Grid toolbar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(10)
        };
        toolbar.Children.Add(newButton);
        Grid.SetColumn(newButton, 0);
        toolbar.Children.Add(openButton);
        Grid.SetColumn(openButton, 1);
        toolbar.Children.Add(insertButton);
        Grid.SetColumn(insertButton, 2);
        toolbar.Children.Add(saveButton);
        Grid.SetColumn(saveButton, 3);
        toolbar.Children.Add(pathText);
        Grid.SetColumn(pathText, 4);
        toolbar.Children.Add(rendererBox);
        Grid.SetColumn(rendererBox, 5);
        root.Children.Add(toolbar);

        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("280,5,*,5,310"),
            Margin = new Thickness(10, 0, 10, 8)
        };
        Control scenePanel = BuildScenePanel();
        content.Children.Add(scenePanel);
        Grid.SetColumn(scenePanel, 0);

        GridSplitter leftSplitter = new() { Width = 5, ResizeDirection = GridResizeDirection.Columns };
        content.Children.Add(leftSplitter);
        Grid.SetColumn(leftSplitter, 1);

        content.Children.Add(viewport);
        Grid.SetColumn(viewport, 2);

        GridSplitter rightSplitter = new() { Width = 5, ResizeDirection = GridResizeDirection.Columns };
        content.Children.Add(rightSplitter);
        Grid.SetColumn(rightSplitter, 3);

        Control inspector = BuildInspectorPanel();
        content.Children.Add(inspector);
        Grid.SetColumn(inspector, 4);

        root.Children.Add(content);
        Grid.SetRow(content, 1);

        Border statusBar = new()
        {
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { statusText, detailsText }
            }
        };
        root.Children.Add(statusBar);
        Grid.SetRow(statusBar, 2);
        return root;
    }

    private Control BuildScenePanel()
    {
        Grid panel = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            RowSpacing = 8
        };

        TextBlock heading = Heading("Scene objects");
        panel.Children.Add(heading);
        panel.Children.Add(objectTree);
        Grid.SetRow(objectTree, 1);

        Grid objectButtons = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };
        objectButtons.Children.Add(duplicateButton);
        objectButtons.Children.Add(deleteButton);
        Grid.SetColumn(deleteButton, 1);
        panel.Children.Add(objectButtons);
        Grid.SetRow(objectButtons, 2);

        Border stressPanel = new()
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    Heading("Performance stress grid"),
                    LabeledControl("Copies", copyCountBox),
                    LabeledControl("Spacing", spacingBox),
                    gridButton,
                    new TextBlock
                    {
                        Text = "Copies share material and triangle objects where the core allows, then render as independent transforms.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.68,
                        FontSize = 12
                    }
                }
            }
        };
        panel.Children.Add(stressPanel);
        Grid.SetRow(stressPanel, 3);
        return panel;
    }

    private Control BuildInspectorPanel()
    {
        StackPanel stack = new() { Spacing = 9, Margin = new Thickness(8, 0, 0, 0) };
        stack.Children.Add(Heading("Inspector"));
        stack.Children.Add(LabeledControl("Name", nameBox));
        stack.Children.Add(visibleBox);
        stack.Children.Add(Heading("Position"));
        stack.Children.Add(VectorRow(positionX, positionY, positionZ));
        stack.Children.Add(Heading("Rotation (degrees)"));
        stack.Children.Add(VectorRow(rotationX, rotationY, rotationZ));
        stack.Children.Add(Heading("Scale"));
        stack.Children.Add(VectorRow(scaleX, scaleY, scaleZ));
        stack.Children.Add(applyButton);
        stack.Children.Add(frameButton);
        stack.Children.Add(resetTransformButton);
        stack.Children.Add(new TextBlock
        {
            Text = "Viewport: left click selects; right drag orbits; middle drag or Shift+right drag pans; wheel zooms. Delete removes, Ctrl+D duplicates, and F frames the selection.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0)
        });

        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    private void WireEvents()
    {
        newButton.Click += async (_, _) => await NewSceneAsync();
        openButton.Click += async (_, _) => await BrowseAndOpenAsync();
        insertButton.Click += async (_, _) => await BrowseAndInsertAsync();
        saveButton.Click += async (_, _) => await SaveSceneAsync();
        duplicateButton.Click += async (_, _) => await DuplicateSelectedAsync();
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        gridButton.Click += async (_, _) => await GenerateGridAsync();
        applyButton.Click += async (_, _) => await ApplyInspectorAsync();
        frameButton.Click += (_, _) => FrameSelected();
        resetTransformButton.Click += async (_, _) => await ResetSelectedTransformAsync();

        objectTree.SelectionChanged += (_, _) =>
        {
            if (refreshingObjectTree)
                return;
            selectedObjectId = (objectTree.SelectedItem as ObjectTreeNode)?.Id;
            LoadInspectorFromSelection();
            _ = UpdateSelectionPreviewAsync();
        };

        rendererBox.SelectionChanged += (_, _) =>
        {
            detailsText.Text = SelectedRenderer.Description;
            _ = RequestRenderAsync(interactive: false);
        };

        viewport.PointerPressed += OnViewportPointerPressed;
        viewport.PointerMoved += OnViewportPointerMoved;
        viewport.PointerReleased += OnViewportPointerReleased;
        viewport.PointerCaptureLost += (_, _) =>
        {
            viewportDragMode = ViewportDragMode.None;
            leftPressed = false;
        };
        viewport.PointerWheelChanged += (_, e) =>
        {
            if (!session.HasRenderableScene) return;
            session.Camera.Zoom(e.Delta.Y);
            _ = RequestRenderAsync(interactive: false);
            e.Handled = true;
        };
        viewport.SizeChanged += (_, _) => ScheduleResizeRender();
        foreach (TextBox box in TransformTextBoxes())
            box.KeyDown += OnTransformBoxKeyDown;
        KeyDown += OnWindowKeyDown;
    }

    private RendererChoice SelectedRenderer => rendererBox.SelectedItem as RendererChoice ?? rendererChoices[0];

    private async Task NewSceneAsync()
    {
        CancelCurrentRender();
        SetBusy(true, "Creating a new composition…");
        try
        {
            await Task.Run(() => session.NewScene(lifetimeCancellation.Token), lifetimeCancellation.Token);
            bitmap?.Dispose();
            bitmap = null;
            image.Source = null;
            pathText.Text = "Untitled composition";
            selectedObjectId = null;
            RefreshObjectTree();
            SetInspectorEnabled(false);
            statusText.Text = "New empty composition. Insert a model to begin.";
            detailsText.Text = SelectedRenderer.Description;
        }
        catch (Exception ex)
        {
            statusText.Text = $"Could not create scene: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task BrowseAndOpenAsync()
    {
        string? path = await PickOpenPathAsync("Open scene or model", ComposerFileTypes.OpenPickerTypes);
        if (!string.IsNullOrWhiteSpace(path))
            await LoadSceneAsync(path);
    }

    private async Task BrowseAndInsertAsync()
    {
        string? path = await PickOpenPathAsync("Insert 3D model", ComposerFileTypes.InsertPickerTypes);
        if (!string.IsNullOrWhiteSpace(path))
            await InsertModelAsync(path);
    }

    private async Task<string?> PickOpenPathAsync(string title, IReadOnlyList<FilePickerFileType> types)
    {
        if (!StorageProvider.CanOpen)
        {
            statusText.Text = "The desktop file picker is unavailable.";
            return null;
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = types
            });
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        }
        catch (Exception ex)
        {
            statusText.Text = $"File picker failed: {ex.Message}";
            return null;
        }
    }

    private async Task LoadSceneAsync(string path)
    {
        CancelCurrentRender();
        SetBusy(true, $"Loading {Path.GetFileName(path)}…");
        try
        {
            await Task.Run(() => session.Load(path, lifetimeCancellation.Token), lifetimeCancellation.Token);
            pathText.Text = session.ScenePath ?? Path.GetFileName(path);
            selectedObjectId = null;
            RefreshObjectTree();
            statusText.Text = $"Loaded {Path.GetFileName(path)} — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles.";
            await RequestRenderAsync(interactive: false);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            statusText.Text = $"Load failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task InsertModelAsync(string path)
    {
        CancelCurrentRender();
        SetBusy(true, $"Inserting {Path.GetFileName(path)}…");
        try
        {
            int insertedId = await Task.Run(
                () => session.Insert(path, lifetimeCancellation.Token),
                lifetimeCancellation.Token);
            pathText.Text = "Untitled composition (modified)";
            selectedObjectId = insertedId;
            RefreshObjectTree(insertedId);
            statusText.Text = $"Inserted {Path.GetFileName(path)} — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles.";
            await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Insert failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveSceneAsync()
    {
        if (!StorageProvider.CanSave)
        {
            statusText.Text = "The desktop save picker is unavailable.";
            return;
        }

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save composition",
                SuggestedFileName = session.ScenePath is null
                    ? "composition.lscene"
                    : Path.GetFileNameWithoutExtension(session.ScenePath) + ".lscene",
                DefaultExtension = "lscene",
                FileTypeChoices = [ComposerFileTypes.ComposerScene]
            });
            string? path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            SetBusy(true, "Saving composition…");
            await Task.Run(() => session.Save(path, lifetimeCancellation.Token), lifetimeCancellation.Token);
            pathText.Text = session.ScenePath ?? path;
            statusText.Text = $"Saved {Path.GetFileName(session.ScenePath ?? path)}.";
        }
        catch (Exception ex)
        {
            statusText.Text = $"Save failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateSelectionPreviewAsync()
    {
        if (lifetimeCancellation.IsCancellationRequested)
            return;

        try
        {
            int? requestedSelection = selectedObjectId;
            CancelCurrentRender();
            bool changed = await Task.Run(
                () => session.SetSelectedObject(requestedSelection),
                lifetimeCancellation.Token);
            if (requestedSelection != selectedObjectId)
                return;
            if (changed && session.HasRenderableScene)
                await RequestRenderAsync(interactive: false);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            statusText.Text = $"Selection update failed: {ex.Message}";
        }
    }

    private IEnumerable<TextBox> TransformTextBoxes()
    {
        yield return positionX;
        yield return positionY;
        yield return positionZ;
        yield return rotationX;
        yield return rotationY;
        yield return rotationZ;
        yield return scaleX;
        yield return scaleY;
        yield return scaleZ;
    }

    private void OnTransformBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _ = ApplyInspectorAsync();
        e.Handled = true;
    }

    private async Task ApplyInspectorAsync()
    {
        if (selectedObjectId is not int id)
            return;

        try
        {
            Vec3 position = ReadVector(positionX, positionY, positionZ, "Position");
            Vec3 rotationDegrees = ReadVector(rotationX, rotationY, rotationZ, "Rotation");
            Vec3 scale = ReadVector(scaleX, scaleY, scaleZ, "Scale");
            Vec3 rotationRadians = rotationDegrees * (Math.PI / 180.0);

            CancelCurrentRender();
            SetBusy(true, "Applying transform to the selected group…");
            bool updated = await Task.Run(() => session.UpdateObject(
                id,
                nameBox.Text ?? string.Empty,
                visibleBox.IsChecked ?? true,
                position,
                rotationRadians,
                scale), lifetimeCancellation.Token);
            if (!updated)
                throw new InvalidOperationException("The selected scene node no longer exists.");

            pathText.Text = "Untitled composition (modified)";
            RefreshObjectTree(id);
            statusText.Text = "Selected group transform updated.";
            await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Transform update failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ResetSelectedTransformAsync()
    {
        if (selectedObjectId is not int id)
            return;

        CancelCurrentRender();
        SetBusy(true, "Resetting the selected group transform…");
        try
        {
            bool reset = await Task.Run(
                () => session.ResetObjectTransform(id),
                lifetimeCancellation.Token);
            if (!reset)
                throw new InvalidOperationException("The selected scene node no longer exists.");

            RefreshObjectTree(id);
            LoadInspectorFromSelection();
            pathText.Text = "Untitled composition (modified)";
            statusText.Text = "Selected group transform reset.";
            await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Transform reset failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DuplicateSelectedAsync()
    {
        if (selectedObjectId is not int id)
            return;

        CancelCurrentRender();
        SetBusy(true, "Duplicating object…");
        try
        {
            int? duplicateId = await Task.Run(() => session.DuplicateObject(id), lifetimeCancellation.Token);
            pathText.Text = "Untitled composition (modified)";
            RefreshObjectTree(duplicateId);
            statusText.Text = $"Duplicated object — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles.";
            await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Duplicate failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (selectedObjectId is not int id)
            return;

        CancelCurrentRender();
        await Task.Run(() => session.DeleteObject(id));
        selectedObjectId = null;
        pathText.Text = "Untitled composition (modified)";
        RefreshObjectTree();
        statusText.Text = $"Deleted object — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles.";
        if (session.HasRenderableScene)
            await RequestRenderAsync(interactive: false);
        else
        {
            image.Source = null;
            SetInspectorEnabled(false);
        }
    }

    private async Task GenerateGridAsync()
    {
        if (selectedObjectId is not int id)
        {
            statusText.Text = "Select an object before generating copies.";
            return;
        }

        try
        {
            int copyCount = ParsePositiveInt(copyCountBox.Text, "Copies");
            double spacing = ParsePositiveDouble(spacingBox.Text, "Spacing");
            CancelCurrentRender();
            SetBusy(true, $"Generating {copyCount:N0} copies…");
            await Task.Run(
                () => session.GenerateGridCopies(id, copyCount, spacing, lifetimeCancellation.Token),
                lifetimeCancellation.Token);
            pathText.Text = "Untitled composition (modified)";
            RefreshObjectTree(id);
            statusText.Text = $"Generated {copyCount:N0} copies — {session.ObjectCount:N0} objects, {session.TriangleCount:N0} triangles.";
            await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Grid generation failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void FrameSelected()
    {
        if (selectedObjectId is not int id || !session.FrameObject(id))
            return;
        _ = RequestRenderAsync(interactive: false);
    }

    private void RefreshObjectTree(int? preferredSelection = null)
    {
        refreshingObjectTree = true;
        try
        {
            IReadOnlyList<SceneObjectInfo> infos = session.GetObjectInfos();
            List<ObjectTreeNode> roots = BuildObjectTree(infos);
            objectTree.ItemsSource = roots;

            int? target = preferredSelection ?? selectedObjectId;
            ObjectTreeNode? item = target.HasValue ? FindTreeNode(roots, target.Value) : null;
            objectTree.SelectedItem = item;
            selectedObjectId = item?.Id;
        }
        finally
        {
            refreshingObjectTree = false;
        }

        session.SetSelectedObject(selectedObjectId);
        LoadInspectorFromSelection();
    }

    private static List<ObjectTreeNode> BuildObjectTree(IReadOnlyList<SceneObjectInfo> infos)
    {
        List<ObjectTreeNode> roots = new();
        List<ObjectTreeNode> ancestors = new();

        foreach (SceneObjectInfo info in infos)
        {
            ObjectTreeNode node = new(
                info.Id,
                $"{(info.Visible ? "●" : "○")} {info.Name}  [{info.TriangleCount:N0}]");

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

    private static ObjectTreeNode? FindTreeNode(IEnumerable<ObjectTreeNode> nodes, int id)
    {
        foreach (ObjectTreeNode node in nodes)
        {
            if (node.Id == id)
                return node;
            ObjectTreeNode? child = FindTreeNode(node.Children, id);
            if (child != null)
                return child;
        }
        return null;
    }

    private void LoadInspectorFromSelection()
    {
        if (selectedObjectId is not int id || session.GetObjectState(id) is not ComposerObjectState state)
        {
            SetInspectorEnabled(false);
            return;
        }

        SetInspectorEnabled(true);
        nameBox.Text = state.Name;
        visibleBox.IsChecked = state.Visible;
        WriteVector(state.Position, positionX, positionY, positionZ);
        WriteVector(state.Rotation * (180.0 / Math.PI), rotationX, rotationY, rotationZ);
        WriteVector(state.Scale, scaleX, scaleY, scaleZ);
    }

    private void SetInspectorEnabled(bool enabled)
    {
        nameBox.IsEnabled = enabled;
        visibleBox.IsEnabled = enabled;
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
        duplicateButton.IsEnabled = enabled;
        deleteButton.IsEnabled = enabled;
        gridButton.IsEnabled = enabled;
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!session.HasRenderableScene)
            return;

        PointerPoint point = e.GetCurrentPoint(viewport);
        Point position = e.GetPosition(viewport);
        viewport.Focus();

        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsRightButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            viewportDragMode = ViewportDragMode.Pan;
            previousPointer = position;
            e.Pointer.Capture(viewport);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            viewportDragMode = ViewportDragMode.Orbit;
            previousPointer = position;
            e.Pointer.Capture(viewport);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            leftPressed = true;
            leftPressPoint = position;
            e.Pointer.Capture(viewport);
            e.Handled = true;
        }
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (viewportDragMode == ViewportDragMode.None)
            return;

        Point current = e.GetPosition(viewport);
        Vector delta = current - previousPointer;
        previousPointer = current;

        if (viewportDragMode == ViewportDragMode.Orbit)
            session.Camera.Orbit(delta.X, delta.Y);
        else
            session.Camera.Pan(delta.X, delta.Y, viewport.Bounds.Height);

        if (CanRenderContinuously(SelectedRenderer.Kind))
            _ = RequestRenderAsync(interactive: true);
        else
            statusText.Text = $"{SelectedRenderer.Label}: release the mouse to render the new view.";

        e.Handled = true;
    }

    private async void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        Point releasePoint = e.GetPosition(viewport);
        if (viewportDragMode != ViewportDragMode.None)
        {
            viewportDragMode = ViewportDragMode.None;
            e.Pointer.Capture(null);
            await RequestRenderAsync(interactive: false);
            e.Handled = true;
            return;
        }

        if (leftPressed)
        {
            leftPressed = false;
            e.Pointer.Capture(null);
            Vector movement = releasePoint - leftPressPoint;
            if (movement.Length <= 5.0 && viewport.Bounds.Width > 0 && viewport.Bounds.Height > 0)
            {
                double normalizedX = releasePoint.X / viewport.Bounds.Width;
                double normalizedY = releasePoint.Y / viewport.Bounds.Height;
                CameraDefinition camera = session.Camera.Snapshot();
                int? hitId = await Task.Run(() => session.PickObject(
                    camera,
                    normalizedX,
                    normalizedY,
                    lastRenderWidth,
                    lastRenderHeight));
                if (hitId.HasValue)
                    SelectObject(hitId.Value);
            }
            e.Handled = true;
        }
    }

    private void SelectObject(int id)
    {
        if (objectTree.ItemsSource is not IEnumerable<ObjectTreeNode> roots)
            return;
        ObjectTreeNode? item = FindTreeNode(roots, id);
        if (item == null)
            return;
        objectTree.SelectedItem = item;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.D)
        {
            _ = DuplicateSelectedAsync();
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
            _ = RequestRenderAsync(interactive: false);
            e.Handled = true;
        }
    }

    private bool CanRenderContinuously(ComposerRendererKind renderer)
    {
        if (renderer == ComposerRendererKind.Raster)
            return true;
        if (renderer == ComposerRendererKind.Cpu)
            return false;
        if (!lastFrameTimes.TryGetValue(renderer, out double milliseconds))
            return false;
        return milliseconds <= (renderer == ComposerRendererKind.VulkanRaster ? 160.0 : 220.0);
    }

    private async Task RequestRenderAsync(bool interactive)
    {
        if (!session.HasRenderableScene || lifetimeCancellation.IsCancellationRequested)
            return;

        pendingInteractive = interactive;
        renderVersion++;
        if (!interactive)
            activeRenderCancellation?.Cancel();

        if (rendering)
        {
            renderAgain = true;
            return;
        }

        rendering = true;
        try
        {
            do
            {
                renderAgain = false;
                bool thisInteractive = pendingInteractive;
                pendingInteractive = false;
                long thisVersion = renderVersion;

                using CancellationTokenSource frameCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
                activeRenderCancellation = frameCancellation;
                try
                {
                    await RenderOneFrameAsync(thisInteractive, thisVersion, frameCancellation.Token);
                }
                finally
                {
                    if (ReferenceEquals(activeRenderCancellation, frameCancellation))
                        activeRenderCancellation = null;
                }
            }
            while (renderAgain && !lifetimeCancellation.IsCancellationRequested);
        }
        finally
        {
            rendering = false;
        }
    }

    private async Task RenderOneFrameAsync(bool interactive, long requestVersion, CancellationToken token)
    {
        RendererChoice renderer = SelectedRenderer;
        (int width, int height) = ChooseRenderSize(renderer.Kind, interactive);
        CameraDefinition camera = session.Camera.Snapshot();
        RenderOptions.SetBitmapInterpolationMode(
            image,
            interactive ? BitmapInterpolationMode.LowQuality : BitmapInterpolationMode.HighQuality);

        if (!interactive)
            statusText.Text = $"Rendering {renderer.Label} at {width}x{height}…";

        try
        {
            ComposerFrame frame = await Task.Run(
                () => session.Render(renderer.Kind, camera, width, height, interactive, token),
                token);

            if (token.IsCancellationRequested || (!interactive && requestVersion != renderVersion))
                return;

            lastRenderWidth = frame.Image.Width;
            lastRenderHeight = frame.Image.Height;
            lastFrameTimes[renderer.Kind] = lastFrameTimes.TryGetValue(renderer.Kind, out double previous)
                ? previous * 0.70 + frame.ElapsedMilliseconds * 0.30
                : frame.ElapsedMilliseconds;

            ShowImage(frame.Image);
            double fps = frame.ElapsedMilliseconds > 0.001 ? 1000.0 / frame.ElapsedMilliseconds : 0.0;
            long workingSet = Process.GetCurrentProcess().WorkingSet64;
            statusText.Text = $"{renderer.Label}: {frame.ElapsedMilliseconds:0.0} ms ({fps:0.0} FPS) | " +
                              $"{session.ObjectCount:N0} objects | {session.TriangleCount:N0} triangles | " +
                              $"{FormatBytes(workingSet)} process memory";
            detailsText.Text = frame.Details;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestVersion == renderVersion && !lifetimeCancellation.IsCancellationRequested)
                statusText.Text = $"{renderer.Label} failed: {ex.Message}";
        }
    }

    private (int Width, int Height) ChooseRenderSize(ComposerRendererKind renderer, bool interactive)
    {
        double renderScaling = Math.Clamp(RenderScaling, 1.0, 4.0);
        double viewWidth = Math.Max(320.0, viewport.Bounds.Width * renderScaling);
        double viewHeight = Math.Max(180.0, viewport.Bounds.Height * renderScaling);

        int maxWidth = renderer switch
        {
            ComposerRendererKind.Cpu => 640,
            ComposerRendererKind.VulkanCompute when interactive => 640,
            ComposerRendererKind.VulkanCompute => 960,
            _ when interactive => 960,
            _ => 1280
        };
        int maxHeight = renderer switch
        {
            ComposerRendererKind.Cpu => 360,
            ComposerRendererKind.VulkanCompute when interactive => 360,
            ComposerRendererKind.VulkanCompute => 540,
            _ when interactive => 540,
            _ => 720
        };

        double scale = Math.Min(maxWidth / viewWidth, maxHeight / viewHeight);
        scale = Math.Min(1.0, scale);
        int width = AlignToEight(Math.Max(160, (int)Math.Round(viewWidth * scale)));
        int height = AlignToEight(Math.Max(96, (int)Math.Round(viewHeight * scale)));
        return (width, height);
    }

    private unsafe void ShowImage(RenderImage rendered)
    {
        bool sizeChanged = bitmap == null ||
                           bitmap.PixelSize.Width != rendered.Width ||
                           bitmap.PixelSize.Height != rendered.Height;
        if (sizeChanged)
        {
            WriteableBitmap next = new(
                new PixelSize(rendered.Width, rendered.Height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);
            WriteableBitmap? old = bitmap;
            bitmap = next;
            image.Source = next;
            old?.Dispose();
        }

        using ILockedFramebuffer framebuffer = bitmap!.Lock();
        fixed (uint* sourceBase = rendered.PackedRgba32)
        {
            long sourceRowBytes = checked((long)rendered.Width * sizeof(uint));
            for (int y = 0; y < rendered.Height; y++)
            {
                byte* source = (byte*)(sourceBase + y * rendered.Width);
                byte* destination = (byte*)framebuffer.Address + y * framebuffer.RowBytes;
                Buffer.MemoryCopy(source, destination, framebuffer.RowBytes, sourceRowBytes);
            }
        }
        image.InvalidateVisual();
    }

    private void ScheduleResizeRender()
    {
        if (!session.HasRenderableScene || lifetimeCancellation.IsCancellationRequested)
            return;

        resizeDebounceCancellation?.Cancel();
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        resizeDebounceCancellation = cancellation;
        _ = RenderAfterResizeDelayAsync(cancellation);
    }

    private async Task RenderAfterResizeDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(140, cancellation.Token);
            await RequestRenderAsync(interactive: false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(resizeDebounceCancellation, cancellation))
                resizeDebounceCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelCurrentRender()
    {
        renderVersion++;
        activeRenderCancellation?.Cancel();
    }

    private void SetBusy(bool busy, string? message = null)
    {
        newButton.IsEnabled = !busy;
        openButton.IsEnabled = !busy;
        insertButton.IsEnabled = !busy;
        saveButton.IsEnabled = !busy;
        rendererBox.IsEnabled = !busy;
        objectTree.IsEnabled = !busy;
        if (selectedObjectId.HasValue)
            SetInspectorEnabled(!busy);
        if (!string.IsNullOrWhiteSpace(message))
            statusText.Text = message;
    }

    private void DisposeWindowResources()
    {
        lifetimeCancellation.Cancel();
        activeRenderCancellation?.Cancel();
        resizeDebounceCancellation?.Cancel();
        bitmap?.Dispose();
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
    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        FontSize = 15
    };

    private static Control LabeledControl(string label, Control control)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8
        };
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        return grid;
    }

    private static Control VectorRow(TextBox x, TextBox y, TextBox z)
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"),
            ColumnSpacing = 5
        };
        Add("X", x, 0);
        Add("Y", y, 2);
        Add("Z", z, 4);
        return grid;

        void Add(string label, TextBox box, int column)
        {
            TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(text);
            Grid.SetColumn(text, column);
            grid.Children.Add(box);
            Grid.SetColumn(box, column + 1);
        }
    }

    private static void WriteVector(Vec3 value, TextBox x, TextBox y, TextBox z)
    {
        x.Text = value.X.ToString("0.######", CultureInfo.InvariantCulture);
        y.Text = value.Y.ToString("0.######", CultureInfo.InvariantCulture);
        z.Text = value.Z.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static Vec3 ReadVector(TextBox x, TextBox y, TextBox z, string label) => new(
        ParseDouble(x.Text, $"{label} X"),
        ParseDouble(y.Text, $"{label} Y"),
        ParseDouble(z.Text, $"{label} Z"));

    private static double ParseDouble(string? text, string label)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant) && double.IsFinite(invariant))
            return invariant;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current) && double.IsFinite(current))
            return current;
        throw new FormatException($"{label} must be a finite number.");
    }

    private static double ParsePositiveDouble(string? text, string label)
    {
        double value = ParseDouble(text, label);
        if (value <= 0.0)
            throw new ArgumentOutOfRangeException(label, $"{label} must be greater than zero.");
        return value;
    }

    private static int ParsePositiveInt(string? text, string label)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 1)
            throw new FormatException($"{label} must be a positive integer.");
        return value;
    }

    private static int AlignToEight(int value) => Math.Max(8, (value + 7) & ~7);

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int suffix = 0;
        while (value >= 1024.0 && suffix < suffixes.Length - 1)
        {
            value /= 1024.0;
            suffix++;
        }
        return $"{value:0.0} {suffixes[suffix]}";
    }
}
