// Built-in texture-library picker for the Textures tab: category filter,
// preset list, map choice, slot target, and apply button. Hosts wire
// PresetApplied into the existing SetObjectTexture path so decode, undo, and
// cache invalidation are reused untouched.
using Avalonia.Controls;
using Avalonia.Layout;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class TexturePresetPicker : StackPanel
{
    private sealed record PresetItem(TexturePreset Preset)
    {
        /// <summary>Returns the preset name for list display.</summary>
        public override string ToString() => Preset.Name;
    }

    private sealed record MapItem(string Label, string File)
    {
        /// <summary>Returns the map label for combo display.</summary>
        public override string ToString() => Label;
    }

    private sealed record SlotItem(MaterialTextureSlot Slot)
    {
        /// <summary>Returns the slot display label.</summary>
        public override string ToString() => ComposerSceneSession.TextureSlotLabel(Slot);
    }

    public event Func<TexturePreset, MaterialTextureSlot, string, Task>? PresetApplied;

    private readonly ComboBox categoryBox;
    private readonly ListBox presetList;
    private readonly ComboBox mapBox;
    private readonly ComboBox slotBox;

    public TexturePresetPicker()
    {
        Spacing = 6;

        string[] categories = TexturePresetLibrary.All
            .Select(static p => p.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static c => c, StringComparer.Ordinal)
            .ToArray();
        categoryBox = new ComboBox
        {
            Name = "TexturePresetCategory",
            ItemsSource = new[] { "All categories" }.Concat(categories).ToArray(),
            SelectedIndex = 0,
            MinWidth = 170,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(categoryBox, "Filters the texture library list to one material category.");
        categoryBox.SelectionChanged += (_, _) => RefreshList();

        presetList = new ListBox
        {
            Name = "TexturePresetList",
            MinHeight = 110,
            MaxHeight = 170
        };
        ToolTip.SetTip(presetList, "Built-in CC0 texture library. Pick a material, map, and target slot, then press Apply to slot.");
        presetList.SelectionChanged += (_, _) => { SyncSlotToPreset(); RefreshMapList(); };

        mapBox = new ComboBox
        {
            Name = "TexturePresetMap",
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(mapBox, "Which map of the selected material to apply: albedo color, normal detail, or roughness.");

        Grid mapRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        TextBlock mapLabel = new() { Text = "Map:", VerticalAlignment = VerticalAlignment.Center };
        mapRow.Children.Add(mapLabel);
        mapRow.Children.Add(mapBox);
        Grid.SetColumn(mapBox, 1);

        SlotItem[] slots = Enum.GetValues<MaterialTextureSlot>()
            .Select(static slot => new SlotItem(slot))
            .ToArray();
        slotBox = new ComboBox
        {
            Name = "TexturePresetSlot",
            ItemsSource = slots,
            SelectedIndex = 0,
            MinWidth = 190,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(slotBox, "Texture slot the library image is applied to.");

        Grid slotRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        TextBlock slotLabel = new() { Text = "Slot:", VerticalAlignment = VerticalAlignment.Center };
        slotRow.Children.Add(slotLabel);
        slotRow.Children.Add(slotBox);
        Grid.SetColumn(slotBox, 1);

        Button apply = new()
        {
            Name = "TexturePresetApply",
            Content = "Apply to slot",
            Padding = new Avalonia.Thickness(10, 6),
            MinWidth = 78,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ToolTip.SetTip(apply, "Applies the selected library image to the target slot on every targeted object. One undo step covers all targeted objects.");
        apply.Click += async (_, _) => await ApplySelectedAsync();

        Children.Add(categoryBox);
        Children.Add(presetList);
        Children.Add(mapRow);
        Children.Add(slotRow);
        Children.Add(apply);

        RefreshList();
    }

    /// <summary>Rebuilds the preset list for the selected category, keeping the selection when possible.</summary>
    private void RefreshList()
    {
        string? category = categoryBox.SelectedItem as string;
        TexturePreset? selected = (presetList.SelectedItem as PresetItem)?.Preset;
        PresetItem[] items = TexturePresetLibrary.All
            .Where(p => category == null || category == "All categories" || p.Category == category)
            .Select(static p => new PresetItem(p))
            .ToArray();
        presetList.ItemsSource = items;
        PresetItem? keep = selected == null ? null : items.FirstOrDefault(i => i.Preset.Name == selected.Name);
        presetList.SelectedItem = keep ?? items.FirstOrDefault();
        SyncSlotToPreset();
        RefreshMapList();
    }

    /// <summary>Moves the slot target to the selected preset's default slot.</summary>
    private void SyncSlotToPreset()
    {
        if ((presetList.SelectedItem as PresetItem)?.Preset is not TexturePreset preset)
            return;
        if (slotBox.ItemsSource is System.Collections.IEnumerable items)
        {
            foreach (object? item in items)
            {
                if (item is SlotItem slot && slot.Slot == preset.DefaultSlot)
                {
                    slotBox.SelectedItem = item;
                    return;
                }
            }
        }
    }

    /// <summary>Rebuilds the map choice for the selected preset, keeping the map when possible.</summary>
    private void RefreshMapList()
    {
        string? selected = (mapBox.SelectedItem as MapItem)?.Label;
        List<MapItem> items = new();
        if ((presetList.SelectedItem as PresetItem)?.Preset is TexturePreset preset)
        {
            items.Add(new MapItem("Albedo", preset.AlbedoFile));
            if (preset.NormalFile != null)
                items.Add(new MapItem("Normal", preset.NormalFile));
            if (preset.RoughnessFile != null)
                items.Add(new MapItem("Roughness", preset.RoughnessFile));
        }
        mapBox.ItemsSource = items;
        mapBox.SelectedItem = items.FirstOrDefault(i => i.Label == selected) ?? items.FirstOrDefault();
    }

    /// <summary>Raises PresetApplied for the current preset, slot, and map selection.</summary>
    private async Task ApplySelectedAsync()
    {
        if ((presetList.SelectedItem as PresetItem)?.Preset is not TexturePreset preset)
            return;
        if (slotBox.SelectedItem is not SlotItem slot)
            return;
        if ((mapBox.SelectedItem as MapItem)?.File is not string mapFile)
            return;
        if (PresetApplied != null)
            await PresetApplied.Invoke(preset, slot.Slot, mapFile);
    }
}
