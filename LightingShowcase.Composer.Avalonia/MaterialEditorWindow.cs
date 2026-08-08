using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Modeless material editor for the selected object/subtree. Presets, base color,
/// and base-color textures are authored independently from object/primitive geometry.
/// </summary>
internal sealed class MaterialEditorWindow : Window
{
    private readonly ComposerSceneSession session;
    private readonly int objectId;
    private readonly Action onMaterialChanged;
    private readonly Action onClosed;
    private readonly ComboBox presetBox;
    private readonly TextBlock presetSummary;
    private readonly TextBox hexBox;
    private readonly TextBox redBox;
    private readonly TextBox greenBox;
    private readonly TextBox blueBox;
    private readonly Border colorSwatch;
    private readonly TextBox texturePathBox;
    private readonly TextBox tileMetersBox;
    private readonly CheckBox boxProjectionBox;
    private readonly TextBlock materialDetails;
    private readonly TextBlock statusText;
    private bool synchronizingColor;

    public int ObjectId => objectId;

    public MaterialEditorWindow(
        ComposerSceneSession session,
        ComposerMaterialModel model,
        Action onMaterialChanged,
        Action onClosed)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.onMaterialChanged = onMaterialChanged ?? (() => { });
        this.onClosed = onClosed ?? (() => { });
        objectId = model.ObjectId;

        Title = $"Material — {model.ObjectName}";
        Width = 480;
        Height = 660;
        MinWidth = 420;
        MinHeight = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        presetBox = new ComboBox
        {
            ItemsSource = MaterialPresetLibrary.Common,
            SelectedIndex = 0,
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        presetSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 12 };

