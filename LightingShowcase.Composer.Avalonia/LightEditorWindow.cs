using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Modeless editor for scene lights. The list and property controls edit the
/// renderer-backed SceneLight collection; preview markers are editor overlays and
/// never become renderable scene geometry.
/// </summary>
internal sealed class LightEditorWindow : Window
{
    private readonly ComposerSceneSession session;
    private readonly Action<int?> selectLight;
    private readonly Action onLightChanged;
    private readonly Action onPreviewChanged;
    private readonly Action onClosed;

    private readonly ListBox lightList;
    private readonly CheckBox showMarkersBox;
    private readonly TextBox idBox;
    private readonly ComboBox kindBox;
    private readonly CheckBox enabledBox;
    private readonly CheckBox shadowBox;
    private readonly TextBox positionX;
    private readonly TextBox positionY;
    private readonly TextBox positionZ;
    private readonly TextBox directionX;
    private readonly TextBox directionY;
    private readonly TextBox directionZ;
    private readonly ComboBox aimObjectBox;
    private readonly Button aimObjectButton;
    private readonly TextBox colorHexBox;
    private readonly TextBox intensityBox;
    private readonly TextBox rangeBox;
    private readonly TextBox innerConeBox;
    private readonly TextBox outerConeBox;
    private readonly TextBlock provenanceText;
    private readonly TextBlock statusText;
    private readonly Button deleteButton;
    private readonly Button applyButton;
    private IReadOnlyList<SceneObjectInfo> aimTargets = Array.Empty<SceneObjectInfo>();
    private bool synchronizing;

    public LightEditorWindow(
        ComposerSceneSession session,
        Action<int?> selectLight,
        Action onLightChanged,
        Action onPreviewChanged,
        Action onClosed)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.selectLight = selectLight ?? (_ => { });
        this.onLightChanged = onLightChanged ?? (() => { });
        this.onPreviewChanged = onPreviewChanged ?? (() => { });
        this.onClosed = onClosed ?? (() => { });

