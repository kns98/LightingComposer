/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 *
 * `RenderSettingsDialog` owns temporary Avalonia presentation/edit state. Values become durable only when
 * accepted and routed through the relevant session/controller operation, preserving validation and cancellation
 * semantics.
 *
 * `ShowForResultAsync` shows the dialog modally relative to its owner and returns the typed result chosen by the
 * user; closing/cancelling without acceptance returns `null` rather than fabricating default values.
 *
 * `Accept` reads and validates the dialog’s current control values; when they form a valid result it closes the
 * dialog with that value, otherwise the dialog remains open so invalid text never reaches the scene operation.
 *
 * `ReadEnabledInt` reads enabled int from the external stream/document, advancing through the format in the order
 * required to resolve references and produce valid internal data.
 *
 * `ReadEnabledDouble` reads enabled double from the external stream/document, advancing through the format in the
 * order required to resolve references and produce valid internal data.
 *
 * `ReadEnabledColor` reads enabled color from the external stream/document, advancing through the format in the
 * order required to resolve references and produce valid internal data.
 *
 * `NewButton` creates a consistently configured button UI/domain object so repeated controls/objects share
 * sizing, alignment, or default behavior.
 *
 * `ClearErrors` removes prior validation messages before a new validation pass so the UI shows only errors that
 * apply to the current control values.
 */
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

/// <summary>
/// General renderer settings dialog. Controls stay visible across all view modes
/// so the layout is predictable, while unsupported controls are disabled and
/// annotated for the selected renderer.
/// </summary>
internal sealed class RenderSettingsDialog : Window
{
    private readonly ComposerRendererKind kind;
    private readonly TextBox widthBox;
    private readonly TextBox heightBox;
    private readonly TextBox samplesBox;
    private readonly TextBox bouncesBox;
    private readonly TextBox fovBox;
    private readonly TextBox exposureBox;
    private readonly TextBox ambientBox;
    private readonly CheckBox shadowsBox;
    private readonly TextBox backgroundTopBox;
    private readonly TextBox backgroundBottomBox;
    private readonly TextBlock validationText;
    private readonly TaskCompletionSource<ComposerRenderOptions?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool accepted;

    public RenderSettingsDialog(
        ComposerRendererKind kind,
        string rendererLabel,
        ComposerRenderOptions current)
    {
        ArgumentNullException.ThrowIfNull(current);
        this.kind = kind;

        Title = $"Render Settings — {rendererLabel}";
        Width = 510;
        Height = 620;
        MinWidth = 470;
        MinHeight = 520;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        widthBox = NumberBox(current.Width);
        heightBox = NumberBox(current.Height);
        samplesBox = NumberBox(current.Samples);
        bouncesBox = NumberBox(current.Bounces);
        fovBox = NumberBox(current.FieldOfViewDegrees);
        exposureBox = NumberBox(current.Exposure);
        ambientBox = NumberBox(current.AmbientStrength);
        shadowsBox = new CheckBox
        {
            IsChecked = current.UseShadows,
            VerticalAlignment = VerticalAlignment.Center
        };
        backgroundTopBox = TextBoxFor(ColorText(current.BackgroundTop));
        backgroundBottomBox = TextBoxFor(ColorText(current.BackgroundBottom));

        // Resolution applies to every renderer. Every other control is enabled
        // only when the selected renderer actually consumes that setting.
        widthBox.IsEnabled = true;
        heightBox.IsEnabled = true;
        samplesBox.IsEnabled = ComposerRenderOptions.SupportsSamples(kind);
        bouncesBox.IsEnabled = ComposerRenderOptions.SupportsBounces(kind);
        fovBox.IsEnabled = ComposerRenderOptions.SupportsFieldOfView(kind);
        exposureBox.IsEnabled = ComposerRenderOptions.SupportsExposure(kind);
        ambientBox.IsEnabled = ComposerRenderOptions.SupportsAmbient(kind);
        shadowsBox.IsEnabled = ComposerRenderOptions.SupportsShadows(kind);
        backgroundTopBox.IsEnabled = ComposerRenderOptions.SupportsBackground(kind);
        backgroundBottomBox.IsEnabled = ComposerRenderOptions.SupportsBackground(kind);

        validationText = new TextBlock
        {
            Text = ModeHelp(kind),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.76,
            FontSize = 12
        };

        Button modeDefaults = NewButton("Mode defaults");
        modeDefaults.Click += (_, _) => Load(ComposerRenderOptions.DefaultsFor(kind));

        Button cliDefaults = NewButton("CLI defaults");
        cliDefaults.Click += (_, _) => Load(ComposerRenderOptions.CommandLineDefaultsFor(kind));

        Button cancel = NewButton("Cancel");
        cancel.Click += (_, _) => Close();

        Button apply = NewButton("Apply");
        apply.Click += (_, _) => Accept();

        Grid buttons = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 8
        };
        buttons.Children.Add(modeDefaults);
        buttons.Children.Add(cliDefaults);
        Grid.SetColumn(cliDefaults, 1);
        buttons.Children.Add(cancel);
        Grid.SetColumn(cancel, 3);
        buttons.Children.Add(apply);
        Grid.SetColumn(apply, 4);

