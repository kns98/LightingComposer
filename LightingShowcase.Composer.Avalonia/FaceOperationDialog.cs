/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 */
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LightingShowcase.Composer;

internal readonly record struct FaceOperationValues(
    double AmountMeters,
    double SecondaryMeters = 0.0,
    ComposerInsetProfile InsetProfile = ComposerInsetProfile.Square);

// FaceOperationDialog owns temporary Avalonia presentation/edit state. Values become durable only when accepted and
// routed through the relevant session/controller operation, preserving validation and cancellation semantics.
/// <summary>Small numeric dialog used by right-click polygon face operations.</summary>
internal sealed class FaceOperationDialog : Window
{
    private readonly TextBox amountBox;
    private readonly TextBox? secondaryBox;
    private readonly ComboBox? insetProfileBox;
    private readonly bool allowNegative;
    private readonly bool allowSecondaryNegative;
    private readonly bool allowSecondaryZero;
    private readonly TaskCompletionSource<FaceOperationValues?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FaceOperationDialog(
        string operationName,
        string label,
        double defaultMeters,
        bool allowNegative,
        string? secondaryLabel = null,
        double secondaryDefaultMeters = 0.0,
        bool allowSecondaryNegative = false,
        bool allowSecondaryZero = true,
        bool showInsetProfile = false)
    {
        Title = operationName;
        Width = 350;
        Height = showInsetProfile ? 300 : secondaryLabel == null ? 170 : 230;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.allowNegative = allowNegative;
        this.allowSecondaryNegative = allowSecondaryNegative;
        this.allowSecondaryZero = allowSecondaryZero;

        amountBox = new TextBox
        {
            Text = defaultMeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            MinWidth = 170
        };

        StackPanel fields = new() { Spacing = 7 };
        fields.Children.Add(new TextBlock { Text = label });
        fields.Children.Add(amountBox);

        if (!string.IsNullOrWhiteSpace(secondaryLabel))
        {
            secondaryBox = new TextBox
            {
                Text = secondaryDefaultMeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                MinWidth = 170
            };
            fields.Children.Add(new TextBlock { Text = secondaryLabel });
            fields.Children.Add(secondaryBox);
        }

        if (showInsetProfile)
        {
            insetProfileBox = new ComboBox
            {
                MinWidth = 220,
                SelectedIndex = 0,
                ItemsSource = new[]
                {
                    "Square (90° reveal)",
                    "Sloped (two-finger)"
                }
            };
            fields.Children.Add(new TextBlock { Text = "Depth profile" });
            fields.Children.Add(insetProfileBox);
        }

        Button apply = new() { Content = operationName, MinWidth = 95 };
        Button cancel = new() { Content = "Cancel", MinWidth = 80 };
        apply.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                fields,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, apply }
                }
            }
        };

        Closed += (_, _) => completion.TrySetResult(null);
    }

    // ShowForResultAsync shows the dialog modally relative to its owner and returns the typed result chosen by the
    // user; closing/cancelling without acceptance returns null rather than fabricating default values.
    public async Task<FaceOperationValues?> ShowForResultAsync(Window owner)
    {
        Show(owner);
        amountBox.Focus();
        amountBox.SelectAll();
        return await completion.Task;
    }

    // Accept reads and validates the dialog’s current control values; when they form a valid result it closes the
    // dialog with that value, otherwise the dialog remains open so invalid text never reaches the scene operation.
    private void Accept()
    {
        amountBox.Classes.Remove("error");
        secondaryBox?.Classes.Remove("error");

        if (!TryReadMeters(amountBox, allowNegative, allowZero: false, out double amount))
        {
            amountBox.Classes.Add("error");
            return;
        }

        double secondary = 0.0;
        if (secondaryBox != null &&
            !TryReadMeters(secondaryBox, allowSecondaryNegative, allowSecondaryZero, out secondary))
        {
            secondaryBox.Classes.Add("error");
            return;
        }

        ComposerInsetProfile profile = insetProfileBox?.SelectedIndex == 1
            ? ComposerInsetProfile.Sloped
            : ComposerInsetProfile.Square;
        completion.TrySetResult(new FaceOperationValues(amount, secondary, profile));
        Close();
    }

    private static bool TryReadMeters(TextBox box, bool allowNegative, bool allowZero, out double value)
    {
        if (!double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) ||
            !double.IsFinite(value))
        {
            return false;
        }

        if (!allowNegative && value < 0.0)
            return false;
        if (!allowZero && Math.Abs(value) <= 1e-9)
            return false;
        return true;
    }
}
