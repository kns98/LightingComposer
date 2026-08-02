using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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

    private enum ViewportDragMode
    {
        None,
        Orbit,
        Pan
    }

    private sealed record GizmoDragState(
        int SelectedId,
        ComposerGizmoAxis Axis,
        Point StartImagePoint,
        Vec3 StartPosition,
        Vec3 Rotation,
        Vec3 Scale,
        double ScreenDirectionX,
        double ScreenDirectionY,
        double WorldUnitsPerPixel);

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
    private readonly ScrollViewer objectTree;
    private readonly StackPanel objectTreePanel;
    private readonly HashSet<int> expandedObjectIds = new();
    private readonly Dictionary<int, int> trianglePageOffsets = new();
    private const int TrianglePageSize = 200;
    private bool treeExpansionInitialized;
    private readonly Border viewport;
    private readonly Image image;
    private readonly Button newButton;
    private readonly Button openButton;
    private readonly Button insertButton;
    private readonly Button saveButton;
    private readonly Button exportButton;
    private readonly Button undoButton;
    private readonly Button redoButton;
    private readonly Button duplicateButton;
    private readonly Button ungroupButton;
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
    private int? selectedTriangleGroupId;
    private int? selectedTriangleIndex;
    private ViewportDragMode viewportDragMode;
    private bool leftPressed;
    private GizmoDragState? gizmoDrag;
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
        exportButton = NewButton("Export package…");
        undoButton = NewButton("Undo");
        redoButton = NewButton("Redo");
        duplicateButton = NewButton("Duplicate");
        ungroupButton = NewButton("Ungroup");
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
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto"),
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
        toolbar.Children.Add(undoButton);
        Grid.SetColumn(undoButton, 4);
        toolbar.Children.Add(redoButton);
        Grid.SetColumn(redoButton, 5);
        toolbar.Children.Add(pathText);
        Grid.SetColumn(pathText, 6);
        toolbar.Children.Add(exportButton);
        Grid.SetColumn(exportButton, 7);
        toolbar.Children.Add(rendererBox);
        Grid.SetColumn(rendererBox, 8);
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
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };
        objectButtons.Children.Add(duplicateButton);
        objectButtons.Children.Add(ungroupButton);
        Grid.SetColumn(ungroupButton, 1);
        objectButtons.Children.Add(deleteButton);
        Grid.SetColumn(deleteButton, 2);
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
            Text = "Hierarchy: ▸/▾ expands group nodes. A … row lazily reveals triangle details in pages, so large meshes do not flood the tree. Ungroup promotes children or splits a mesh further. Apply bakes transforms into vertices; Ctrl+Z/Ctrl+Y undo and redo. Viewport: right drag orbits, middle drag pans, and wheel zooms.",
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
        exportButton.Click += async (_, _) => await ExportPackageAsync();
        undoButton.Click += async (_, _) => await UndoAsync();
        redoButton.Click += async (_, _) => await RedoAsync();
        duplicateButton.Click += async (_, _) => await DuplicateSelectedAsync();
        ungroupButton.Click += async (_, _) => await UngroupSelectedAsync();
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        gridButton.Click += async (_, _) => await GenerateGridAsync();
        applyButton.Click += async (_, _) => await ApplyInspectorAsync();
        frameButton.Click += (_, _) => FrameSelected();
        resetTransformButton.Click += async (_, _) => await ResetSelectedTransformAsync();

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
            if (gizmoDrag is GizmoDragState pending)
            {
                session.CancelPendingTransform(pending.SelectedId);
                LoadInspectorFromSelection();
            }
            gizmoDrag = null;
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
            ClearVirtualTriangleSelection();
            expandedObjectIds.Clear();
            trianglePageOffsets.Clear();
            treeExpansionInitialized = false;
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
            ClearVirtualTriangleSelection();
            expandedObjectIds.Clear();
            trianglePageOffsets.Clear();
            treeExpansionInitialized = false;
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
            ClearVirtualTriangleSelection();
            expandedObjectIds.Add(insertedId);
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

            await StopCurrentRenderAsync();
            SetBusy(true, "Saving composition…");

            int triangleCountAtSave = session.TriangleCount;
            Stopwatch saveStopwatch = Stopwatch.StartNew();
            Task saveTask = Task.Run(
                () => session.Save(path, lifetimeCancellation.Token),
                lifetimeCancellation.Token);

            while (!saveTask.IsCompleted)
            {
                Task completed = await Task.WhenAny(saveTask, Task.Delay(500, lifetimeCancellation.Token));
                if (ReferenceEquals(completed, saveTask))
                    break;

                statusText.Text = $"Saving composition… {saveStopwatch.Elapsed.TotalSeconds:0.0}s " +
                                  $"({triangleCountAtSave:N0} triangles).";
            }

            await saveTask;
            saveStopwatch.Stop();
            pathText.Text = session.ScenePath ?? path;
            statusText.Text = $"Saved {Path.GetFileName(session.ScenePath ?? path)} in {saveStopwatch.Elapsed.TotalSeconds:0.0}s.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            statusText.Text = $"Save failed: {ex.Message}";
            Console.Error.WriteLine($"Save failed: {ex}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExportPackageAsync()
    {
        if (!session.HasRenderableScene)
        {
            statusText.Text = "There is no renderable scene to export.";
            return;
        }
        if (!StorageProvider.CanPickFolder)
        {
            statusText.Text = "The desktop folder picker is unavailable.";
            return;
        }

        try
        {
            SceneExportFormat? format = await new ExportFormatDialog().ShowDialog<SceneExportFormat?>(this);
            if (format == null)
                return;

            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Choose parent folder for {format.DisplayName}",
                AllowMultiple = false
            });
            string? parentDirectory = folders.Count == 0 ? null : folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(parentDirectory))
                return;

            SetBusy(true, $"Exporting {format.DisplayName} package…");
            SceneExportPackageResult result = await Task.Run(
                () => session.ExportPackage(parentDirectory, format, lifetimeCancellation.Token),
                lifetimeCancellation.Token);
            statusText.Text = $"Exported {Path.GetFileName(result.PrimaryFilePath)} with {result.Files.Count:N0} files to {result.DirectoryPath}.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            statusText.Text = $"Export failed: {ex.Message}";
            Console.Error.WriteLine($"Export failed: {ex}");
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
            // Capture every Avalonia control value while still on the UI thread.
            // The background worker must receive plain immutable data only; reading
            // nameBox/visibleBox inside Task.Run throws "Call from invalid thread".
            ComposerTransformRequest request = ComposerTransformRequest.Parse(
                positionX.Text, positionY.Text, positionZ.Text,
                rotationX.Text, rotationY.Text, rotationZ.Text,
                scaleX.Text, scaleY.Text, scaleZ.Text);
            ComposerTransformWorkItem workItem = new(
                id,
                nameBox.Text ?? string.Empty,
                visibleBox.IsChecked ?? true,
                request);

            ComposerModelEvidence? beforeEvidence = session.GetModelEvidence(id);
            await StopCurrentRenderAsync();
            SetBusy(true, "Baking transform into the selected geometry…");
            bool updated = await Task.Run(
                () => workItem.Apply(session),
                lifetimeCancellation.Token);
            if (!updated)
                throw new InvalidOperationException("The selected scene node no longer exists.");

            pathText.Text = "Untitled composition (modified)";
            ComposerObjectState? appliedState = session.GetObjectState(id);
            ComposerModelEvidence? afterEvidence = session.GetModelEvidence(id);
            if (appliedState == null || afterEvidence == null)
                throw new InvalidOperationException("The transformed scene node could not be verified.");

            // Baked transforms leave the node transform fields at identity. The
            // authoritative proof is that the underlying world geometry changed.
            if (!NearlyEqual(appliedState.Position, Vec3.Zero) ||
                !NearlyEqual(appliedState.Rotation, Vec3.Zero) ||
                !NearlyEqual(appliedState.Scale, new Vec3(1, 1, 1)))
            {
                throw new InvalidOperationException("The transform was not fully baked into geometry.");
            }
            if (beforeEvidence != null && afterEvidence.SceneRevision <= beforeEvidence.SceneRevision)
                throw new InvalidOperationException("The scene revision did not advance after the transform.");

            bool nonIdentity = request.Position.Length() > 1e-12 ||
                               request.RotationRadians.Length() > 1e-12 ||
                               Math.Abs(request.Scale.X - 1.0) > 1e-12 ||
                               Math.Abs(request.Scale.Y - 1.0) > 1e-12 ||
                               Math.Abs(request.Scale.Z - 1.0) > 1e-12;
            if (nonIdentity && beforeEvidence != null && beforeEvidence.WorldGeometryHash == afterEvidence.WorldGeometryHash)
                throw new InvalidOperationException("The underlying triangle geometry did not change.");

            ClearVirtualTriangleSelection();
            RefreshObjectTree(id);
            ClearTransformTextBoxes();
            UpdateHistoryButtons();
            statusText.Text = $"Baked transform into {appliedState.Name}; scene revision {afterEvidence.SceneRevision}. {session.LastGeometryRefreshDetails}";
            await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            ReportOperationFailure("Transform update failed", ex);
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

        await StopCurrentRenderAsync();
        SetBusy(true, "Resetting the selected node transform…");
        try
        {
            bool reset = await Task.Run(
                () => session.ResetObjectTransform(id),
                lifetimeCancellation.Token);
            if (!reset)
                throw new InvalidOperationException("The selected scene node no longer exists.");

            ClearVirtualTriangleSelection();
            RefreshObjectTree(id);
            LoadInspectorFromSelection();
            pathText.Text = "Untitled composition (modified)";
            statusText.Text = "Selected node transform reset.";
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

    private async Task UndoAsync()
    {
        if (!session.CanUndo)
            return;

        await StopCurrentRenderAsync();
        SetBusy(true, "Undoing edit…");
        try
        {
            int? preferred = await Task.Run(session.Undo, lifetimeCancellation.Token);
            selectedObjectId = preferred;
            pathText.Text = "Untitled composition (modified)";
            ClearVirtualTriangleSelection();
            RefreshObjectTree(preferred);
            ClearTransformTextBoxes();
            UpdateHistoryButtons();
            statusText.Text = $"Undo complete. {session.LastGeometryRefreshDetails}";
            if (session.HasRenderableScene)
                await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Undo failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RedoAsync()
    {
        if (!session.CanRedo)
            return;

        await StopCurrentRenderAsync();
        SetBusy(true, "Redoing edit…");
        try
        {
            int? preferred = await Task.Run(session.Redo, lifetimeCancellation.Token);
            selectedObjectId = preferred;
            pathText.Text = "Untitled composition (modified)";
            ClearVirtualTriangleSelection();
            RefreshObjectTree(preferred);
            ClearTransformTextBoxes();
            UpdateHistoryButtons();
            statusText.Text = $"Redo complete. {session.LastGeometryRefreshDetails}";
            if (session.HasRenderableScene)
                await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Redo failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UngroupSelectedAsync()
    {
        if (selectedObjectId is not int id)
            return;
        if (!session.CanUngroupObject(id))
        {
            statusText.Text = "The selected node is already a terminal triangle and cannot be ungrouped further.";
            return;
        }

        await StopCurrentRenderAsync();
        SetBusy(true, "Ungrouping selected node…");
        try
        {
            IReadOnlyList<int> promoted = await Task.Run(() => session.UngroupObject(id), lifetimeCancellation.Token);
            trianglePageOffsets.Remove(id);
            expandedObjectIds.Remove(id);
            selectedObjectId = promoted.FirstOrDefault();
            if (selectedObjectId == 0)
                selectedObjectId = null;
            pathText.Text = "Untitled composition (modified)";
            ClearVirtualTriangleSelection();
            RefreshObjectTree(selectedObjectId);
            UpdateHistoryButtons();
            statusText.Text = $"Ungrouped into {promoted.Count:N0} node(s).";
            if (session.HasRenderableScene)
                await RequestRenderAsync(interactive: false);
        }
        catch (Exception ex)
        {
            statusText.Text = $"Ungroup failed: {ex.Message}";
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
            ClearVirtualTriangleSelection();
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
        ClearVirtualTriangleSelection();
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
        IReadOnlyList<SceneObjectInfo> infos = session.GetObjectInfos();
        List<ObjectTreeNode> roots = ComposerObjectTree.Build(infos);
        HashSet<int> validIds = infos.Select(info => info.Id).ToHashSet();

        int? target = preferredSelection ?? selectedObjectId;
        if (target.HasValue && ComposerObjectTree.Find(roots, target.Value) == null)
            target = null;
        selectedObjectId = target;

        if (!treeExpansionInitialized)
            treeExpansionInitialized = true;

        expandedObjectIds.RemoveWhere(id => !validIds.Contains(id));
        foreach (int staleId in trianglePageOffsets.Keys.Where(id => !validIds.Contains(id)).ToList())
            trianglePageOffsets.Remove(staleId);

        objectTreePanel.Children.Clear();
        foreach (ObjectTreeNode root in roots)
            objectTreePanel.Children.Add(BuildObjectTreeControl(root, depth: 0));

        if (selectedTriangleGroupId is int triangleGroupId &&
            selectedTriangleIndex is int triangleIndex &&
            selectedObjectId == triangleGroupId &&
            session.SetSelectedTriangle(triangleGroupId, triangleIndex))
        {
            // Virtual triangle selection is restored after rebuilding the UI tree.
        }
        else
        {
            selectedTriangleGroupId = null;
            selectedTriangleIndex = null;
            session.SetSelectedObject(selectedObjectId);
        }
        LoadInspectorFromSelection();
        UpdateHistoryButtons();
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
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.55
            });
        }

        Button select = new()
        {
            Content = new TextBlock
            {
                Text = node.Label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Left
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 28,
            Padding = new Thickness(6, 3),
            BorderThickness = new Thickness(0),
            Background = selectedObjectId == node.Id
                ? new SolidColorBrush(Color.FromArgb(110, 255, 125, 40))
                : Brushes.Transparent
        };
        select.Click += (_, _) => SelectObject(node.Id);
        row.Children.Add(select);
        Grid.SetColumn(select, 1);
        branch.Children.Add(row);

        if (hasExpandableContent && expandedObjectIds.Contains(node.Id))
        {
            foreach (ObjectTreeNode child in node.Children)
                branch.Children.Add(BuildObjectTreeControl(child, depth + 1));

            if (hasTriangleDetails)
                AddLazyTriangleRows(branch, node, depth + 1);
        }

        return branch;
    }

    private void AddLazyTriangleRows(StackPanel branch, ObjectTreeNode node, int depth)
    {
        bool open = trianglePageOffsets.TryGetValue(node.Id, out int pageOffset);
        pageOffset = Math.Clamp(pageOffset, 0, Math.Max(0, node.LocalTriangleCount - 1));

        if (!open)
        {
            Button show = new()
            {
                Content = $"… show triangles ({node.LocalTriangleCount:N0})",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
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

        IReadOnlyList<ComposerTriangleInfo> page = session.GetTriangleInfos(
            node.Id,
            pageOffset,
            TrianglePageSize);
        foreach (ComposerTriangleInfo triangle in page)
        {
            bool selectedTriangle = selectedTriangleGroupId == node.Id &&
                                    selectedTriangleIndex == triangle.Index;
            Button triangleRow = new()
            {
                Content = new TextBlock
                {
                    Text = $"△ {triangle.Label}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = 12
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(depth * 14 + 20, 1, 4, 1),
                Padding = new Thickness(8, 2),
                MinHeight = 24,
                BorderThickness = new Thickness(0),
                Background = selectedTriangle
                    ? new SolidColorBrush(Color.FromArgb(95, 255, 125, 40))
                    : Brushes.Transparent
            };
            triangleRow.Click += (_, _) => SelectTriangle(node.Id, triangle.Index);
            branch.Children.Add(triangleRow);
        }

        int pageEnd = pageOffset + page.Count;
        Grid controls = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 6,
            Margin = new Thickness(depth * 14 + 20, 2, 4, 2)
        };

        Button previous = new()
        {
            Content = "… previous",
            IsEnabled = pageOffset > 0,
            Padding = new Thickness(8, 3)
        };
        previous.Click += (_, _) =>
        {
            trianglePageOffsets[node.Id] = Math.Max(0, pageOffset - TrianglePageSize);
            RefreshObjectTree(selectedObjectId);
        };
        controls.Children.Add(previous);

        Button next = new()
        {
            Content = $"… next ({pageEnd:N0}/{node.LocalTriangleCount:N0})",
            IsEnabled = pageEnd < node.LocalTriangleCount,
            Padding = new Thickness(8, 3)
        };
        next.Click += (_, _) =>
        {
            trianglePageOffsets[node.Id] = Math.Min(
                Math.Max(0, node.LocalTriangleCount - 1),
                pageOffset + TrianglePageSize);
            RefreshObjectTree(selectedObjectId);
        };
        controls.Children.Add(next);
        Grid.SetColumn(next, 1);

        Button hide = new()
        {
            Content = "… hide triangles",
            HorizontalAlignment = HorizontalAlignment.Right,
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

    private void LoadInspectorFromSelection()
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
        ungroupButton.IsEnabled = enabled && selectedObjectId is int id && session.CanUngroupObject(id);
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
            if (TryBeginGizmoDrag(position))
            {
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
        if (gizmoDrag != null)
        {
            UpdateGizmoDrag(e.GetPosition(viewport));
            e.Handled = true;
            return;
        }

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
        if (gizmoDrag != null)
        {
            UpdateGizmoDrag(releasePoint);
            int commitId = gizmoDrag.SelectedId;
            gizmoDrag = null;
            e.Pointer.Capture(null);
            await StopCurrentRenderAsync();
            bool committed = await Task.Run(() => session.CommitPendingTransform(commitId), lifetimeCancellation.Token);
            if (!committed)
            {
                statusText.Text = "The transform target no longer exists.";
                e.Handled = true;
                return;
            }

            ClearVirtualTriangleSelection();
            RefreshObjectTree(commitId);
            ClearTransformTextBoxes();
            UpdateHistoryButtons();
            pathText.Text = "Untitled composition (modified)";
            statusText.Text = $"Baked gizmo move into geometry. {session.LastGeometryRefreshDetails}";
            await RequestRenderAsync(interactive: false);
            e.Handled = true;
            return;
        }

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
            if (movement.Length <= 5.0 && TryViewportToImagePoint(releasePoint, out Point imagePoint))
            {
                double normalizedX = imagePoint.X / Math.Max(1, lastRenderWidth);
                double normalizedY = imagePoint.Y / Math.Max(1, lastRenderHeight);
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

    private bool TryBeginGizmoDrag(Point viewportPoint)
    {
        if (selectedObjectId is not int selectedId ||
            session.GetTransformTargetState(selectedId) is not ComposerObjectState state ||
            session.GetTransformTargetBounds(selectedId) is not Aabb bounds ||
            !TryViewportToImagePoint(viewportPoint, out Point imagePoint))
        {
            return false;
        }

        CameraDefinition camera = session.Camera.Snapshot();
        if (!ComposerOverlayRenderer.TryHitTranslationAxis(
                camera,
                bounds,
                lastRenderWidth,
                lastRenderHeight,
                imagePoint.X,
                imagePoint.Y,
                out ComposerGizmoHit hit))
        {
            return false;
        }

        gizmoDrag = new GizmoDragState(
            selectedId,
            hit.Axis,
            imagePoint,
            state.Position,
            state.Rotation,
            state.Scale,
            hit.ScreenDirectionX,
            hit.ScreenDirectionY,
            hit.WorldUnitsPerPixel);
        statusText.Text = $"Dragging {hit.Axis} translation axis…";
        return true;
    }

    private void UpdateGizmoDrag(Point viewportPoint)
    {
        GizmoDragState? drag = gizmoDrag;
        if (drag == null || !TryViewportToImagePoint(viewportPoint, out Point imagePoint))
            return;

        double deltaX = imagePoint.X - drag.StartImagePoint.X;
        double deltaY = imagePoint.Y - drag.StartImagePoint.Y;
        double pixelDistance = deltaX * drag.ScreenDirectionX + deltaY * drag.ScreenDirectionY;
        double worldDistance = pixelDistance * drag.WorldUnitsPerPixel;
        Vec3 axis = drag.Axis switch
        {
            ComposerGizmoAxis.X => new Vec3(1, 0, 0),
            ComposerGizmoAxis.Y => new Vec3(0, 1, 0),
            ComposerGizmoAxis.Z => new Vec3(0, 0, 1),
            _ => Vec3.Zero
        };
        Vec3 updatedPosition = drag.StartPosition + axis * worldDistance;

        CancelCurrentRender();
        if (!session.UpdateTransformTarget(
                drag.SelectedId,
                updatedPosition,
                drag.Rotation,
                drag.Scale))
        {
            gizmoDrag = null;
            statusText.Text = "The transform target no longer exists.";
            return;
        }

        LoadInspectorFromSelection();
        pathText.Text = "Untitled composition (modified)";
        statusText.Text = $"Pending {drag.Axis} translation: release the mouse to bake once and refresh Vulkan.";
    }

    private bool TryViewportToImagePoint(Point viewportPoint, out Point imagePoint)
    {
        double viewportWidth = viewport.Bounds.Width;
        double viewportHeight = viewport.Bounds.Height;
        if (lastRenderWidth <= 0 || lastRenderHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            imagePoint = default;
            return false;
        }

        double scale = Math.Min(viewportWidth / lastRenderWidth, viewportHeight / lastRenderHeight);
        double displayedWidth = lastRenderWidth * scale;
        double displayedHeight = lastRenderHeight * scale;
        double offsetX = (viewportWidth - displayedWidth) * 0.5;
        double offsetY = (viewportHeight - displayedHeight) * 0.5;
        double localX = viewportPoint.X - offsetX;
        double localY = viewportPoint.Y - offsetY;
        if (localX < 0 || localY < 0 || localX > displayedWidth || localY > displayedHeight)
        {
            imagePoint = default;
            return false;
        }

        imagePoint = new Point(localX / scale, localY / scale);
        return true;
    }

    private void SelectObject(int id)
    {
        if (session.GetObjectState(id) == null)
            return;

        selectedObjectId = id;
        ClearVirtualTriangleSelection();
        RefreshObjectTree(id);
        _ = RequestRenderAsync(interactive: false);
    }

    private void SelectTriangle(int groupId, int triangleIndex)
    {
        if (!session.SetSelectedTriangle(groupId, triangleIndex))
            return;

        selectedObjectId = groupId;
        selectedTriangleGroupId = groupId;
        selectedTriangleIndex = triangleIndex;
        RefreshObjectTree(groupId);
        statusText.Text = $"Selected virtual Triangle {triangleIndex + 1:N0}. It adds no scene node; transforms and ungroup target the owning mesh.";
        _ = RequestRenderAsync(interactive: false);
    }

    private void ClearVirtualTriangleSelection()
    {
        selectedTriangleGroupId = null;
        selectedTriangleIndex = null;
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

    private async Task StopCurrentRenderAsync()
    {
        renderVersion++;
        renderAgain = false;
        pendingInteractive = false;
        activeRenderCancellation?.Cancel();

        while (rendering && !lifetimeCancellation.IsCancellationRequested)
            await Task.Delay(8, lifetimeCancellation.Token);
    }

    private static bool NearlyEqual(Vec3 left, Vec3 right)
    {
        const double tolerance = 1e-8;
        return Math.Abs(left.X - right.X) <= tolerance &&
               Math.Abs(left.Y - right.Y) <= tolerance &&
               Math.Abs(left.Z - right.Z) <= tolerance;
    }

    private void ClearTransformTextBoxes()
    {
        foreach (TextBox box in TransformTextBoxes())
            box.Text = string.Empty;
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
        exportButton.IsEnabled = !busy;
        rendererBox.IsEnabled = !busy;
        objectTree.IsEnabled = !busy;
        if (selectedObjectId.HasValue)
            SetInspectorEnabled(!busy);
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
