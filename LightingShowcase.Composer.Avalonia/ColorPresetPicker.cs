// Clickable swatch grid for the named color preset library. Hosts wire
// PresetPicked into their existing color fields so validation and apply
// paths stay unchanged. Hosts may also highlight the active swatch with
// SetSelected; the highlight stays until changed or cleared.
using Avalonia.Controls;
using Avalonia.Media;

namespace LightingShowcase.Composer;

internal sealed class ColorPresetPicker : WrapPanel
{
    private readonly Dictionary<ColorPreset, Button> swatches = new();

    public event Action<ColorPreset>? PresetPicked;

    public ColorPresetPicker()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        foreach (ColorPreset preset in ColorPresets.All)
        {
            ColorPreset captured = preset;
            Button swatch = new()
            {
                Background = new SolidColorBrush(Color.FromRgb(
                    (byte)(captured.Color.X * 255.0),
                    (byte)(captured.Color.Y * 255.0),
                    (byte)(captured.Color.Z * 255.0))),
                MinWidth = 26,
                MinHeight = 26,
                Margin = new Avalonia.Thickness(2),
                Padding = new Avalonia.Thickness(0)
            };
            ToolTip.SetTip(swatch, captured.Name);
            swatch.Click += (_, _) => PresetPicked?.Invoke(captured);
            Children.Add(swatch);
            swatches[captured] = swatch;
        }
    }

    /// <summary>Highlights the given swatch with a contrasting ring, or clears when null.</summary>
    public void SetSelected(ColorPreset? preset)
    {
        foreach ((ColorPreset key, Button swatch) in swatches)
        {
            if (preset != null && key.Equals(preset))
            {
                double luminance = 0.299 * key.Color.X + 0.587 * key.Color.Y + 0.114 * key.Color.Z;
                swatch.BorderBrush = luminance > 0.5 ? Brushes.Black : Brushes.White;
                swatch.BorderThickness = new Avalonia.Thickness(3);
            }
            else
            {
                swatch.ClearValue(Button.BorderBrushProperty);
                swatch.ClearValue(Button.BorderThicknessProperty);
            }
        }
    }
}