        hexBox = new TextBox { MinWidth = 105, TextAlignment = TextAlignment.Center };
        redBox = ChannelBox();
        greenBox = ChannelBox();
        blueBox = ChannelBox();
        colorSwatch = new Border
        {
            Width = 64,
            Height = 38,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 128, 128, 128)),
            CornerRadius = new CornerRadius(4)
        };

        texturePathBox = new TextBox { IsReadOnly = true, Watermark = "No base-color texture" };
        tileMetersBox = new TextBox { TextAlignment = TextAlignment.Right, MinWidth = 90 };
        boxProjectionBox = new CheckBox
        {
            Content = "Box-project UVs using real-world tile size",
            IsChecked = true
        };
        materialDetails = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.74, FontSize = 12 };
        statusText = new TextBlock
        {
            Text = "Material changes apply to the selected object and its child meshes.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.76,
            FontSize = 12
        };

        presetBox.SelectionChanged += (_, _) => UpdatePresetSummary();
        redBox.TextChanged += (_, _) => SyncColorFromChannels();
        greenBox.TextChanged += (_, _) => SyncColorFromChannels();
        blueBox.TextChanged += (_, _) => SyncColorFromChannels();
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && TryParseHex(hexBox.Text, out Vec3 color))
            {
                SetColorEditors(color);
                e.Handled = true;
            }
        };
        hexBox.LostFocus += (_, _) =>
        {
            if (TryParseHex(hexBox.Text, out Vec3 color))
                SetColorEditors(color);
        };

        Content = BuildContent();
        LoadModel(model);
        UpdatePresetSummary();
        Closed += (_, _) => this.onClosed();
    }

    public void RefreshFromScene(string message = "Material state refreshed.")
    {
        ComposerMaterialModel? model = session.GetMaterialModel(objectId);
        if (model == null)
        {
            statusText.Text = "The material target no longer exists.";
            return;
        }
        LoadModel(model);
        statusText.Text = message;
    }

    private Control BuildContent()
    {
        StackPanel stack = new() { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = "Material Library",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Choose a PBR preset, set an exact base color, or assign an image texture. Existing image maps are retained when a library preset is applied.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        Grid presetRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        presetRow.Children.Add(presetBox);
        Button applyPreset = NewButton("Apply preset");
        applyPreset.Click += async (_, _) => await ApplyPresetAsync();
        presetRow.Children.Add(applyPreset);
        Grid.SetColumn(applyPreset, 1);
        stack.Children.Add(presetRow);
        stack.Children.Add(presetSummary);

        stack.Children.Add(Heading("Base color"));
        Grid colorTop = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10 };
        colorTop.Children.Add(colorSwatch);
        colorTop.Children.Add(hexBox);
        Grid.SetColumn(hexBox, 1);
        Button applyColor = NewButton("Apply color");
        applyColor.HorizontalAlignment = HorizontalAlignment.Right;
        applyColor.Click += async (_, _) => await ApplyColorAsync();
        colorTop.Children.Add(applyColor);
        Grid.SetColumn(applyColor, 2);
        stack.Children.Add(colorTop);

        Grid rgb = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"), ColumnSpacing = 6 };
        AddRgb(rgb, "R", redBox, 0);
        AddRgb(rgb, "G", greenBox, 2);
        AddRgb(rgb, "B", blueBox, 4);
        stack.Children.Add(rgb);

        stack.Children.Add(Heading("Base-color texture"));
        stack.Children.Add(texturePathBox);
        Grid textureButtons = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
        Button browse = NewButton("Browse…");
        browse.Click += async (_, _) => await BrowseTextureAsync();
        Button clear = NewButton("Clear texture");
        clear.Click += async (_, _) => await ClearTextureAsync();
        textureButtons.Children.Add(browse);
        textureButtons.Children.Add(clear);
        Grid.SetColumn(clear, 1);
        stack.Children.Add(textureButtons);
        stack.Children.Add(boxProjectionBox);
        stack.Children.Add(LabeledControl("Texture tile size (m)", tileMetersBox));
        stack.Children.Add(new TextBlock
        {
            Text = "Box projection tiles the image in scene meters and is useful for architectural/material-scale work. Turn it off to preserve the model's authored UV coordinates.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });

        stack.Children.Add(Heading("Current material"));
        stack.Children.Add(materialDetails);
        stack.Children.Add(statusText);

        Button close = NewButton("Close");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Close();
        stack.Children.Add(close);

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

    private async Task ApplyPresetAsync()
    {
        if (presetBox.SelectedItem is not MaterialPreset preset)
            return;
        await RunEditAsync(
            () => session.ApplyMaterialPreset(objectId, preset),
            $"Applied {preset.Category} — {preset.Name}. Assigned textures were preserved.");
    }

    private async Task ApplyColorAsync()
    {
        if (!TryReadColor(out Vec3 color))
        {
            statusText.Text = "Enter RGB values from 0 to 255 or a hex color such as #C8A060.";
            return;
        }
        await RunEditAsync(() => session.SetObjectBaseColor(objectId, color), $"Base color set to {ToHex(color)}.");
    }

    private async Task BrowseTextureAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            statusText.Text = "The desktop file picker is unavailable.";
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose base-color texture",
                AllowMultiple = false,
                FileTypeFilter = ComposerFileTypes.TexturePickerTypes
            });
        }
        catch (Exception ex)
        {
            statusText.Text = $"Texture picker failed: {ex.Message}";
            return;
        }

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!TryReadTileMeters(out double tileMeters))
        {
            statusText.Text = "Texture tile size must be a number greater than zero meters.";
            return;
        }

        bool boxProjection = boxProjectionBox.IsChecked == true;
        await RunEditAsync(
            () => session.SetObjectTexture(objectId, path, tileMeters, boxProjection),
            boxProjection
                ? $"Texture {Path.GetFileName(path)} applied with {tileMeters:0.######} m box tiling."
                : $"Texture {Path.GetFileName(path)} applied using authored UVs.");
    }

    private async Task ClearTextureAsync()
    {
        await RunEditAsync(() => session.ClearObjectTexture(objectId), "Base-color texture cleared.");
    }

    private async Task RunEditAsync(Func<bool> edit, string successMessage)
    {
        try
        {
            IsEnabled = false;
            bool changed = await Task.Run(edit);
            if (!changed)
            {
                statusText.Text = "The selected object no longer has editable material geometry.";
                return;
            }

            RefreshFromScene(successMessage);
            onMaterialChanged();
        }
        catch (Exception ex)
        {
            statusText.Text = $"Material update failed: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void LoadModel(ComposerMaterialModel model)
    {
        Title = $"Material — {model.ObjectName}";
        SetColorEditors(model.BaseColor);
        texturePathBox.Text = model.TexturePath ?? model.TextureName ?? string.Empty;
        tileMetersBox.Text = model.TextureTileMeters.ToString("0.######", CultureInfo.InvariantCulture);
        if (model.HasStoredTextureProjection)
            boxProjectionBox.IsChecked = model.UsesBoxProjection;
        materialDetails.Text =
            $"Color {ToHex(model.BaseColor)}   Metallic {model.Metallic:0.###}   Roughness {model.Roughness:0.###}\n" +
            $"Transmission {model.Transmission:0.###}   Opacity {model.Alpha:0.###}   Texture {(model.TextureName ?? "none")}";
    }

    private void UpdatePresetSummary()
    {
        if (presetBox.SelectedItem is not MaterialPreset preset)
        {
            presetSummary.Text = string.Empty;
            return;
        }
        presetSummary.Text = $"{preset.Summary}  Metallic {preset.Material.Metallic:0.##}, roughness {preset.Material.Roughness:0.##}.";
    }

    private void SyncColorFromChannels()
    {
        if (synchronizingColor || !TryReadRgb(out Vec3 color))
            return;
        synchronizingColor = true;
        hexBox.Text = ToHex(color);
        colorSwatch.Background = ToBrush(color);
        synchronizingColor = false;
    }

    private void SetColorEditors(Vec3 color)
    {
        synchronizingColor = true;
        redBox.Text = ToByte(color.X).ToString(CultureInfo.InvariantCulture);
        greenBox.Text = ToByte(color.Y).ToString(CultureInfo.InvariantCulture);
        blueBox.Text = ToByte(color.Z).ToString(CultureInfo.InvariantCulture);
        hexBox.Text = ToHex(color);
        colorSwatch.Background = ToBrush(color);
        synchronizingColor = false;
    }

    private bool TryReadColor(out Vec3 color)
    {
        if (TryParseHex(hexBox.Text, out color))
            return true;
        return TryReadRgb(out color);
    }

    private bool TryReadRgb(out Vec3 color)
    {
        color = Vec3.Zero;
        if (!TryByte(redBox.Text, out int r) || !TryByte(greenBox.Text, out int g) || !TryByte(blueBox.Text, out int b))
            return false;
        color = new Vec3(r / 255.0, g / 255.0, b / 255.0);
        return true;
    }

    private bool TryReadTileMeters(out double value)
    {
        string text = tileMetersBox.Text?.Trim() ?? string.Empty;
        bool ok = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                  double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return ok && double.IsFinite(value) && value > 1e-6;
    }

    private static bool TryParseHex(string? text, out Vec3 color)
    {
        color = Vec3.Zero;
        string value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length == 3)
            value = string.Concat(value.Select(c => new string(c, 2)));
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;
        color = new Vec3(((rgb >> 16) & 0xff) / 255.0, ((rgb >> 8) & 0xff) / 255.0, (rgb & 0xff) / 255.0);
        return true;
    }

    private static bool TryByte(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 255;

    private static byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255.0), 0, 255);
    private static string ToHex(Vec3 color) => $"#{ToByte(color.X):X2}{ToByte(color.Y):X2}{ToByte(color.Z):X2}";
    private static IBrush ToBrush(Vec3 color) => new SolidColorBrush(Color.FromRgb(ToByte(color.X), ToByte(color.Y), ToByte(color.Z)));

    private static TextBox ChannelBox() => new() { MinWidth = 58, TextAlignment = TextAlignment.Right };

    private static void AddRgb(Grid grid, string label, Control editor, int column)
    {
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        Grid.SetColumn(text, column);
        grid.Children.Add(editor);
        Grid.SetColumn(editor, column + 1);
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 6, 0, 0)
    };

    private static Control LabeledControl(string label, Control control)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        Grid.SetColumn(control, 1);
        return row;
    }

    private static Button NewButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6),
        MinWidth = 78
    };
}