        Title = "Lighting Editor";
        Width = 760;
        Height = 760;
        MinWidth = 640;
        MinHeight = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        lightList = new ListBox { MinWidth = 235, MinHeight = 320 };
        showMarkersBox = new CheckBox
        {
            Content = "Show light markers and light move gizmo in preview",
            IsChecked = session.ShowLightMarkers
        };
        idBox = FieldBox();
        kindBox = new ComboBox { ItemsSource = Enum.GetValues<SceneLightKind>(), MinWidth = 140 };
        enabledBox = new CheckBox { Content = "Enabled" };
        shadowBox = new CheckBox { Content = "Casts shadows" };
        positionX = NumberBox();
        positionY = NumberBox();
        positionZ = NumberBox();
        directionX = NumberBox();
        directionY = NumberBox();
        directionZ = NumberBox();
        aimObjectBox = new ComboBox { MinWidth = 190 };
        aimObjectButton = NewButton("Aim at object");
        colorHexBox = new TextBox { MinWidth = 110, Watermark = "#FFFFFF", TextAlignment = TextAlignment.Center };
        intensityBox = NumberBox();
        rangeBox = NumberBox();
        innerConeBox = NumberBox();
        outerConeBox = NumberBox();
        provenanceText = new TextBlock { Opacity = 0.68, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        statusText = new TextBlock
        {
            Text = "Select a light to edit it. Right-clicking a light marker in the viewport opens this editor.",
            Opacity = 0.76,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        deleteButton = NewButton("Delete light");
        applyButton = NewButton("Apply");

        lightList.SelectionChanged += (_, _) => OnListSelectionChanged();
        showMarkersBox.Click += (_, _) =>
        {
            if (synchronizing)
                return;
            session.SetShowLightMarkers(showMarkersBox.IsChecked == true);
            onPreviewChanged();
        };
        kindBox.SelectionChanged += (_, _) => UpdateKindFieldState();
        aimObjectButton.Click += (_, _) => AimAtObject();
        applyButton.Click += async (_, _) => await ApplyAsync();
        deleteButton.Click += async (_, _) => await DeleteAsync();

        Content = BuildContent();
        RefreshFromScene(session.SelectedLightIndex);
        Closed += (_, _) => this.onClosed();
    }

    public void SelectLight(int? index)
    {
        RefreshFromScene(index);
        Activate();
    }

    public void RefreshFromScene(int? preferredIndex = null)
    {
        IReadOnlyList<ComposerLightModel> lights = session.GetLightInfos();
        synchronizing = true;
        try
        {
            lightList.ItemsSource = lights.Select(light => light.DisplayLabel).ToArray();
            showMarkersBox.IsChecked = session.ShowLightMarkers;
            RefreshAimTargets();
            int index = preferredIndex ?? session.SelectedLightIndex ?? -1;
            if (index >= lights.Count)
                index = lights.Count - 1;
            lightList.SelectedIndex = index;
            LoadModel(index >= 0 && index < lights.Count ? lights[index] : null);
        }
        finally
        {
            synchronizing = false;
        }
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(14),
            RowSpacing = 10
        };

        StackPanel header = new() { Spacing = 5 };
        header.Children.Add(new TextBlock { Text = "Lighting Editor", FontSize = 21, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "Scene lights are renderer-backed. The viewport icons and move gizmo are editor-only overlays; hiding them does not disable the lights.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.76
        });
        header.Children.Add(showMarkersBox);
        root.Children.Add(header);

        Grid body = new()
        {
            ColumnDefinitions = new ColumnDefinitions("245,10,*"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        body.Children.Add(BuildListPanel());
        Control properties = BuildPropertyPanel();
        body.Children.Add(properties);
        Grid.SetColumn(properties, 2);
        root.Children.Add(body);
        Grid.SetRow(body, 1);

        root.Children.Add(statusText);
        Grid.SetRow(statusText, 2);
        return root;
    }

    private Control BuildListPanel()
    {
        Grid panel = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 7 };
        panel.Children.Add(Heading("Lights"));
        panel.Children.Add(lightList);
        Grid.SetRow(lightList, 1);

        Grid addRow = new() { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        Button point = NewButton("+ Point");
        Button spot = NewButton("+ Spot");
        point.Click += async (_, _) => await AddAsync(SceneLightKind.Point);
        spot.Click += async (_, _) => await AddAsync(SceneLightKind.Spot);
        addRow.Children.Add(point);
        addRow.Children.Add(spot);
        Grid.SetColumn(spot, 1);
        panel.Children.Add(addRow);
        Grid.SetRow(addRow, 2);

        Button directional = NewButton("+ Directional");
        directional.Click += async (_, _) => await AddAsync(SceneLightKind.Directional);
        Grid finalButtons = new() { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        finalButtons.Children.Add(directional);
        finalButtons.Children.Add(deleteButton);
        Grid.SetColumn(deleteButton, 1);
        panel.Children.Add(finalButtons);
        Grid.SetRow(finalButtons, 3);
        return panel;
    }

    private Control BuildPropertyPanel()
    {
        StackPanel stack = new() { Spacing = 8 };
        stack.Children.Add(Heading("Selected light"));
        stack.Children.Add(Labeled("Name / ID", idBox));
        stack.Children.Add(Labeled("Type", kindBox));

        Grid flags = new() { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        flags.Children.Add(enabledBox);
        flags.Children.Add(shadowBox);
        Grid.SetColumn(shadowBox, 1);
        stack.Children.Add(flags);

        stack.Children.Add(Heading("Position (m)"));
        stack.Children.Add(VectorRow(positionX, positionY, positionZ));
        stack.Children.Add(Heading("Direction"));
        stack.Children.Add(VectorRow(directionX, directionY, directionZ));

        Grid aimRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        aimRow.Children.Add(aimObjectBox);
        aimRow.Children.Add(aimObjectButton);
        Grid.SetColumn(aimObjectButton, 1);
        stack.Children.Add(Labeled("Aim at object", aimRow));

        stack.Children.Add(Labeled("Color", colorHexBox));
        stack.Children.Add(Labeled("Intensity", intensityBox));
        stack.Children.Add(Labeled("Range (m; 0 = unlimited)", rangeBox));
        stack.Children.Add(Heading("Spot cone (degrees)"));

        Grid cones = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), ColumnSpacing = 6 };
        cones.Children.Add(new TextBlock { Text = "Inner", VerticalAlignment = VerticalAlignment.Center });
        cones.Children.Add(innerConeBox);
        Grid.SetColumn(innerConeBox, 1);
        TextBlock outerLabel = new() { Text = "Outer", VerticalAlignment = VerticalAlignment.Center };
        cones.Children.Add(outerLabel);
        Grid.SetColumn(outerLabel, 2);
        cones.Children.Add(outerConeBox);
        Grid.SetColumn(outerConeBox, 3);
        stack.Children.Add(cones);

        stack.Children.Add(provenanceText);
        stack.Children.Add(applyButton);
        stack.Children.Add(new TextBlock
        {
            Text = "Tip: for a spot or directional light, choose a scene object and press Aim at object to fill a normalized direction vector toward the object center. For directional lights, Position is only the editor marker/aim origin; illumination depends on Direction. Apply saves the values and closes this window.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.66,
            FontSize = 12
        });

        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    private void OnListSelectionChanged()
    {
        if (synchronizing)
            return;
        int index = lightList.SelectedIndex;
        selectLight(index >= 0 ? index : null);
        ComposerLightModel? model = index >= 0 ? session.GetLightInfo(index) : null;
        LoadModel(model);
        onPreviewChanged();
    }

    private async Task AddAsync(SceneLightKind kind)
    {
        try
        {
            IsEnabled = false;
            int index = await Task.Run(() => session.AddLight(kind));
            selectLight(index);
            RefreshFromScene(index);
            statusText.Text = $"Added {kind.ToString().ToLowerInvariant()} light.";
            onLightChanged();
        }
        catch (Exception ex)
        {
            statusText.Text = $"Could not add light: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async Task DeleteAsync()
    {
        int index = lightList.SelectedIndex;
        if (index < 0)
            return;
        try
        {
            IsEnabled = false;
            bool changed = await Task.Run(() => session.DeleteLight(index));
            if (!changed)
                return;
            int? selected = session.SelectedLightIndex;
            selectLight(selected);
            RefreshFromScene(selected);
            statusText.Text = "Light deleted.";
            onLightChanged();
        }
        catch (Exception ex)
        {
            statusText.Text = $"Could not delete light: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async Task ApplyAsync()
    {
        int index = lightList.SelectedIndex;
        ComposerLightModel? current = index >= 0 ? session.GetLightInfo(index) : null;
        if (current == null)
            return;
        if (!TryReadModel(current, out ComposerLightModel? updated, out string error) || updated == null)
        {
            statusText.Text = error;
            return;
        }

        try
        {
            IsEnabled = false;
            bool changed = await Task.Run(() => session.UpdateLight(index, updated));
            if (!changed)
            {
                statusText.Text = "The selected light no longer exists.";
                return;
            }
            RefreshFromScene(index);
            statusText.Text = "Light properties applied.";
            onLightChanged();
            Close();
        }
        catch (Exception ex)
        {
            statusText.Text = $"Light update failed: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private bool TryReadModel(ComposerLightModel current, out ComposerLightModel? model, out string error)
    {
        model = null;
        error = string.Empty;
        if (kindBox.SelectedItem is not SceneLightKind kind)
            return Fail("Choose a light type.", out error);
        if (!TryVec(positionX, positionY, positionZ, out Vec3 position))
            return Fail("Position X/Y/Z must be valid numbers in meters.", out error);
        if (!TryVec(directionX, directionY, directionZ, out Vec3 direction) || direction.Length() < 1e-8)
            return Fail("Direction X/Y/Z must form a non-zero vector.", out error);
        if (!TryHex(colorHexBox.Text, out Vec3 color))
            return Fail("Color must be a hex RGB value such as #FFD080.", out error);
        if (!TryNonNegative(intensityBox.Text, out double intensity))
            return Fail("Intensity must be zero or greater.", out error);
        if (!TryNonNegative(rangeBox.Text, out double range))
            return Fail("Range must be zero or greater meters.", out error);
        if (!TryAngle(innerConeBox.Text, out double inner))
            return Fail("Inner cone must be between 0 and 90 degrees.", out error);
        if (!TryAngle(outerConeBox.Text, out double outer))
            return Fail("Outer cone must be between 0 and 90 degrees.", out error);
        if (inner > outer)
            return Fail("Spot inner cone cannot be larger than the outer cone.", out error);

        model = current with
        {
            Id = string.IsNullOrWhiteSpace(idBox.Text) ? current.Id : idBox.Text.Trim(),
            Kind = kind,
            Position = position,
            Direction = direction,
            Color = color,
            Intensity = intensity,
            Range = range,
            InnerConeAngle = inner,
            OuterConeAngle = outer,
            Enabled = enabledBox.IsChecked == true,
            CastsShadow = shadowBox.IsChecked == true
        };
        return true;
    }

    private void LoadModel(ComposerLightModel? model)
    {
        bool enabled = model != null;
        idBox.IsEnabled = enabled;
        kindBox.IsEnabled = enabled;
        enabledBox.IsEnabled = enabled;
        shadowBox.IsEnabled = enabled;
        applyButton.IsEnabled = enabled;
        deleteButton.IsEnabled = enabled;

        foreach (TextBox box in new[] { positionX, positionY, positionZ, directionX, directionY, directionZ, colorHexBox, intensityBox, rangeBox, innerConeBox, outerConeBox })
            box.IsEnabled = enabled;

        if (model == null)
        {
            idBox.Text = string.Empty;
            kindBox.SelectedIndex = -1;
            enabledBox.IsChecked = false;
            shadowBox.IsChecked = false;
            SetVec(positionX, positionY, positionZ, Vec3.Zero);
            SetVec(directionX, directionY, directionZ, new Vec3(0.0, -1.0, 0.0));
            colorHexBox.Text = "#FFFFFF";
            intensityBox.Text = "0";
            rangeBox.Text = "0";
            innerConeBox.Text = "0";
            outerConeBox.Text = "0";
            aimObjectBox.IsEnabled = false;
            aimObjectButton.IsEnabled = false;
            provenanceText.Text = "No lights in the scene.";
            return;
        }

        idBox.Text = model.Id;
        kindBox.SelectedItem = model.Kind;
        enabledBox.IsChecked = model.Enabled;
        shadowBox.IsChecked = model.CastsShadow;
        SetVec(positionX, positionY, positionZ, model.Position);
        Vec3 displayedDirection = NormalizeDirectionForEditor(model.Direction, model.Kind);
        SetVec(directionX, directionY, directionZ, displayedDirection);
        colorHexBox.Text = ToHex(model.Color);
        intensityBox.Text = Format(model.Intensity);
        rangeBox.Text = Format(model.Range);
        innerConeBox.Text = Format(model.InnerConeAngle * 180.0 / Math.PI);
        outerConeBox.Text = Format(model.OuterConeAngle * 180.0 / Math.PI);
        provenanceText.Text = model.IsImported || model.IsDefault
            ? $"Source: {(model.IsImported ? "imported" : "built-in default")}. Source flags are read-only; all renderer-backed light properties above remain editable."
            : "Source: user light.";
        UpdateKindFieldState();
    }

    private void UpdateKindFieldState()
    {
        if (kindBox.SelectedItem is not SceneLightKind kind)
        {
            aimObjectBox.IsEnabled = false;
            aimObjectButton.IsEnabled = false;
            return;
        }

        bool directional = kind == SceneLightKind.Directional;
        bool spot = kind == SceneLightKind.Spot;
        bool hasLight = lightList.SelectedIndex >= 0;

        directionX.IsEnabled = directionY.IsEnabled = directionZ.IsEnabled = hasLight && (directional || spot);
        rangeBox.IsEnabled = hasLight && !directional;
        innerConeBox.IsEnabled = outerConeBox.IsEnabled = hasLight && spot;
        bool canAim = hasLight && (directional || spot) && aimTargets.Count > 0;
        aimObjectBox.IsEnabled = canAim;
        aimObjectButton.IsEnabled = canAim;

        // Spot/directional lights should never present blank direction fields.
        // This also repairs older scenes whose direction was zero or invalid.
        if (hasLight && (directional || spot) &&
            (!TryVec(directionX, directionY, directionZ, out Vec3 currentDirection) ||
             !IsFinite(currentDirection) ||
             currentDirection.Length() < 1e-8))
        {
            SetVec(
                directionX,
                directionY,
                directionZ,
                kind == SceneLightKind.Spot ? new Vec3(0.0, -1.0, 0.0) : new Vec3(0.0, 0.0, -1.0));
        }
    }

    private void RefreshAimTargets()
    {
        int previousTargetId = aimObjectBox.SelectedIndex >= 0 && aimObjectBox.SelectedIndex < aimTargets.Count
            ? aimTargets[aimObjectBox.SelectedIndex].Id
            : -1;

        aimTargets = session.GetObjectInfos()
            .Where(info => info.TriangleCount > 0)
            .ToArray();

        aimObjectBox.ItemsSource = aimTargets
            .Select(info => $"{new string('·', Math.Min(info.Depth, 6))}{(info.Depth > 0 ? " " : string.Empty)}{info.Name}  [#{info.Id}]")
            .ToArray();

        int selectedIndex = previousTargetId >= 0
            ? aimTargets.ToList().FindIndex(info => info.Id == previousTargetId)
            : -1;
        if (selectedIndex < 0 && aimTargets.Count > 0)
            selectedIndex = 0;
        aimObjectBox.SelectedIndex = selectedIndex;
    }

    private void AimAtObject()
    {
        if (kindBox.SelectedItem is not SceneLightKind kind ||
            (kind != SceneLightKind.Spot && kind != SceneLightKind.Directional))
        {
            statusText.Text = "Aim at object is available for spot and directional lights.";
            return;
        }

        int targetIndex = aimObjectBox.SelectedIndex;
        if (targetIndex < 0 || targetIndex >= aimTargets.Count)
        {
            statusText.Text = "Choose a scene object to aim at.";
            return;
        }

        if (!TryVec(positionX, positionY, positionZ, out Vec3 lightPosition) || !IsFinite(lightPosition))
        {
            statusText.Text = "Position X/Y/Z must be valid numbers before aiming.";
            return;
        }

        SceneObjectInfo target = aimTargets[targetIndex];
        Vec3? targetCenter = session.GetObjectAimCenter(target.Id);
        if (targetCenter is not Vec3 center)
        {
            statusText.Text = $"Could not determine the center of {target.Name}.";
            return;
        }

        Vec3 delta = center - lightPosition;
        if (!IsFinite(delta) || delta.Length() < 1e-8)
        {
            statusText.Text = "The light is at the target center, so an aim direction cannot be calculated.";
            return;
        }

        Vec3 direction = delta.Normalize();
        SetVec(directionX, directionY, directionZ, direction);
        string kindNote = kind == SceneLightKind.Directional
            ? " Directional-light Position is only the editor marker/aim origin; the renderer uses the resulting Direction globally."
            : string.Empty;
        statusText.Text =
            $"Direction filled toward {target.Name}: ({Format(direction.X)}, {Format(direction.Y)}, {Format(direction.Z)}).{kindNote} Press Apply to save.";
    }

    private static Vec3 NormalizeDirectionForEditor(Vec3 direction, SceneLightKind kind)
    {
        if (IsFinite(direction))
        {
            Vec3 normalized = direction.Normalize();
            if (normalized.Length() >= 1e-8)
                return normalized;
        }

        return kind == SceneLightKind.Spot
            ? new Vec3(0.0, -1.0, 0.0)
            : new Vec3(0.0, 0.0, -1.0);
    }

    private static bool IsFinite(Vec3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static Grid Labeled(string label, Control control)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("175,*"), ColumnSpacing = 8 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        Grid.SetColumn(control, 1);
        return row;
    }

    private static Grid VectorRow(TextBox x, TextBox y, TextBox z)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"), ColumnSpacing = 5 };
        Add("X", x, 0);
        Add("Y", y, 2);
        Add("Z", z, 4);
        return row;

        void Add(string label, TextBox box, int column)
        {
            TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(text);
            Grid.SetColumn(text, column);
            row.Children.Add(box);
            Grid.SetColumn(box, column + 1);
        }
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        FontSize = 14
    };

    private static TextBox FieldBox() => new() { MinHeight = 28 };
    private static TextBox NumberBox() => new() { MinHeight = 28, TextAlignment = TextAlignment.Right };
    private static Button NewButton(string text) => new() { Content = text, MinHeight = 30 };
    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static void SetVec(TextBox x, TextBox y, TextBox z, Vec3 value)
    {
        x.Text = Format(value.X);
        y.Text = Format(value.Y);
        z.Text = Format(value.Z);
    }

    private static bool TryVec(TextBox x, TextBox y, TextBox z, out Vec3 value)
    {
        if (TryDouble(x.Text, out double vx) && TryDouble(y.Text, out double vy) && TryDouble(z.Text, out double vz))
        {
            value = new Vec3(vx, vy, vz);
            return true;
        }
        value = Vec3.Zero;
        return false;
    }

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool TryNonNegative(string? text, out double value) =>
        TryDouble(text, out value) && value >= 0.0;

    private static bool TryAngle(string? text, out double radians)
    {
        radians = 0.0;
        if (!TryDouble(text, out double degrees) || degrees < 0.0 || degrees > 90.0)
            return false;
        radians = degrees * Math.PI / 180.0;
        return true;
    }

    private static bool TryHex(string? text, out Vec3 color)
    {
        color = Vec3.Zero;
        string value = (text ?? string.Empty).Trim();
        if (value.StartsWith('#'))
            value = value[1..];
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;
        color = new Vec3(((rgb >> 16) & 0xff) / 255.0, ((rgb >> 8) & 0xff) / 255.0, (rgb & 0xff) / 255.0);
        return true;
    }

    private static string ToHex(Vec3 color)
    {
        int r = Math.Clamp((int)Math.Round(color.X * 255.0), 0, 255);
        int g = Math.Clamp((int)Math.Round(color.Y * 255.0), 0, 255);
        int b = Math.Clamp((int)Math.Round(color.Z * 255.0), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
