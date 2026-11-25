// Clickable swatch grid for the named color preset library. Hosts wire
// PresetPicked into their existing color fields so validation and apply
// paths stay unchanged.
using Avalonia.Controls;
using Avalonia.Media;

namespace LightingShowcase.Composer;

internal sealed class ColorPresetPicker : WrapPanel
{
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
        }
    }
}
