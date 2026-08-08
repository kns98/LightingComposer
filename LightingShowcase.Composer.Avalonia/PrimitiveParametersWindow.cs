using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Modeless parameter editor for procedural mesh primitives. Values are previewed
/// live, while Apply/Close records one undo step for the current edit batch.
/// </summary>
internal sealed class PrimitiveParametersWindow : Window
{
    private readonly ComposerSceneSession session;
    private readonly int objectId;
    private readonly ComposerPrimitiveParameterModel model;
    private readonly Action onPreviewChanged;
    private readonly Action onCommittedOrConverted;
    private readonly Action onClosed;
    private readonly Dictionary<string, Func<double?>> readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer previewTimer;
    private readonly TextBlock statusText;
    private bool suppressCloseCommit;
    private bool previewDirty;
    private bool batchHasChanges;

    public int ObjectId => objectId;

    public PrimitiveParametersWindow(
        ComposerSceneSession session,
        ComposerPrimitiveParameterModel model,
        Action onPreviewChanged,
        Action onCommittedOrConverted,
        Action onClosed)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.onPreviewChanged = onPreviewChanged ?? (() => { });
        this.onCommittedOrConverted = onCommittedOrConverted ?? (() => { });
        this.onClosed = onClosed ?? (() => { });
        objectId = model.ObjectId;

        Title = $"Parameters — {model.PrimitiveName}";
        Width = 390;
        Height = Math.Clamp(230 + model.Parameters.Count * 52, 360, 680);
        MinWidth = 340;
        MinHeight = 300;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        statusText = new TextBlock
        {
            Text = "Scene dimensions are stored in meters (m).",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 12
        };

        previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            PreviewNow();
        };

        Content = BuildContent();
        Closing += (_, _) =>
        {
            previewTimer.Stop();
            if (!suppressCloseCommit)
            {
                PreviewNow();
                if (batchHasChanges)
                {
                    session.CommitPrimitiveParameterEdit(objectId);
                    onCommittedOrConverted();
                }
                else
                {
                    session.CancelPrimitiveParameterEdit(objectId);
                }
            }
        };
        Closed += (_, _) => onClosed();
    }

    /// <summary>
    /// Starts a fresh parameter edit batch after an external object transform.
    /// The visible shape values do not change, but the session baseline must move
    /// forward so closing this modeless window cannot undo the transform.
    /// </summary>
    public void RebaseAfterExternalTransform()
        => RebaseAfterExternalEdit("Object transform applied. Procedural parameters are still editable.");

    /// <summary>
    /// Starts a fresh parameter-edit baseline after another modeless editor changes
    /// the same procedural object (for example a material or texture assignment).
    /// </summary>
    public void RebaseAfterExternalEdit(string message)
    {
        previewTimer.Stop();
        previewDirty = false;
        batchHasChanges = false;
        if (session.BeginPrimitiveParameterEdit(objectId) != null)
            statusText.Text = message;
    }

    private Control BuildContent()
    {
        StackPanel fields = new() { Spacing = 10 };
        fields.Children.Add(new TextBlock
        {
            Text = model.PrimitiveName,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        fields.Children.Add(new TextBlock
        {
            Text = "Edit the procedural geometry directly. Length values are real scene meters. Vertex/edge/face edits or Convert to Mesh make the generated geometry an ordinary mesh.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        foreach (PrimitiveParameterDescriptor descriptor in model.Parameters)
            fields.Children.Add(BuildParameterRow(descriptor));

        fields.Children.Add(statusText);

        Button apply = NewButton("Apply");
        apply.Click += (_, _) =>
        {
            if (!PreviewNow())
                return;
            if (!batchHasChanges)
            {
                statusText.Text = "No parameter changes to apply.";
                return;
            }
            session.CommitPrimitiveParameterEdit(objectId);
            session.BeginPrimitiveParameterEdit(objectId);
            previewDirty = false;
            batchHasChanges = false;
            statusText.Text = "Parameters applied. Continue editing or close the window.";
            onCommittedOrConverted();
        };

        Button revert = NewButton("Revert");
        revert.Click += (_, _) =>
        {
            previewTimer.Stop();
            session.CancelPrimitiveParameterEdit(objectId);
            suppressCloseCommit = true;
            onCommittedOrConverted();
            Close();
        };

        Button convert = NewButton("Convert to Mesh");
        convert.Click += (_, _) =>
        {
            if (!PreviewNow())
                return;
            session.CommitPrimitiveParameterEdit(objectId);
            if (session.ConvertParametricObjectToMesh(objectId))
            {
                suppressCloseCommit = true;
                statusText.Text = "Converted to ordinary mesh geometry.";
                onCommittedOrConverted();
                Close();
            }
        };

        Button close = NewButton("Close");
        close.Click += (_, _) => Close();

        Grid buttons = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 8
        };
        buttons.Children.Add(apply);
        buttons.Children.Add(revert);
        Grid.SetColumn(revert, 1);
        buttons.Children.Add(convert);
        Grid.SetColumn(convert, 3);
        buttons.Children.Add(close);
        Grid.SetColumn(close, 4);
        fields.Children.Add(buttons);

        return new Border
        {
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                Content = fields,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };
    }

    private Control BuildParameterRow(PrimitiveParameterDescriptor descriptor)
    {
        string label = descriptor.Kind == PrimitiveParameterKind.Length
            ? $"{descriptor.Label} ({(string.IsNullOrWhiteSpace(descriptor.UnitLabel) ? "m" : descriptor.UnitLabel)})"
            : descriptor.Label;

        Control editor;
        double current = model.Values.TryGetValue(descriptor.Key, out double value)
            ? descriptor.Normalize(value)
            : descriptor.Minimum;

        switch (descriptor.Kind)
        {
            case PrimitiveParameterKind.Toggle:
            {
                CheckBox check = new() { IsChecked = current >= 0.5, VerticalAlignment = VerticalAlignment.Center };
                check.Click += (_, _) => SchedulePreview();
                readers[descriptor.Key] = () => check.IsChecked == true ? 1.0 : 0.0;
                editor = check;
                break;
            }
            case PrimitiveParameterKind.Choice:
            {
                string[] choices = descriptor.Choices?.ToArray() ?? Array.Empty<string>();
                ComboBox combo = new()
                {
                    ItemsSource = choices,
                    SelectedIndex = Math.Clamp((int)Math.Round(current), 0, Math.Max(0, choices.Length - 1)),
                    MinWidth = 150
                };
                combo.SelectionChanged += (_, _) => SchedulePreview();
                readers[descriptor.Key] = () => Math.Max(0, combo.SelectedIndex);
                editor = combo;
                break;
            }
            default:
            {
                TextBox box = new()
                {
                    Text = FormatNumber(current, descriptor.Kind),
                    MinWidth = 150,
                    TextAlignment = TextAlignment.Right
                };
                box.TextChanged += (_, _) => SchedulePreview();
                readers[descriptor.Key] = () => TryReadNumber(box.Text, descriptor, out double parsed) ? parsed : null;
                editor = box;
                break;
            }
        }

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(editor);
        Grid.SetColumn(editor, 1);
        return row;
    }

    private void SchedulePreview()
    {
        previewDirty = true;
        previewTimer.Stop();
        previewTimer.Start();
    }

    private bool PreviewNow()
    {
        if (!previewDirty)
            return true;

        Dictionary<string, double> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrimitiveParameterDescriptor descriptor in model.Parameters)
        {
            if (!readers.TryGetValue(descriptor.Key, out Func<double?>? reader))
                continue;
            double? value = reader();
            if (!value.HasValue)
            {
                statusText.Text = $"Enter a valid value for {descriptor.Label}.";
                return false;
            }
            values[descriptor.Key] = descriptor.Normalize(value.Value);
        }

        try
        {
            if (!session.PreviewPrimitiveParameters(objectId, values))
            {
                statusText.Text = "The selected object is no longer parameterized.";
                return false;
            }
            previewDirty = false;
            batchHasChanges = true;
            statusText.Text = "Live preview updated. Scene dimensions are meters (m).";
            onPreviewChanged();
            return true;
        }
        catch (Exception ex)
        {
            statusText.Text = $"Parameter update failed: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadNumber(string? text, PrimitiveParameterDescriptor descriptor, out double value)
    {
        string input = text?.Trim() ?? string.Empty;
        bool ok = double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                  double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (!ok || !double.IsFinite(value))
            return false;
        value = descriptor.Normalize(value);
        return true;
    }

    private static string FormatNumber(double value, PrimitiveParameterKind kind) =>
        kind == PrimitiveParameterKind.Integer
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.######", CultureInfo.InvariantCulture);

    private static Button NewButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6),
        MinWidth = 72
    };
}
