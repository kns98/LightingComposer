// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CameraManagerWindow.cs
// Purpose: Modeless manager for the scene's named cameras.
//
// Cameras defined here are stored in the scene and picked up by the Animation
// Studio shot list. Edits apply immediately without undo steps and mark the
// document modified.
// -----------------------------------------------------------------------------
using Avalonia;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class CameraManagerWindow : Window
{
    private readonly ComposerSceneSession session;
    private readonly Action markModified;
    private readonly Action requestPreview;
    private readonly Action onClosed;

    private readonly ListBox cameraList;
    private readonly TextBox positionXBox;
    private readonly TextBox positionYBox;
    private readonly TextBox positionZBox;
    private readonly TextBox targetXBox;
    private readonly TextBox targetYBox;
    private readonly TextBox targetZBox;
    private readonly TextBox fovBox;
    private readonly CheckBox markersBox;
    private readonly TextBlock statusText;

    /// <summary>Creates a modeless camera manager bound to the session.</summary>
    public CameraManagerWindow(
        ComposerSceneSession session,
        Action markModified,
        Action requestPreview,
        Action onClosed)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.markModified = markModified ?? (() => { });
        this.requestPreview = requestPreview ?? (() => { });
        this.onClosed = onClosed ?? (() => { });

        Title = "Cameras";
        Width = 420;
        Height = 480;
        MinWidth = 340;
        MinHeight = 360;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        cameraList = new ListBox { MinHeight = 200 };
        cameraList.SelectionChanged += (_, _) => { LoadSelectedTransform(); UpdateStatusForSelection(); };
        positionXBox = SmallNumberBox();
        positionYBox = SmallNumberBox();
        positionZBox = SmallNumberBox();
        targetXBox = SmallNumberBox();
        targetYBox = SmallNumberBox();
        targetZBox = SmallNumberBox();
        fovBox = SmallNumberBox();
        markersBox = new CheckBox { Content = "Show camera markers", IsChecked = session.ShowCameraMarkers };
        markersBox.IsCheckedChanged += (_, _) =>
        {
            session.SetShowCameraMarkers(markersBox.IsChecked == true);
            requestPreview();
        };
        statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.76, FontSize = 12 };

        Content = BuildContent();
        RefreshAll();
        Activated += (_, _) => RefreshAll();
        Closed += (_, _) => onClosed();
    }

    /// <summary>Builds the manager content with its camera list and actions.</summary>
    private Control BuildContent()
    {
        StackPanel stack = new() { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = "Cameras",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Named viewpoints saved in this scene. The Animation Studio shoots them; preview jumps the viewport to the selected camera.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });
        stack.Children.Add(cameraList);

        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 6 };
        Button addCamera = Tip(NewButton("Add from view"), "Saves the current viewport camera under a new name.");
        addCamera.Click += (_, _) => AddCameraFromView();
        Button previewCamera = Tip(NewButton("Preview"), "Moves the viewport to the selected camera.");
        previewCamera.Click += (_, _) => PreviewSelectedCamera();
        Button updateCamera = Tip(NewButton("Update from view"), "Overwrites the selected camera with the current viewport.");
        updateCamera.Click += (_, _) => UpdateSelectedCamera();
        Button renameCamera = Tip(NewButton("Rename"), "Renames the selected camera.");
        renameCamera.Click += async (_, _) => await RenameSelectedCameraAsync();
        Button deleteCamera = Tip(NewButton("Delete"), "Deletes the selected camera and its shots.");
        deleteCamera.Click += (_, _) => DeleteSelectedCamera();
        Button[] buttons = new[] { addCamera, previewCamera, updateCamera, renameCamera, deleteCamera };
        for (int i = 0; i < buttons.Length; i++)
        {
            int gridRow = i / 3;
            while (row.RowDefinitions.Count <= gridRow)
                row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            row.Children.Add(buttons[i]);
            Grid.SetRow(buttons[i], gridRow);
            Grid.SetColumn(buttons[i], i % 3);
        }
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock { Text = "Transform", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        Grid transform = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,*,*"), ColumnSpacing = 6, RowSpacing = 6 };
        AddVectorRow(transform, "Position", positionXBox, positionYBox, positionZBox, 0);
        AddVectorRow(transform, "Target", targetXBox, targetYBox, targetZBox, 1);
        TextBlock fovLabel = new() { Text = "FOV", VerticalAlignment = VerticalAlignment.Center };
        transform.Children.Add(fovLabel);
        Grid.SetRow(fovLabel, 2);
        transform.Children.Add(fovBox);
        Grid.SetRow(fovBox, 2);
        Grid.SetColumn(fovBox, 1);
        Grid.SetColumnSpan(fovBox, 3);
        while (transform.RowDefinitions.Count < 3)
            transform.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        stack.Children.Add(transform);
        Button applyTransform = Tip(NewButton("Apply transform"), "Writes the position, target, and field of view into the selected camera.");
        applyTransform.HorizontalAlignment = HorizontalAlignment.Right;
        applyTransform.Click += (_, _) => ApplyTransform();
        stack.Children.Add(applyTransform);
        stack.Children.Add(markersBox);
        stack.Children.Add(statusText);

        return new Border
        {
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };
    }

    /// <summary>Creates a right-aligned numeric text box.</summary>
    private static TextBox SmallNumberBox() => new() { MinWidth = 70, TextAlignment = TextAlignment.Right };

    /// <summary>Adds a labeled XYZ row to the given grid.</summary>
    private static void AddVectorRow(Grid grid, string label, TextBox x, TextBox y, TextBox z, int row)
    {
        while (grid.RowDefinitions.Count <= row)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        Grid.SetRow(text, row);
        grid.Children.Add(x);
        Grid.SetRow(x, row);
        Grid.SetColumn(x, 1);
        grid.Children.Add(y);
        Grid.SetRow(y, row);
        Grid.SetColumn(y, 2);
        grid.Children.Add(z);
        Grid.SetRow(z, row);
        Grid.SetColumn(z, 3);
    }

    /// <summary>Creates a button with the given text.</summary>
    private static Button NewButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6),
        MinWidth = 78
    };

    /// <summary>Sets a tooltip on the given control and returns it for inline use.</summary>
    private static T Tip<T>(T control, string text) where T : Control
    {
        ToolTip.SetTip(control, text);
        return control;
    }

    /// <summary>Reloads the camera list, preserving the selection when possible.</summary>
    private void RefreshAll()
    {
        IReadOnlyList<SceneCamera> cameras = session.GetStudioState().Cameras;
        int? keep = (cameraList.SelectedItem as SceneCamera)?.Id;
        cameraList.ItemsSource = cameras.ToArray();
        cameraList.SelectedItem = cameras.FirstOrDefault(camera => camera.Id == keep) ?? cameras.FirstOrDefault();
        markersBox.IsChecked = session.ShowCameraMarkers;
    }

    /// <summary>Refreshes the list from the scene and selects the given camera.</summary>
    public void RefreshFromSelection(int? selectedId)
    {
        IReadOnlyList<SceneCamera> cameras = session.GetStudioState().Cameras;
        cameraList.ItemsSource = cameras.ToArray();
        cameraList.SelectedItem = selectedId.HasValue
            ? cameras.FirstOrDefault(camera => camera.Id == selectedId.Value)
            : null;
        LoadSelectedTransform();
        UpdateStatusForSelection();
    }

    /// <summary>Loads the selected camera transform into the editors.</summary>
    private void LoadSelectedTransform()
    {
        if (cameraList.SelectedItem is not SceneCamera camera)
        {
            positionXBox.Text = string.Empty;
            positionYBox.Text = string.Empty;
            positionZBox.Text = string.Empty;
            targetXBox.Text = string.Empty;
            targetYBox.Text = string.Empty;
            targetZBox.Text = string.Empty;
            fovBox.Text = string.Empty;
            return;
        }
        positionXBox.Text = Format(camera.Definition.Position.X);
        positionYBox.Text = Format(camera.Definition.Position.Y);
        positionZBox.Text = Format(camera.Definition.Position.Z);
        targetXBox.Text = Format(camera.Definition.Target.X);
        targetYBox.Text = Format(camera.Definition.Target.Y);
        targetZBox.Text = Format(camera.Definition.Target.Z);
        fovBox.Text = Format(camera.Definition.FieldOfViewDegrees);
    }

    /// <summary>Writes the transform editors into the selected camera.</summary>
    private void ApplyTransform()
    {
        if (cameraList.SelectedItem is not SceneCamera camera)
        {
            statusText.Text = "Select a camera first.";
            return;
        }
        if (!TryVec(positionXBox, positionYBox, positionZBox, out Vec3 position) ||
            !TryVec(targetXBox, targetYBox, targetZBox, out Vec3 target) ||
            !TryDouble(fovBox.Text, out double fov) || fov < 1.0 || fov > 179.0)
        {
            statusText.Text = "Enter finite XYZ numbers and a field of view between 1 and 179 degrees.";
            return;
        }
        CameraDefinition definition = camera.Definition.Clone();
        definition.Position = position;
        definition.Target = target;
        definition.FieldOfViewDegrees = fov;
        session.SetStudioCameraDefinition(camera.Id, definition);
        markModified();
        RefreshAll();
        requestPreview();
        statusText.Text = $"Camera '{camera.Name}' transform applied.";
    }

    /// <summary>Shows the selected camera summary in the status line.</summary>
    private void UpdateStatusForSelection()
    {
        if (cameraList.SelectedItem is not SceneCamera camera)
        {
            statusText.Text = "Add a camera from the current viewport to begin.";
            return;
        }
        CameraDefinition definition = camera.Definition;
        statusText.Text =
            $"'{camera.Name}' at ({definition.Position.X:0.##}, {definition.Position.Y:0.##}, {definition.Position.Z:0.##}), " +
            $"target ({definition.Target.X:0.##}, {definition.Target.Y:0.##}, {definition.Target.Z:0.##}), " +
            $"fov {definition.FieldOfViewDegrees:0.#}°.";
    }

    /// <summary>Saves the viewport camera under a new name.</summary>
    private void AddCameraFromView()
    {
        CameraDefinition definition = session.Camera.Snapshot();
        IReadOnlyList<SceneCamera> cameras = session.GetStudioState().Cameras;
        int number = cameras.Count + 1;
        string name = $"Camera {number}";
        while (cameras.Any(camera => camera.Name == name))
            name = $"Camera {++number}";
        session.AddStudioCamera(name, definition);
        markModified();
        RefreshAll();
        cameraList.SelectedItem = session.GetStudioState().Cameras.FirstOrDefault(camera => camera.Name == name);
        statusText.Text = $"Camera '{name}' saved from the current view.";
    }

    /// <summary>Moves the viewport to the selected camera.</summary>
    private void PreviewSelectedCamera()
    {
        if (cameraList.SelectedItem is not SceneCamera camera)
        {
            statusText.Text = "Select a camera first.";
            return;
        }
        session.Camera.ApplyView(camera.Definition);
        requestPreview();
        statusText.Text = $"Previewing '{camera.Name}'.";
    }

    /// <summary>Overwrites the selected camera with the current viewport.</summary>
    private void UpdateSelectedCamera()
    {
        if (cameraList.SelectedItem is not SceneCamera camera)
        {
            statusText.Text = "Select a camera first.";
            return;
        }
        session.SetStudioCameraDefinition(camera.Id, session.Camera.Snapshot());
        markModified();
        RefreshAll();
        LoadSelectedTransform();
        UpdateStatusForSelection();
        statusText.Text = $"Camera '{camera.Name}' updated from the current view.";
    }

    /// <summary>Renames the selected camera.</summary>
    private async Task RenameSelectedCameraAsync()
    {
        if (cameraList.SelectedItem is not SceneCamera camera)
        {
            statusText.Text = "Select a camera first.";
            return;
        }
        string? name = await TextPromptDialog.AskAsync(this, "Rename camera", "Camera name:", "Rename");
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == camera.Name)
            return;
        session.RenameStudioCamera(camera.Id, name);
        markModified();
        RefreshAll();
    }

    /// <summary>Deletes the selected camera and its shots.</summary>
    private void DeleteSelectedCamera()
    {
        if (cameraList.SelectedItem is not SceneCamera camera)
        {
            statusText.Text = "Select a camera first.";
            return;
        }
        session.RemoveStudioCamera(camera.Id);
        markModified();
        RefreshAll();
        statusText.Text = $"Camera '{camera.Name}' and its shots were deleted.";
    }

    /// <summary>Formats the given value for the numeric editors.</summary>
    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Parses a finite number from the given text.</summary>
    private static bool TryDouble(string? text, out double value)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        bool ok = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                  double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return ok && double.IsFinite(value);
    }

    /// <summary>Parses a finite vector from the given boxes.</summary>
    private static bool TryVec(TextBox x, TextBox y, TextBox z, out Vec3 value)
    {
        value = Vec3.Zero;
        if (!TryDouble(x.Text, out double vx) || !TryDouble(y.Text, out double vy) || !TryDouble(z.Text, out double vz))
            return false;
        value = new Vec3(vx, vy, vz);
        return true;
    }
}
