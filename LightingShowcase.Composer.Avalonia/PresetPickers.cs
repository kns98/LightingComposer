// Text-button preset pickers for the scalar material preset libraries. Each
// picker follows the ColorPresetPicker pattern (buttons in a WrapPanel with a
// Picked event); hosts write the picked values into their editors so
// validation and apply paths stay unchanged. The selected button stays
// highlighted until hosts select another preset or clear the selection.
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LightingShowcase.Composer;

/// <summary>WrapPanel of text buttons raising a typed event when a preset is picked.</summary>
internal abstract class PresetPicker<TPreset> : WrapPanel where TPreset : notnull
{
    private static readonly SolidColorBrush SelectedBorderBrush = new(Color.FromRgb(0x2B, 0x88, 0xD8));

    private readonly Dictionary<TPreset, (Button Button, string Label)> buttons = new();

    public event Action<TPreset>? PresetPicked;

    protected PresetPicker(IEnumerable<TPreset> presets, Func<TPreset, string> label, Func<TPreset, string> tip)
    {
        Orientation = Orientation.Horizontal;
        foreach (TPreset preset in presets)
        {
            TPreset captured = preset;
            string text = label(captured);
            Button button = new()
            {
                Content = text,
                Margin = new Avalonia.Thickness(2),
                Padding = new Avalonia.Thickness(8, 4)
            };
            ToolTip.SetTip(button, tip(captured));
            button.Click += (_, _) => PresetPicked?.Invoke(captured);
            Children.Add(button);
            buttons[captured] = (button, text);
        }
    }

    /// <summary>Highlights the given preset, or clears the highlight when null.</summary>
    public void SetSelected(TPreset? preset)
    {
        foreach ((TPreset key, (Button button, string label)) in buttons)
        {
            if (preset != null && key.Equals(preset))
            {
                button.Content = new TextBlock { Text = label, FontWeight = FontWeight.Bold };
                button.BorderBrush = SelectedBorderBrush;
                button.BorderThickness = new Avalonia.Thickness(2);
            }
            else
            {
                button.Content = label;
                button.ClearValue(Button.BorderBrushProperty);
                button.ClearValue(Button.BorderThicknessProperty);
            }
        }
    }
}

/// <summary>Surface-tab finish presets (metallic/roughness pairs).</summary>
internal sealed class FinishPresetPicker : PresetPicker<FinishPreset>
{
    public FinishPresetPicker()
        : base(FinishPresets.All, static p => p.Name, static p => p.Summary)
    {
    }
}

/// <summary>Surface-tab alpha presets (alpha mode/opacity/cutoff triples).</summary>
internal sealed class AlphaPresetPicker : PresetPicker<AlphaPreset>
{
    public AlphaPresetPicker()
        : base(AlphaPresets.All, static p => p.Name, static p => p.Summary)
    {
    }
}

/// <summary>Light-and-volume-tab glow presets (emission strength/color pairs).</summary>
internal sealed class GlowPresetPicker : PresetPicker<GlowPreset>
{
    public GlowPresetPicker()
        : base(GlowPresets.All, static p => p.Name, static p => p.Summary)
    {
    }
}

/// <summary>Light-and-volume-tab glass presets (transmission/volume/IOR values).</summary>
internal sealed class GlassPresetPicker : PresetPicker<GlassPreset>
{
    public GlassPresetPicker()
        : base(GlassPresets.All, static p => p.Name, static p => p.Summary)
    {
    }
}

/// <summary>Detail-tab effect presets (normal/specular/sheen/anisotropy/iridescence values).</summary>
internal sealed class DetailEffectPresetPicker : PresetPicker<DetailEffectPreset>
{
    public DetailEffectPresetPicker()
        : base(DetailEffectPresets.All, static p => p.Name, static p => p.Summary)
    {
    }
}

/// <summary>Mapping-tab presets (UV source plus transform and wrap values).</summary>
internal sealed class MappingPresetPicker : PresetPicker<MappingPreset>
{
    public MappingPresetPicker()
        : base(MappingPresets.All, static p => p.Name, static p => p.Summary)
    {
    }
}