        StackPanel fields = new() { Spacing = 9 };
        fields.Children.Add(new TextBlock
        {
            Text = rendererLabel,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        fields.Children.Add(new TextBlock
        {
            Text = "General render controls. Settings not consumed by the current view mode are disabled rather than silently ignored.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        fields.Children.Add(Section("Output"));
        fields.Children.Add(Field("Width (pixels)", widthBox, "1–32768; final/idle render size"));
        fields.Children.Add(Field("Height (pixels)", heightBox, "1–32768; final/idle render size"));

        fields.Children.Add(Section("Ray / camera"));
        fields.Children.Add(Field("Samples per pixel", samplesBox, Hint("1–4096", ComposerRenderOptions.SupportsSamples(kind))));
        fields.Children.Add(Field("Path bounces", bouncesBox, Hint("0–8", ComposerRenderOptions.SupportsBounces(kind))));
        fields.Children.Add(Field("Field of view (degrees)", fovBox, Hint("1–179", ComposerRenderOptions.SupportsFieldOfView(kind))));

        fields.Children.Add(Section("Display / lighting"));
        fields.Children.Add(Field("Exposure", exposureBox, Hint("0.01–100", ComposerRenderOptions.SupportsExposure(kind))));
        fields.Children.Add(Field("Ambient strength", ambientBox, Hint("0–100", ComposerRenderOptions.SupportsAmbient(kind))));
        fields.Children.Add(Field("Shadows", shadowsBox, Hint("Vulkan compute shadow toggle", ComposerRenderOptions.SupportsShadows(kind))));
        fields.Children.Add(Field("Background top (R,G,B)", backgroundTopBox, Hint("linear RGB, non-negative", ComposerRenderOptions.SupportsBackground(kind))));
        fields.Children.Add(Field("Background bottom (R,G,B)", backgroundBottomBox, Hint("linear RGB, non-negative", ComposerRenderOptions.SupportsBackground(kind))));

        fields.Children.Add(validationText);

        // Keep the settings themselves scrollable while leaving the action
        // buttons permanently visible at the bottom of the dialog.
        ScrollViewer settingsScroll = new()
        {
            Content = fields,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        Border buttonBar = new()
        {
            Padding = new Thickness(16, 10, 16, 16),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = buttons
        };

        Grid layout = new()
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };

        Border scrollHost = new()
        {
            Padding = new Thickness(16, 16, 16, 8),
            Child = settingsScroll
        };

        layout.Children.Add(scrollHost);
        layout.Children.Add(buttonBar);
        Grid.SetRow(buttonBar, 1);

        Content = layout;

        Closed += (_, _) =>
        {
            if (!accepted)
                completion.TrySetResult(null);
        };
    }

    public async Task<ComposerRenderOptions?> ShowForResultAsync(Window owner)
    {
        Show(owner);
        widthBox.Focus();
        widthBox.SelectAll();
        return await completion.Task;
    }

    private void Accept()
    {
        ClearErrors();

        bool valid =
            TryReadInt(widthBox, 1, 32768, out int width) &
            TryReadInt(heightBox, 1, 32768, out int height);

        int samples = ReadEnabledInt(
            samplesBox,
            ComposerRenderOptions.SupportsSamples(kind),
            1, 4096,
            out bool samplesValid);
        int bounces = ReadEnabledInt(
            bouncesBox,
            ComposerRenderOptions.SupportsBounces(kind),
            0, 8,
            out bool bouncesValid);
        double fov = ReadEnabledDouble(
            fovBox,
            ComposerRenderOptions.SupportsFieldOfView(kind),
            1.0, 179.0,
            out bool fovValid);
        double exposure = ReadEnabledDouble(
            exposureBox,
            ComposerRenderOptions.SupportsExposure(kind),
            0.01, 100.0,
            out bool exposureValid);
        double ambient = ReadEnabledDouble(
            ambientBox,
            ComposerRenderOptions.SupportsAmbient(kind),
            0.0, 100.0,
            out bool ambientValid);

        Vec3 backgroundTop = ReadEnabledColor(
            backgroundTopBox,
            ComposerRenderOptions.SupportsBackground(kind),
            out bool topValid);
        Vec3 backgroundBottom = ReadEnabledColor(
            backgroundBottomBox,
            ComposerRenderOptions.SupportsBackground(kind),
            out bool bottomValid);

        valid &= samplesValid & bouncesValid & fovValid & exposureValid & ambientValid & topValid & bottomValid;

        if ((long)width * height * 4L > uint.MaxValue)
        {
            widthBox.Classes.Add("error");
            heightBox.Classes.Add("error");
            valid = false;
        }

        if (!valid)
        {
            validationText.Text = "One or more enabled values are outside the allowed range.";
            validationText.Foreground = Brushes.OrangeRed;
            return;
        }

        ComposerRenderOptions result = new(
            width,
            height,
            samples,
            bounces,
            fov,
            exposure,
            ambient,
            shadowsBox.IsChecked == true,
            backgroundTop,
            backgroundBottom);
        result.Validate();

        accepted = true;
        completion.TrySetResult(result);
        Close();
    }

    private void Load(ComposerRenderOptions options)
    {
        widthBox.Text = options.Width.ToString(CultureInfo.InvariantCulture);
        heightBox.Text = options.Height.ToString(CultureInfo.InvariantCulture);
        samplesBox.Text = options.Samples.ToString(CultureInfo.InvariantCulture);
        bouncesBox.Text = options.Bounces.ToString(CultureInfo.InvariantCulture);
        fovBox.Text = options.FieldOfViewDegrees.ToString("0.##", CultureInfo.InvariantCulture);
        exposureBox.Text = options.Exposure.ToString("0.###", CultureInfo.InvariantCulture);
        ambientBox.Text = options.AmbientStrength.ToString("0.###", CultureInfo.InvariantCulture);
        shadowsBox.IsChecked = options.UseShadows;
        backgroundTopBox.Text = ColorText(options.BackgroundTop);
        backgroundBottomBox.Text = ColorText(options.BackgroundBottom);

        ClearErrors();
        validationText.Text = ModeHelp(kind);
    }

    private int ReadEnabledInt(TextBox box, bool enabled, int min, int max, out bool valid)
    {
        if (!enabled)
        {
            valid = int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int disabledValue);
            return valid ? Math.Clamp(disabledValue, min, max) : min;
        }

        valid = TryReadInt(box, min, max, out int value);
        return value;
    }

    private double ReadEnabledDouble(TextBox box, bool enabled, double min, double max, out bool valid)
    {
        if (!enabled)
        {
            valid = double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double disabledValue) &&
                    double.IsFinite(disabledValue);
            return valid ? Math.Clamp(disabledValue, min, max) : min;
        }

        valid = TryReadDouble(box, min, max, out double value);
        return value;
    }

    private Vec3 ReadEnabledColor(TextBox box, bool enabled, out bool valid)
    {
        if (!TryReadColor(box, out Vec3 value))
        {
            valid = !enabled;
            return ComposerRenderOptions.DefaultBackgroundTop;
        }

        valid = true;
        return value;
    }

    private static string ModeHelp(ComposerRendererKind kind) => kind switch
    {
        ComposerRendererKind.Raster =>
            "Raster uses configurable output resolution. Samples, bounces, FOV, exposure and compute-lighting controls are disabled because this software rasterizer currently uses its own fixed projection/shading path.",
        ComposerRendererKind.VulkanRaster =>
            "Vulkan raster uses configurable output resolution. Ray/path and compute-only display controls are disabled because the current raster pipeline does not consume them.",
        ComposerRendererKind.VulkanCompute =>
            "Vulkan compute supports resolution, samples, bounces, FOV, exposure, ambient strength, shadows and background colors. Interactive camera motion temporarily uses 1 sample / 0 bounces for responsiveness; the configured quality is used for the idle render.",
        ComposerRendererKind.Cpu =>
            "CPU supports resolution, samples, bounces, FOV and exposure. Ambient, shadow and background controls are disabled because the current CPU command-line ray/path tracer does not consume them.",
        _ => "Renderer settings."
    };

    private static string Hint(string supportedText, bool supported) =>
        supported ? supportedText : "Not used by this view mode";

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 5, 0, 0)
    };

    private static Control Field(string label, Control editor, string hint)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,190"),
            ColumnSpacing = 12
        };

        StackPanel labelPanel = new() { Spacing = 1 };
        labelPanel.Children.Add(new TextBlock { Text = label });
        labelPanel.Children.Add(new TextBlock
        {
            Text = hint,
            Opacity = 0.62,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        row.Children.Add(labelPanel);
        row.Children.Add(editor);
        Grid.SetColumn(editor, 1);
        return row;
    }

    private static TextBox NumberBox(double value) =>
        TextBoxFor(value.ToString("0.###", CultureInfo.InvariantCulture));

    private static TextBox TextBoxFor(string text) => new()
    {
        Text = text,
        MinWidth = 180,
        TextAlignment = TextAlignment.Right
    };

    private static Button NewButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6),
        MinWidth = 78
    };

    private static bool TryReadInt(TextBox box, int minimum, int maximum, out int value)
    {
        bool ok = int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                  value >= minimum && value <= maximum;
        if (!ok)
            box.Classes.Add("error");
        return ok;
    }

    private static bool TryReadDouble(TextBox box, double minimum, double maximum, out double value)
    {
        bool ok = double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                  double.IsFinite(value) &&
                  value >= minimum && value <= maximum;
        if (!ok)
            box.Classes.Add("error");
        return ok;
    }

    private static bool TryReadColor(TextBox box, out Vec3 color)
    {
        string[] parts = (box.Text ?? string.Empty)
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        double r = 0.0;
        double g = 0.0;
        double b = 0.0;
        bool ok =
            parts.Length == 3 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out r) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out g) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b) &&
            double.IsFinite(r) && double.IsFinite(g) && double.IsFinite(b) &&
            r >= 0.0 && g >= 0.0 && b >= 0.0;

        color = ok ? new Vec3(r, g, b) : Vec3.Zero;
        if (!ok && box.IsEnabled)
            box.Classes.Add("error");
        return ok;
    }

    private static string ColorText(Vec3 color) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{color.X:0.###},{color.Y:0.###},{color.Z:0.###}");

    private void ClearErrors()
    {
        foreach (TextBox box in new[]
                 {
                     widthBox, heightBox, samplesBox, bouncesBox, fovBox,
                     exposureBox, ambientBox, backgroundTopBox, backgroundBottomBox
                 })
        {
            box.Classes.Remove("error");
        }

        validationText.Foreground = null;
    }
}
