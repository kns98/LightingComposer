// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/MaterialEditorWindow.cs
// Purpose: Floating PBR material editor.
//
// Preset library, numeric PBR controls, RGB and hex color setters, texture slots, and UV transform controls.
// -----------------------------------------------------------------------------

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
/// Modeless material editor for the selected object/subtree. Library presets are
/// convenient starting points, while every renderer-backed scalar PBR property can
/// also be authored directly without changing object/primitive geometry.
/// </summary>
internal sealed class MaterialEditorWindow : Window
{
    private sealed record TextureSlotChoice(MaterialTextureSlot Slot, string Label)
    {
        /// <summary>Returns the display label for this texture slot choice.</summary>
        public override string ToString() => Label;
    }

    private sealed record ProjectionChoice(bool BoxProjection, string Label)
    {
        /// <summary>Returns the display label for this projection choice.</summary>
        public override string ToString() => Label;
    }

    private sealed record SceneMaterialChoice(SceneMaterialEntry Entry)
    {
        /// <summary>Returns the saved material name for display.</summary>
        public override string ToString() => Entry.Name;
    }

    private readonly ComposerSceneSession session;
    private readonly int objectId;
    private readonly int[] targetIds;
    private readonly Action onMaterialChanged;
    private readonly Action onClosed;
    private readonly ComboBox presetBox;
    private readonly ComboBox categoryBox;
    private readonly TextBlock presetSummary;
    private readonly ComboBox sceneMaterialBox;
    private readonly TextBlock sceneMaterialSummary;
    private readonly TextBox hexBox;
    private readonly TextBox redBox;
    private readonly TextBox greenBox;
    private readonly TextBox blueBox;
    private readonly Border colorSwatch;
    private ColorSpectrumPicker? spectrumPicker;

    private readonly TextBox metallicBox;
    private readonly TextBox roughnessBox;
    private readonly TextBox transmissionBox;
    private readonly TextBox opacityBox;
    private readonly TextBox iorBox;
    private readonly TextBox emissionBox;
    private readonly TextBox emissionColorBox;
    private readonly TextBox thicknessBox;
    private readonly TextBox attenuationColorBox;
    private readonly TextBox attenuationDistanceBox;
    private readonly TextBox clearcoatBox;
    private readonly TextBox clearcoatRoughnessBox;
    private readonly TextBox normalScaleBox;
    private readonly TextBox occlusionStrengthBox;
    private readonly TextBox specularBox;
    private readonly TextBox specularColorBox;
    private readonly TextBox sheenColorBox;
    private readonly TextBox sheenRoughnessBox;
    private readonly TextBox anisotropyBox;
    private readonly TextBox anisotropyRotationBox;
    private readonly TextBox iridescenceBox;
    private readonly TextBox iridescenceIorBox;
    private readonly TextBox iridescenceThicknessMinBox;
    private readonly TextBox iridescenceThicknessMaxBox;
    private readonly ComboBox alphaModeBox;
    private readonly TextBox alphaCutoffBox;
    private readonly CheckBox doubleSidedBox;

    private readonly Dictionary<MaterialTextureSlot, TextBox> texturePathBoxes = new();
    private readonly ComboBox mappingSlotBox;
    private readonly ComboBox projectionModeBox;
    private readonly ComboBox uvSetBox;
    private readonly TextBox tileMetersBox;
    private readonly TextBox offsetUBox;
    private readonly TextBox offsetVBox;
    private readonly TextBox scaleUBox;
    private readonly TextBox scaleVBox;
    private readonly TextBox rotationBox;
    private readonly ComboBox wrapUBox;
    private readonly ComboBox wrapVBox;
    private readonly TextBlock materialDetails;
    private readonly TextBlock statusText;
    private bool synchronizingColor;
    private ComposerMaterialModel? currentModel;
    private ColorPresetPicker? colorSwatchPicker;
    private FinishPresetPicker? finishPicker;
    private AlphaPresetPicker? alphaPicker;
    private GlowPresetPicker? glowPicker;
    private GlassPresetPicker? glassPicker;
    private DetailEffectPresetPicker? detailPicker;
    private MappingPresetPicker? mappingPicker;

    public int ObjectId => objectId;

    /// <summary>Returns every object id this editor applies changes to, primary first.</summary>
    public IReadOnlyList<int> TargetObjectIds => targetIds;

    /// <summary>Returns true when this editor already targets exactly the given object ids.</summary>
    public bool TargetsMatch(IEnumerable<int> ids) =>
        targetIds.Length == ids.Count() && targetIds.All(ids.Contains);

    /// <summary>Creates a modeless material editor window for the given material model.</summary>
    public MaterialEditorWindow(
        ComposerSceneSession session,
        ComposerMaterialModel model,
        Action onMaterialChanged,
        Action onClosed,
        IEnumerable<int>? targetIds = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.onMaterialChanged = onMaterialChanged ?? (() => { });
        this.onClosed = onClosed ?? (() => { });
        objectId = model.ObjectId;
        int[] requested = targetIds?.Distinct().ToArray() ?? Array.Empty<int>();
        this.targetIds = requested.Length == 0
            ? new[] { objectId }
            : requested.Contains(objectId)
                ? new[] { objectId }.Concat(requested.Where(id => id != objectId)).ToArray()
                : new[] { objectId }.Concat(requested).ToArray();

        Title = TargetTitle(model.ObjectName);
        Width = 660;
        Height = 900;
        MinWidth = 560;
        MinHeight = 560;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        presetBox = new ComboBox
        {
            ItemsSource = MaterialPresetLibrary.Common,
            SelectedIndex = 0,
            PlaceholderText = "Unassigned",
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        string[] presetCategories = MaterialPresetLibrary.Common
            .Select(static p => p.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static c => c, StringComparer.Ordinal)
            .ToArray();
        categoryBox = new ComboBox
        {
            ItemsSource = new[] { "All categories" }.Concat(presetCategories).ToArray(),
            SelectedIndex = 0,
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        categoryBox.SelectionChanged += (_, _) =>
        {
            string? category = categoryBox.SelectedItem as string;
            presetBox.ItemsSource = MaterialPresetLibrary.Common
                .Where(p => category == "All categories" || p.Category == category)
                .ToArray();
            presetBox.SelectedIndex = 0;
        };
        presetSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 12 };
        sceneMaterialBox = new ComboBox
        {
            PlaceholderText = "No saved materials",
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        sceneMaterialSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72, FontSize = 12 };

        hexBox = new TextBox { Name = "MaterialHex", MinWidth = 105, TextAlignment = TextAlignment.Center };
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

        metallicBox = PropertyBox();
        roughnessBox = PropertyBox();
        transmissionBox = PropertyBox();
        opacityBox = PropertyBox();
        iorBox = PropertyBox();
        emissionBox = PropertyBox();
        emissionColorBox = new TextBox { MinWidth = 100, TextAlignment = TextAlignment.Right, Watermark = "#FFFFFF" };
        thicknessBox = PropertyBox();
        attenuationColorBox = new TextBox { MinWidth = 100, TextAlignment = TextAlignment.Right, Watermark = "#FFFFFF" };
        attenuationDistanceBox = PropertyBox();
        clearcoatBox = PropertyBox();
        clearcoatRoughnessBox = PropertyBox();
        normalScaleBox = PropertyBox();
        occlusionStrengthBox = PropertyBox();
        specularBox = PropertyBox();
        specularColorBox = new TextBox { MinWidth = 100, TextAlignment = TextAlignment.Right, Watermark = "#FFFFFF" };
        sheenColorBox = new TextBox { MinWidth = 100, TextAlignment = TextAlignment.Right, Watermark = "#000000" };
        sheenRoughnessBox = PropertyBox();
        anisotropyBox = PropertyBox();
        anisotropyRotationBox = PropertyBox();
        iridescenceBox = PropertyBox();
        iridescenceIorBox = PropertyBox();
        iridescenceThicknessMinBox = PropertyBox();
        iridescenceThicknessMaxBox = PropertyBox();
        alphaModeBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<MaterialAlphaMode>(),
            MinWidth = 116,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        alphaCutoffBox = PropertyBox();
        doubleSidedBox = new CheckBox { Content = "Render both sides" };

        foreach (MaterialTextureSlot slot in Enum.GetValues<MaterialTextureSlot>())
            texturePathBoxes[slot] = new TextBox { IsReadOnly = true, Watermark = $"No {ComposerSceneSession.TextureSlotLabel(slot).ToLowerInvariant()} texture" };

        TextureSlotChoice[] mappingSlots = Enum.GetValues<MaterialTextureSlot>()
            .Select(slot => new TextureSlotChoice(slot, ComposerSceneSession.TextureSlotLabel(slot)))
            .ToArray();
        mappingSlotBox = new ComboBox { ItemsSource = mappingSlots, SelectedIndex = 0, MinWidth = 180 };
        projectionModeBox = new ComboBox
        {
            ItemsSource = new[]
            {
                new ProjectionChoice(false, "Authored / current UVs"),
                new ProjectionChoice(true, "Box projection (meters)")
            },
            SelectedIndex = 0,
            MinWidth = 190
        };
        uvSetBox = new ComboBox
        {
            ItemsSource = new[] { "Current stored UV channel" },
            SelectedIndex = 0,
            IsEnabled = false,
            MinWidth = 190
        };
        tileMetersBox = new TextBox { TextAlignment = TextAlignment.Right, MinWidth = 90 };
        offsetUBox = PropertyBox();
        offsetVBox = PropertyBox();
        scaleUBox = PropertyBox();
        scaleVBox = PropertyBox();
        rotationBox = PropertyBox();
        wrapUBox = new ComboBox { ItemsSource = Enum.GetValues<TextureAddressMode>(), MinWidth = 130 };
        wrapVBox = new ComboBox { ItemsSource = Enum.GetValues<TextureAddressMode>(), MinWidth = 130 };
        materialDetails = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.74, FontSize = 12 };
        statusText = new TextBlock
        {
            Text = this.targetIds.Length > 1
                ? $"Material changes apply to all {this.targetIds.Length} selected objects. Shown values come from {model.ObjectName}."
                : "Material changes apply to the selected object and its child meshes.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.76,
            FontSize = 12
        };

        presetBox.SelectionChanged += (_, _) => UpdatePresetSummary();
        sceneMaterialBox.SelectionChanged += (_, _) => UpdateSceneMaterialSummary();
        mappingSlotBox.SelectionChanged += (_, _) => { LoadSelectedTextureMapping(); SyncMappingHighlight(); };
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

        // Tooltips go on before layout so row builders can mirror each editor
        // tooltip onto its label. The spectrum picker is tipped inline where
        // it is created because it does not exist until BuildContent runs.
        ApplyTooltips();
        Content = BuildContent();
        LoadModel(model);
        UpdatePresetSummary();
        Closed += (_, _) => this.onClosed();
    }

    /// <summary>Sets a tooltip on the given control and returns it for inline use.</summary>
    private static T Tip<T>(T control, string text) where T : Control
    {
        ToolTip.SetTip(control, text);
        return control;
    }

    /// <summary>Attaches detailed help tooltips to every editor control.</summary>
    private void ApplyTooltips()
    {
        Tip(categoryBox, "Filters the library preset list to one material category.");
        Tip(sceneMaterialBox, "Saved scene materials. Pick a material saved with Save as… and press Apply to use it on every targeted object.");
        Tip(presetBox, "Library preset picker. Choose a starting material (metal, plastic, glass, …) and press Apply preset. The summary line shows its metallic and roughness values. Applying a preset overwrites the base color and resets extended PBR values to the preset; assigned texture maps, the light link, and double-sided rendering are preserved.");
        Tip(hexBox, "Base color as a hex triplet such as #C8A060. Press Enter or leave the box to sync the RGB editors and pickers below, then press Apply color to write it to the material.");
        Tip(redBox, "Red channel 0–255. Syncs with the hex box and pickers; press Apply color to write the combined color.");
        Tip(greenBox, "Green channel 0–255. Syncs with the hex box and pickers; press Apply color to write the combined color.");
        Tip(blueBox, "Blue channel 0–255. Syncs with the hex box and pickers; press Apply color to write the combined color.");
        Tip(colorSwatch, "Preview of the base color currently entered. It updates as you type or pick; press Apply color to write it to the material.");
        Tip(metallicBox, "Metallic factor 0–1. 0 behaves as a dielectric (plastic, wood) with colored diffuse response; 1 behaves as a metal with tinted reflections and almost no diffuse. Values between blend the two.");
        Tip(roughnessBox, "Surface roughness 0.02–1. Low values give sharp mirror-like reflections; high values spread reflections into a soft matte response. The 0.02 minimum keeps specular math stable.");
        Tip(transmissionBox, "Transmission factor 0–1. 0 is fully opaque; 1 passes light through the surface, using thickness, attenuation, and IOR for glass-like refraction.");
        Tip(opacityBox, "Opacity (alpha) 0–1. 1 is fully opaque; lower values fade the surface when Alpha mode is Blend, or cut pixels out when it is Mask.");
        Tip(iorBox, "Index of refraction 1–2.333. Controls how much transmitted light bends: 1 means no bending, 1.5 is about glass, and higher values bend more. Only affects transmissive materials.");
        Tip(emissionBox, "Emission strength 0–100000. 0 emits no light; higher values make the surface glow with the emission color and add light to the render.");
        Tip(emissionColorBox, "Emission color as a hex triplet such as #FFFFFF. Tints the light the surface emits; emission strength scales its brightness.");
        Tip(thicknessBox, "Refraction thickness in meters (0 or more). Simulates how far transmitted light travels inside the material for absorption and volume tinting.");
        Tip(attenuationColorBox, "Attenuation color as a hex triplet such as #FFFFFF. Tints transmitted light over distance — the deep color seen through thick glass or liquid.");
        Tip(attenuationDistanceBox, "Attenuation distance in meters (0 or more). Distance over which transmitted light falls toward the attenuation color; larger values absorb less.");
        Tip(clearcoatBox, "Clearcoat layer strength 0–1. Adds a glossy lacquer coat over the base material, like car paint or polished wood.");
        Tip(clearcoatRoughnessBox, "Clearcoat layer roughness 0–1. Low values give a sharp showroom shine on the coat; high values dull it.");
        Tip(normalScaleBox, "Normal-map strength −8–8. Scales the bump detail sampled from the normal map; negative values invert the relief.");
        Tip(occlusionStrengthBox, "Ambient-occlusion strength 0–1. Scales how much the occlusion map darkens crevices and contact areas.");
        Tip(specularBox, "Specular intensity 0–1. Scales dielectric reflectivity; use together with specular color for fine highlight control.");
        Tip(specularColorBox, "Specular tint as a hex triplet such as #FFFFFF. Tints dielectric reflections; metals ignore it in favor of the base color.");
        Tip(sheenColorBox, "Sheen tint as a hex triplet. Colors the soft fabric-like retroreflection layer on cloth, velvet, and fuzzy surfaces.");
        Tip(sheenRoughnessBox, "Sheen roughness 0–1. Low values give a tight silky highlight; high values spread the sheen broadly.");
        Tip(anisotropyBox, "Anisotropy strength 0–1. Stretches highlights along one direction for brushed metal, hair, and satin.");
        Tip(anisotropyRotationBox, "Anisotropy direction in radians. Rotates the stretched-highlight direction around the surface normal.");
        Tip(iridescenceBox, "Iridescence strength 0–1. Adds thin-film rainbow interference like soap bubbles and oil slicks.");
        Tip(iridescenceIorBox, "Iridescence film IOR (1 or more). Refractive index of the thin film; shifts which rainbow hues appear.");
        Tip(iridescenceThicknessMinBox, "Minimum film thickness in nanometers. Thinner films shift the interference toward violet and blue.");
        Tip(iridescenceThicknessMaxBox, "Maximum film thickness in nanometers. Thicker films shift the interference toward red; min and max define the range across the surface.");
        Tip(alphaModeBox, "Alpha mode. Opaque ignores opacity, Mask discards pixels below the cutoff, and Blend fades smoothly using opacity.");
        Tip(alphaCutoffBox, "Alpha cutoff 0–1 for Mask mode. Pixels with opacity below this value are discarded; used for cutout foliage and grilles.");
        Tip(doubleSidedBox, "Render both sides. When checked, back faces are drawn too, so open or single-sided geometry stays visible from behind. Double-sided is the default for new materials.");
        foreach ((MaterialTextureSlot slot, TextBox pathBox) in texturePathBoxes)
            Tip(pathBox, $"{ComposerSceneSession.TextureSlotLabel(slot)} texture display. Shows the image file currently assigned to this slot, or the empty watermark when none is assigned. Use Browse to assign one and Clear to remove it.");
        Tip(mappingSlotBox, "Mapping target. Chooses which texture slot the UV source, tile size, offset, scale, rotation, and wrap modes below apply to.");
        Tip(projectionModeBox, "UV source. Authored/current UVs keep the coordinates stored on the mesh; box projection regenerates the shared UV channel with real-world meter tiling.");
        Tip(uvSetBox, "UV set. Composer currently keeps one UV channel per mesh; per-face editing and multiple stored sets belong in the future UV Editor.");
        Tip(tileMetersBox, "Box tile size in meters. World-space size of one texture repeat when box projection is active; smaller values repeat the image more often.");
        Tip(offsetUBox, "Offset U. Shifts the image along the U axis in UV units; 1.0 moves a full tile.");
        Tip(offsetVBox, "Offset V. Shifts the image along the V axis in UV units; 1.0 moves a full tile.");
        Tip(scaleUBox, "Scale U. Multiplies U coordinates; values above 1 repeat the image and values below 1 zoom into it. Must be finite and non-zero.");
        Tip(scaleVBox, "Scale V. Multiplies V coordinates; values above 1 repeat the image and values below 1 zoom into it. Must be finite and non-zero.");
        Tip(rotationBox, "Rotation in degrees. Spins the target slot image around the UV origin.");
        Tip(wrapUBox, "Wrap U. Chooses what happens outside 0–1: Repeat tiles the image, mirrored variants flip each tile, Clamp stretches the edge pixels.");
        Tip(wrapVBox, "Wrap V. Chooses what happens outside 0–1: Repeat tiles the image, mirrored variants flip each tile, Clamp stretches the edge pixels.");
        Tip(materialDetails, "Current material summary for the primary object: base color, key PBR values, and how many texture maps are assigned.");
    }

    /// <summary>Reloads the editor controls from the current scene material.</summary>
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

    /// <summary>Builds the tabbed editor content with a persistent header and footer.</summary>
    private Control BuildContent()
    {
        Grid layout = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };

        StackPanel header = new() { Spacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = "Material",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Choose a library preset as a starting point, then override its PBR properties directly. Base color and image texture remain independent controls.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });
        layout.Children.Add(header);

        TabControl tabs = new() { Name = "MaterialTabs" };
        tabs.Items.Add(new TabItem { Header = "Library", Content = TabScroll(BuildLibraryTab()) });
        tabs.Items.Add(new TabItem { Header = "Color", Content = TabScroll(BuildColorTab()) });
        tabs.Items.Add(new TabItem { Header = "Surface", Content = TabScroll(BuildSurfaceTab()) });
        tabs.Items.Add(new TabItem { Header = "Light & Volume", Content = TabScroll(BuildLightVolumeTab()) });
        tabs.Items.Add(new TabItem { Header = "Detail", Content = TabScroll(BuildDetailTab()) });
        tabs.Items.Add(new TabItem { Header = "Textures", Content = TabScroll(BuildTexturesTab()) });
        tabs.Items.Add(new TabItem { Header = "Mapping", Content = TabScroll(BuildMappingTab()) });
        tabs.SelectedIndex = 0;
        layout.Children.Add(tabs);
        Grid.SetRow(tabs, 1);

        StackPanel footer = new() { Spacing = 6 };
        footer.Children.Add(Heading("Current material"));
        footer.Children.Add(materialDetails);
        footer.Children.Add(statusText);
        Button close = Tip(NewButton("Close"), "Closes the material editor. Changes were already applied when you pressed each Apply button; nothing is lost by closing.");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Close();
        footer.Children.Add(close);
        layout.Children.Add(footer);
        Grid.SetRow(footer, 2);

        return new Border
        {
            Padding = new Thickness(16),
            Child = layout
        };
    }

    /// <summary>Creates a tab-page stack with the standard section spacing.</summary>
    private static StackPanel NewTabStack() => new() { Spacing = 12 };

    /// <summary>Wraps tab content in a vertical scroll viewer.</summary>
    private static Control TabScroll(Control content) => new ScrollViewer
    {
        Content = content,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };

    /// <summary>Creates a labeled preset-picker section for a property tab.</summary>
    private static Control PresetSection(string label, Control picker)
    {
        StackPanel section = new() { Spacing = 4 };
        section.Children.Add(new TextBlock { Text = label, Opacity = 0.72, FontSize = 12 });
        section.Children.Add(picker);
        return section;
    }

    /// <summary>Builds the Library tab with its category filter and preset picker.</summary>
    private Control BuildLibraryTab()
    {
        StackPanel stack = NewTabStack();
        stack.Children.Add(Heading("Library preset"));
        stack.Children.Add(LabeledControl("Category", categoryBox));
        Grid presetRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        presetRow.Children.Add(presetBox);
        Button applyPreset = Tip(NewButton("Apply preset"), "Applies the selected library preset to every targeted object. Base color is overwritten and extended PBR values reset to the preset; texture maps, the light link, and double-sided rendering are preserved. One undo step covers all targeted objects.");
        applyPreset.Click += async (_, _) => await ApplyPresetAsync();
        presetRow.Children.Add(applyPreset);
        Grid.SetColumn(applyPreset, 1);
        stack.Children.Add(presetRow);
        stack.Children.Add(presetSummary);
        stack.Children.Add(new TextBlock
        {
            Text = "Applying a preset overwrites the base color and resets extended PBR values (IOR, clearcoat, sheen, …) to the preset. Texture maps, the light link, and double-sided rendering are preserved.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        stack.Children.Add(Heading("Scene materials"));
        stack.Children.Add(new TextBlock
        {
            Text = "Save the current editors as a named material stored in this scene file, then re-apply it to any object. Saved materials capture base color and PBR values; texture assignments stay per-object.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        Grid sceneRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        sceneRow.Children.Add(sceneMaterialBox);
        Button applySceneMaterial = Tip(NewButton("Apply"), "Applies the selected saved material to every targeted object, like a library preset. Base color is overwritten; texture maps, the light link, and double-sided rendering are preserved. One undo step covers all targeted objects.");
        applySceneMaterial.Click += async (_, _) => await ApplySceneMaterialAsync();
        sceneRow.Children.Add(applySceneMaterial);
        Grid.SetColumn(applySceneMaterial, 1);
        stack.Children.Add(sceneRow);
        stack.Children.Add(sceneMaterialSummary);
        Button saveAs = Tip(NewButton("Save as…"), "Saves the current dialog values as a named material in this scene file so it can be re-applied later.");
        saveAs.HorizontalAlignment = HorizontalAlignment.Right;
        saveAs.Click += async (_, _) => await SaveSceneMaterialAsync();
        stack.Children.Add(saveAs);
        return stack;
    }

    /// <summary>Builds the Color tab with its hex, RGB, spectrum, and swatch editors.</summary>
    private Control BuildColorTab()
    {
        StackPanel stack = NewTabStack();
        stack.Children.Add(Heading("Base color"));
        Grid colorTop = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10 };
        colorTop.Children.Add(colorSwatch);
        colorTop.Children.Add(hexBox);
        Grid.SetColumn(hexBox, 1);
        Button applyColor = Tip(NewButton("Apply color"), "Writes the entered base color to every targeted object, replacing each object's previous base color. Textures and PBR properties are untouched. One undo step covers all targeted objects.");
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
        spectrumPicker = Tip(new ColorSpectrumPicker { Name = "MaterialSpectrum" }, "Spectrum color picker. Click or drag to choose a hue; the choice fills the hex and RGB editors above. Press Apply color to write it to the material.");
        spectrumPicker.ColorPicked += color =>
            SetColorEditors(new Vec3(color.R / 255.0, color.G / 255.0, color.B / 255.0));
        stack.Children.Add(spectrumPicker);
        colorSwatchPicker = Tip(new ColorPresetPicker { Name = "MaterialPresets" }, "Named color preset library. Click a swatch to load its color into the hex and RGB editors above, then press Apply color to write it to the material.");
        colorSwatchPicker.PresetPicked += preset =>
        {
            SetColorEditors(preset.Color);
            colorSwatchPicker?.SetSelected(preset);
            SyncPresetSelectionFromDialog();
        };
        stack.Children.Add(colorSwatchPicker);
        foreach (TextBox box in new[] { hexBox, redBox, greenBox, blueBox })
            box.TextChanged += (_, _) => RefreshPropertyHighlightsFromDialog();
        return stack;
    }

    /// <summary>Creates an Apply button that writes every property tab through the shared validation path.</summary>
    private Button NewApplyPropertiesButton()
    {
        Button applyProperties = Tip(NewButton("Apply properties"), "Writes the direct properties on every property tab (Surface, Light & Volume, Detail) to all targeted objects, preserving each object's base color and texture maps. One undo step covers all targeted objects.");
        applyProperties.HorizontalAlignment = HorizontalAlignment.Right;
        applyProperties.Click += async (_, _) => await ApplyPropertiesAsync();
        return applyProperties;
    }

    /// <summary>Builds the Surface tab with its finish and alpha presets.</summary>
    private Control BuildSurfaceTab()
    {
        StackPanel stack = NewTabStack();
        stack.Children.Add(Heading("Surface"));
        stack.Children.Add(new TextBlock
        {
            Text = "Values are written directly to the material used by the raster and ray renderers. 0–1 values use normalized PBR units; thickness and attenuation distance are meters.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        stack.Children.Add(TwoPropertyRow("Metallic (0–1)", metallicBox, "Roughness (0.02–1)", roughnessBox));
        stack.Children.Add(TwoPropertyRow("Transmission (0–1)", transmissionBox, "Opacity (0–1)", opacityBox));
        stack.Children.Add(LabeledControl("IOR (1–2.333)", iorBox));
        stack.Children.Add(TwoPropertyRow("Alpha mode", alphaModeBox, "Alpha cutoff (0–1)", alphaCutoffBox));
        stack.Children.Add(doubleSidedBox);
        finishPicker = Tip(new FinishPresetPicker(), "Finish presets. Click one to load its metallic and roughness values into the editors above, then press Apply properties to write them to the material.");
        finishPicker.PresetPicked += preset =>
        {
            SetFinishEditors(preset);
            finishPicker?.SetSelected(preset);
            SyncPresetSelectionFromDialog();
        };
        stack.Children.Add(PresetSection("Finish presets", finishPicker));
        alphaPicker = Tip(new AlphaPresetPicker(), "Alpha presets. Click one to load its alpha mode, opacity, and cutoff into the editors above, then press Apply properties to write them to the material.");
        alphaPicker.PresetPicked += preset =>
        {
            SetAlphaEditors(preset);
            alphaPicker?.SetSelected(preset);
            SyncPresetSelectionFromDialog();
        };
        stack.Children.Add(PresetSection("Alpha presets", alphaPicker));
        foreach (TextBox box in new[] { metallicBox, roughnessBox, transmissionBox, opacityBox, iorBox, alphaCutoffBox })
            box.TextChanged += (_, _) => RefreshPropertyHighlightsFromDialog();
        alphaModeBox.SelectionChanged += (_, _) => RefreshPropertyHighlightsFromDialog();
        stack.Children.Add(NewApplyPropertiesButton());
        return stack;
    }

    /// <summary>Builds the Light and Volume tab with its glow and glass presets.</summary>
    private Control BuildLightVolumeTab()
    {
        StackPanel stack = NewTabStack();
        stack.Children.Add(Heading("Light & Volume"));
        stack.Children.Add(new TextBlock
        {
            Text = "Emission, volume absorption, and clearcoat. Glow and glass presets fill these editors; Apply writes the properties on every tab at once.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        stack.Children.Add(LabeledControl("Emission strength", emissionBox));
        stack.Children.Add(LabeledControl("Emission color", emissionColorBox));
        stack.Children.Add(TwoPropertyRow("Thickness (m)", thicknessBox, "Attenuation distance (m)", attenuationDistanceBox));
        stack.Children.Add(LabeledControl("Attenuation color", attenuationColorBox));
        stack.Children.Add(TwoPropertyRow("Clearcoat (0–1)", clearcoatBox, "Clearcoat roughness (0–1)", clearcoatRoughnessBox));
        glowPicker = Tip(new GlowPresetPicker(), "Glow presets. Click one to load its emission strength and color into the editors above, then press Apply properties to write them to the material.");
        glowPicker.PresetPicked += preset =>
        {
            SetGlowEditors(preset);
            glowPicker?.SetSelected(preset);
            SyncPresetSelectionFromDialog();
        };
        stack.Children.Add(PresetSection("Glow presets", glowPicker));
        glassPicker = Tip(new GlassPresetPicker(), "Glass presets. Click one to load its transmission, volume, and IOR values into the editors (transmission and IOR live on the Surface tab), then press Apply properties to write them to the material.");
        glassPicker.PresetPicked += preset =>
        {
            SetGlassEditors(preset);
            glassPicker?.SetSelected(preset);
            SyncPresetSelectionFromDialog();
        };
        stack.Children.Add(PresetSection("Glass presets", glassPicker));
        foreach (TextBox box in new[] { emissionBox, emissionColorBox, thicknessBox, attenuationColorBox, attenuationDistanceBox, clearcoatBox, clearcoatRoughnessBox })
            box.TextChanged += (_, _) => RefreshPropertyHighlightsFromDialog();
        stack.Children.Add(NewApplyPropertiesButton());
        return stack;
    }

    /// <summary>Builds the Detail tab with its effect presets.</summary>
    private Control BuildDetailTab()
    {
        StackPanel stack = NewTabStack();
        stack.Children.Add(Heading("Detail"));
        stack.Children.Add(new TextBlock
        {
            Text = "Normal, occlusion, specular, sheen, anisotropy, and iridescence. Effect presets fill these editors; Apply writes the properties on every tab at once.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        stack.Children.Add(TwoPropertyRow("Normal scale (-8–8)", normalScaleBox, "Occlusion strength (0–1)", occlusionStrengthBox));
        stack.Children.Add(TwoPropertyRow("Specular (0–1)", specularBox, "Sheen roughness (0–1)", sheenRoughnessBox));
        stack.Children.Add(LabeledControl("Specular color", specularColorBox));
        stack.Children.Add(LabeledControl("Sheen color", sheenColorBox));
        stack.Children.Add(TwoPropertyRow("Anisotropy (0–1)", anisotropyBox, "Anisotropy rotation (rad)", anisotropyRotationBox));
        stack.Children.Add(TwoPropertyRow("Iridescence (0–1)", iridescenceBox, "Iridescence IOR (≥1)", iridescenceIorBox));
        stack.Children.Add(TwoPropertyRow("Iridescence min (nm)", iridescenceThicknessMinBox, "Iridescence max (nm)", iridescenceThicknessMaxBox));
        detailPicker = Tip(new DetailEffectPresetPicker(), "Effect presets. Click one to load its detail values into the editors above, then press Apply properties to write them to the material.");
        detailPicker.PresetPicked += preset =>
        {
            SetDetailEffectEditors(preset);
            detailPicker?.SetSelected(preset);
            SyncPresetSelectionFromDialog();
        };
        stack.Children.Add(PresetSection("Effect presets", detailPicker));
        foreach (TextBox box in new[] { normalScaleBox, occlusionStrengthBox, specularBox, specularColorBox, sheenColorBox, sheenRoughnessBox, anisotropyBox, anisotropyRotationBox, iridescenceBox, iridescenceIorBox, iridescenceThicknessMinBox, iridescenceThicknessMaxBox })
            box.TextChanged += (_, _) => RefreshPropertyHighlightsFromDialog();
        stack.Children.Add(NewApplyPropertiesButton());
        return stack;
    }

    /// <summary>Builds the Textures tab with its slot rows and built-in texture library.</summary>
    private Control BuildTexturesTab()
    {
        StackPanel stack = NewTabStack();
        stack.Children.Add(Heading("Texture maps"));
        stack.Children.Add(new TextBlock
        {
            Text = "Assign renderer-backed image maps independently. All slots use the mesh's current UV channel, while each image keeps its own offset, scale, rotation, and U/V address modes.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        foreach (MaterialTextureSlot slot in Enum.GetValues<MaterialTextureSlot>())
            stack.Children.Add(BuildTextureSlotRow(slot));
        stack.Children.Add(Heading("Texture library"));
        stack.Children.Add(new TextBlock
        {
            Text = "Built-in CC0 starter images (Poly Haven, 1K). Pick a material, map, and target slot, then apply — the same decode, undo, and mapping path as Browse.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        TexturePresetPicker texturePicker = new();
        texturePicker.PresetApplied += ApplyTexturePresetAsync;
        stack.Children.Add(texturePicker);
        return stack;
    }

    /// <summary>Builds the Mapping tab with its UV controls and mapping presets.</summary>
    private Control BuildMappingTab()
    {
        StackPanel stack = NewTabStack();
        stack.Children.Add(Heading("Texture mapping"));
        stack.Children.Add(LabeledControl("Mapping target", mappingSlotBox));
        stack.Children.Add(LabeledControl("UV source", projectionModeBox));
        stack.Children.Add(LabeledControl("UV set", uvSetBox));
        stack.Children.Add(LabeledControl("Box tile size (m)", tileMetersBox));
        stack.Children.Add(TwoPropertyRow("Offset U", offsetUBox, "Offset V", offsetVBox));
        stack.Children.Add(TwoPropertyRow("Scale U", scaleUBox, "Scale V", scaleVBox));
        stack.Children.Add(LabeledControl("Rotation (degrees)", rotationBox));
        stack.Children.Add(TwoPropertyRow("Wrap U", wrapUBox, "Wrap V", wrapVBox));
        mappingPicker = Tip(new MappingPresetPicker(), "Mapping presets. Click one to load its UV source, tile size, transform, and wrap modes into the editors above, then press Apply mapping to write them to the target slot.");
        mappingPicker.PresetPicked += preset =>
        {
            SetMappingEditors(preset);
            mappingPicker?.SetSelected(preset);
        };
        stack.Children.Add(PresetSection("Mapping presets", mappingPicker));
        foreach (TextBox box in new[] { tileMetersBox, offsetUBox, offsetVBox, scaleUBox, scaleVBox, rotationBox })
            box.TextChanged += (_, _) => RefreshMappingHighlightFromDialog();
        projectionModeBox.SelectionChanged += (_, _) => RefreshMappingHighlightFromDialog();
        wrapUBox.SelectionChanged += (_, _) => RefreshMappingHighlightFromDialog();
        wrapVBox.SelectionChanged += (_, _) => RefreshMappingHighlightFromDialog();
        Button applyMapping = Tip(NewButton("Apply mapping"), "Applies the UV source plus offset, scale, rotation, and wrap modes to the target slot on every targeted object. One undo step covers all targeted objects.");
        applyMapping.HorizontalAlignment = HorizontalAlignment.Right;
        applyMapping.Click += async (_, _) => await ApplyTextureMappingAsync();
        stack.Children.Add(applyMapping);
        stack.Children.Add(new TextBlock
        {
            Text = "Authored/current UVs leave the UV coordinates currently stored on the mesh unchanged; parameterized primitives regenerate their authored UVs when switching back from box projection. Box projection regenerates the shared triangle UV channel using real-world meter tiling. Imported models currently retain one UV channel in Composer, so an earlier imported UV layout cannot be reconstructed after it has been box-projected without undo/reload. Per-face UV editing and multiple stored UV sets belong in the future UV Editor.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        return stack;
    }

    /// <summary>Applies the selected library preset to the edited object.</summary>
    private async Task ApplyPresetAsync()
    {
        if (presetBox.SelectedItem is not MaterialPreset preset)
        {
            statusText.Text = "Choose a library preset to apply.";
            return;
        }
        await RunEditAsync(
            () => session.ApplyMaterialPreset(targetIds, preset) > 0,
            $"Applied {preset.Category} — {preset.Name}. Base color overwritten and direct-property fields refreshed; assigned textures were preserved.");
    }

    /// <summary>Saves the current dialog values as a named scene-library material.</summary>
    private async Task SaveSceneMaterialAsync()
    {
        if (!TryReadColor(out Vec3 color))
        {
            statusText.Text = "Enter RGB values from 0 to 255 or a hex color such as #C8A060.";
            return;
        }
        if (!TryReadMaterialProperties(out ComposerMaterialProperties? properties, out string error))
        {
            statusText.Text = error;
            return;
        }
        string? name = await TextPromptDialog.AskAsync(this, "Save material", "Name this material:");
        if (string.IsNullOrWhiteSpace(name))
            return;
        try
        {
            SceneMaterialEntry entry = session.AddSceneMaterial(name, color, properties!);
            RefreshSceneMaterialList(entry.Name);
            statusText.Text = $"Material '{entry.Name}' saved in the scene.";
            onMaterialChanged();
        }
        catch (ArgumentException ex)
        {
            statusText.Text = ex.Message;
        }
    }

    /// <summary>Applies the selected saved scene material to the edited objects.</summary>
    private async Task ApplySceneMaterialAsync()
    {
        if (sceneMaterialBox.SelectedItem is not SceneMaterialChoice choice)
        {
            statusText.Text = "Save the current material with Save as… first.";
            return;
        }
        MaterialPreset preset = new("Scene", choice.Entry.Name, choice.Entry.Material, $"Scene material {choice.Entry.Name}.");
        await RunEditAsync(
            () => session.ApplyMaterialPreset(targetIds, preset) > 0,
            $"Applied scene material {choice.Entry.Name}. Base color overwritten and direct-property fields refreshed; assigned textures were preserved.");
    }

    /// <summary>Rebuilds the saved-material list, keeping the selection when possible.</summary>
    private void RefreshSceneMaterialList(string? selectName = null)
    {
        IReadOnlyList<SceneMaterialEntry> entries = session.GetSceneMaterials();
        string? keep = selectName ?? (sceneMaterialBox.SelectedItem as SceneMaterialChoice)?.Entry.Name;
        SceneMaterialChoice[] choices = entries.Select(static entry => new SceneMaterialChoice(entry)).ToArray();
        sceneMaterialBox.ItemsSource = choices;
        sceneMaterialBox.SelectedItem = choices.FirstOrDefault(choice => choice.Entry.Name == keep) ?? choices.FirstOrDefault();
        UpdateSceneMaterialSummary();
    }

    /// <summary>Updates the saved-material summary line for the current selection.</summary>
    private void UpdateSceneMaterialSummary()
    {
        if (sceneMaterialBox.SelectedItem is not SceneMaterialChoice choice)
        {
            sceneMaterialSummary.Text = string.Empty;
            return;
        }
        sceneMaterialSummary.Text = $"Color {ToHex(choice.Entry.Material.Color)}  Metallic {choice.Entry.Material.Metallic:0.##}, roughness {choice.Entry.Material.Roughness:0.##}.";
    }

    /// <summary>Applies the entered base color to the edited object.</summary>
    private async Task ApplyColorAsync()
    {
        if (!TryReadColor(out Vec3 color))
        {
            statusText.Text = "Enter RGB values from 0 to 255 or a hex color such as #C8A060.";
            return;
        }
        await RunEditAsync(() => session.SetObjectBaseColor(targetIds, color) > 0, $"Base color set to {ToHex(color)}.");
    }

    /// <summary>Applies the entered PBR properties to the edited object.</summary>
    private async Task ApplyPropertiesAsync()
    {
        if (!TryReadMaterialProperties(out ComposerMaterialProperties? properties, out string error))
        {
            statusText.Text = error;
            return;
        }

        await RunEditAsync(
            () => session.SetObjectMaterialProperties(targetIds, properties!) > 0,
            "Direct material properties applied. Base color and texture maps were preserved.");
    }

    /// <summary>Opens a file picker and assigns the chosen image to the given texture slot.</summary>
    private async Task BrowseTextureAsync(MaterialTextureSlot slot)
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
                Title = $"Choose {ComposerSceneSession.TextureSlotLabel(slot).ToLowerInvariant()} texture",
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

        bool boxProjection = SelectedProjectionMode;
        await RunEditAsync(
            () => session.SetObjectTexture(targetIds, slot, path, tileMeters, boxProjection) > 0,
            boxProjection
                ? $"{ComposerSceneSession.TextureSlotLabel(slot)} texture {Path.GetFileName(path)} applied with {tileMeters:0.######} m box tiling."
                : $"{ComposerSceneSession.TextureSlotLabel(slot)} texture {Path.GetFileName(path)} applied using current/authored UVs.");
    }

    /// <summary>Clears the texture assigned to the given slot.</summary>
    private async Task ClearTextureAsync(MaterialTextureSlot slot)
    {
        await RunEditAsync(
            () => session.ClearObjectTexture(targetIds, slot) > 0,
            $"{ComposerSceneSession.TextureSlotLabel(slot)} texture cleared.");
    }

    /// <summary>Applies the entered UV transform and address modes to the selected texture slot.</summary>
    private async Task ApplyTextureMappingAsync()
    {
        if (mappingSlotBox.SelectedItem is not TextureSlotChoice choice)
        {
            statusText.Text = "Choose a texture slot to map.";
            return;
        }
        if (currentModel?.TextureSlot(choice.Slot).HasTexture != true)
        {
            statusText.Text = $"Assign a {choice.Label.ToLowerInvariant()} texture before editing its mapping.";
            return;
        }
        if (!TryReadTileMeters(out double tileMeters))
        {
            statusText.Text = "Box tile size must be a number greater than zero meters.";
            return;
        }
        if (!TryDouble(offsetUBox.Text, out double offsetU) || !TryDouble(offsetVBox.Text, out double offsetV))
        {
            statusText.Text = "Texture offsets must be valid numbers.";
            return;
        }
        if (!TryDouble(scaleUBox.Text, out double scaleU) || Math.Abs(scaleU) <= 1e-9 ||
            !TryDouble(scaleVBox.Text, out double scaleV) || Math.Abs(scaleV) <= 1e-9)
        {
            statusText.Text = "Texture U/V scale must be non-zero numbers.";
            return;
        }
        if (!TryDouble(rotationBox.Text, out double rotationDegrees))
        {
            statusText.Text = "Texture rotation must be valid degrees.";
            return;
        }
        if (wrapUBox.SelectedItem is not TextureAddressMode wrapU || wrapVBox.SelectedItem is not TextureAddressMode wrapV)
        {
            statusText.Text = "Choose U and V texture address modes.";
            return;
        }

        bool boxProjection = SelectedProjectionMode;
        await RunEditAsync(
            () => session.SetObjectTextureMappingAndProjection(
                targetIds,
                choice.Slot,
                tileMeters,
                boxProjection,
                offsetU,
                offsetV,
                scaleU,
                scaleV,
                rotationDegrees,
                wrapU,
                wrapV) > 0,
            $"{choice.Label} mapping applied: offset ({offsetU:0.###}, {offsetV:0.###}), scale ({scaleU:0.###}, {scaleV:0.###}), rotation {rotationDegrees:0.###}°."
        );
    }

    /// <summary>Loads a finish preset into the Surface-tab metallic and roughness editors.</summary>
    private void SetFinishEditors(FinishPreset preset)
    {
        metallicBox.Text = Format(preset.Metallic);
        roughnessBox.Text = Format(preset.Roughness);
    }

    /// <summary>Loads an alpha preset into the Surface-tab alpha editors.</summary>
    private void SetAlphaEditors(AlphaPreset preset)
    {
        alphaModeBox.SelectedItem = preset.AlphaMode;
        opacityBox.Text = Format(preset.Alpha);
        alphaCutoffBox.Text = Format(preset.AlphaCutoff);
    }

    /// <summary>Loads a glow preset into the emission editors.</summary>
    private void SetGlowEditors(GlowPreset preset)
    {
        emissionBox.Text = Format(preset.Emission);
        emissionColorBox.Text = ToHex(preset.EmissionColor);
    }

    /// <summary>Loads a glass preset into the transmission, volume, and IOR editors.</summary>
    private void SetGlassEditors(GlassPreset preset)
    {
        transmissionBox.Text = Format(preset.Transmission);
        thicknessBox.Text = Format(preset.Thickness);
        attenuationColorBox.Text = ToHex(preset.AttenuationColor);
        attenuationDistanceBox.Text = Format(preset.AttenuationDistance);
        iorBox.Text = Format(preset.Ior);
    }

    /// <summary>Loads a detail-effect preset into the Detail-tab editors.</summary>
    private void SetDetailEffectEditors(DetailEffectPreset preset)
    {
        normalScaleBox.Text = Format(preset.NormalScale);
        occlusionStrengthBox.Text = Format(preset.OcclusionStrength);
        specularBox.Text = Format(preset.Specular);
        specularColorBox.Text = ToHex(preset.SpecularColor);
        sheenColorBox.Text = ToHex(preset.SheenColor);
        sheenRoughnessBox.Text = Format(preset.SheenRoughness);
        anisotropyBox.Text = Format(preset.Anisotropy);
        anisotropyRotationBox.Text = Format(preset.AnisotropyRotation);
        iridescenceBox.Text = Format(preset.Iridescence);
        iridescenceIorBox.Text = Format(preset.IridescenceIor);
        iridescenceThicknessMinBox.Text = Format(preset.IridescenceThicknessMinimum);
        iridescenceThicknessMaxBox.Text = Format(preset.IridescenceThicknessMaximum);
    }

    /// <summary>Loads a mapping preset into the Mapping-tab editors.</summary>
    private void SetMappingEditors(MappingPreset preset)
    {
        projectionModeBox.SelectedIndex = preset.BoxProjection ? 1 : 0;
        tileMetersBox.Text = Format(preset.TileMeters);
        offsetUBox.Text = Format(preset.OffsetU);
        offsetVBox.Text = Format(preset.OffsetV);
        scaleUBox.Text = Format(preset.ScaleU);
        scaleVBox.Text = Format(preset.ScaleV);
        rotationBox.Text = Format(preset.RotationDegrees);
        wrapUBox.SelectedItem = preset.WrapU;
        wrapVBox.SelectedItem = preset.WrapV;
    }

    /// <summary>Applies a built-in library image to the given texture slot.</summary>
    private async Task ApplyTexturePresetAsync(TexturePreset preset, MaterialTextureSlot slot, string mapFile)
    {
        string? path = TexturePresetLibrary.ResolvePath(mapFile);
        if (string.IsNullOrWhiteSpace(path))
        {
            statusText.Text = $"Library texture {Path.GetFileName(mapFile)} is missing from the install.";
            return;
        }
        if (!TryReadTileMeters(out double tileMeters))
        {
            statusText.Text = "Texture tile size must be a number greater than zero meters.";
            return;
        }

        bool boxProjection = SelectedProjectionMode;
        await RunEditAsync(
            () => session.SetObjectTexture(targetIds, slot, path, tileMeters, boxProjection) > 0,
            boxProjection
                ? $"{ComposerSceneSession.TextureSlotLabel(slot)} library texture {preset.Name} applied with {tileMeters:0.######} m box tiling."
                : $"{ComposerSceneSession.TextureSlotLabel(slot)} library texture {preset.Name} applied using current/authored UVs.");
    }

    /// <summary>Runs a scene edit on a background thread and refreshes the editor when it succeeds.</summary>
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

    /// <summary>Builds the window title for the primary object and any extra targets.</summary>
    private string TargetTitle(string primaryName) =>
        targetIds.Length > 1 ? $"Material — {primaryName} (+{targetIds.Length - 1} more)" : $"Material — {primaryName}";

    /// <summary>Loads the editor controls and material summary from the given material model.</summary>
    private void LoadModel(ComposerMaterialModel model)
    {
        Title = TargetTitle(model.ObjectName);
        SetColorEditors(model.BaseColor);
        if (spectrumPicker != null)
            spectrumPicker.Color = Color.FromRgb(ToByte(model.BaseColor.X), ToByte(model.BaseColor.Y), ToByte(model.BaseColor.Z));
        metallicBox.Text = Format(model.Metallic);
        roughnessBox.Text = Format(model.Roughness);
        transmissionBox.Text = Format(model.Transmission);
        opacityBox.Text = Format(model.Alpha);
        iorBox.Text = Format(model.Ior);
        emissionBox.Text = Format(model.Emission);
        emissionColorBox.Text = ToHex(model.EmissionColor);
        thicknessBox.Text = Format(model.Thickness);
        attenuationColorBox.Text = ToHex(model.AttenuationColor);
        attenuationDistanceBox.Text = Format(model.AttenuationDistance);
        clearcoatBox.Text = Format(model.Clearcoat);
        clearcoatRoughnessBox.Text = Format(model.ClearcoatRoughness);
        normalScaleBox.Text = Format(model.NormalScale);
        occlusionStrengthBox.Text = Format(model.OcclusionStrength);
        specularBox.Text = Format(model.Specular);
        specularColorBox.Text = ToHex(model.SpecularColor);
        sheenColorBox.Text = ToHex(model.SheenColor);
        sheenRoughnessBox.Text = Format(model.SheenRoughness);
        anisotropyBox.Text = Format(model.Anisotropy);
        anisotropyRotationBox.Text = Format(model.AnisotropyRotation);
        iridescenceBox.Text = Format(model.Iridescence);
        iridescenceIorBox.Text = Format(model.IridescenceIor);
        iridescenceThicknessMinBox.Text = Format(model.IridescenceThicknessMinimum);
        iridescenceThicknessMaxBox.Text = Format(model.IridescenceThicknessMaximum);
        alphaModeBox.SelectedItem = model.AlphaMode;
        alphaCutoffBox.Text = Format(model.AlphaCutoff);
        doubleSidedBox.IsChecked = model.DoubleSided;

        currentModel = model;
        foreach (ComposerTextureSlotModel slot in model.TextureSlots)
            texturePathBoxes[slot.Slot].Text = slot.Path ?? slot.Name ?? string.Empty;
        tileMetersBox.Text = model.TextureTileMeters.ToString("0.######", CultureInfo.InvariantCulture);
        projectionModeBox.SelectedIndex = model.HasStoredTextureProjection && model.UsesBoxProjection ? 1 : 0;
        LoadSelectedTextureMapping();
        int textureCount = model.TextureSlots.Count(slot => slot.HasTexture);
        materialDetails.Text =
            $"Color {ToHex(model.BaseColor)}   Metallic {model.Metallic:0.###}   Roughness {model.Roughness:0.###}\n" +
            $"Transmission {model.Transmission:0.###}   Opacity {model.Alpha:0.###}   IOR {model.Ior:0.###}   Emission {model.Emission:0.###}\n" +
            $"Clearcoat {model.Clearcoat:0.###}   Thickness {model.Thickness:0.######} m   Texture maps {textureCount}";
        SyncPresetSelection(model);
        SyncPickerHighlights(model);
        RefreshSceneMaterialList();
    }

    /// <summary>Highlights the tab presets matching the loaded model values.</summary>
    private void SyncPickerHighlights(ComposerMaterialModel model)
    {
        SyncPropertyHighlights(model.BaseColor, model.DirectProperties);
        SyncMappingHighlight();
    }

    /// <summary>Highlights the property and color presets matching the given values.</summary>
    private void SyncPropertyHighlights(Vec3 baseColor, ComposerMaterialProperties properties)
    {
        finishPicker?.SetSelected(MatchFinishPreset(properties.Metallic, properties.Roughness));
        alphaPicker?.SetSelected(MatchAlphaPreset(properties.AlphaMode, properties.Alpha, properties.AlphaCutoff));
        glowPicker?.SetSelected(MatchGlowPreset(properties.Emission, properties.EmissionColor));
        glassPicker?.SetSelected(MatchGlassPreset(properties.Transmission, properties.Thickness, properties.AttenuationColor, properties.AttenuationDistance, properties.Ior));
        detailPicker?.SetSelected(MatchDetailEffectPreset(properties));
        colorSwatchPicker?.SetSelected(MatchColorPreset(baseColor));
    }

    /// <summary>Clears every property and color preset highlight.</summary>
    private void ClearPropertyHighlights()
    {
        finishPicker?.SetSelected(null);
        alphaPicker?.SetSelected(null);
        glowPicker?.SetSelected(null);
        glassPicker?.SetSelected(null);
        detailPicker?.SetSelected(null);
        colorSwatchPicker?.SetSelected(null);
    }

    /// <summary>Recomputes property highlights from the dialog values, whatever changed them.</summary>
    private void RefreshPropertyHighlightsFromDialog()
    {
        if (!TryReadColor(out Vec3 color) || !TryReadMaterialProperties(out ComposerMaterialProperties? properties, out _))
        {
            ClearPropertyHighlights();
            return;
        }
        SyncPropertyHighlights(color, properties!);
    }

    /// <summary>Recomputes the mapping highlight from the dialog values, whatever changed them.</summary>
    private void RefreshMappingHighlightFromDialog()
    {
        if (mappingPicker == null
            || !TryReadTileMeters(out double tileMeters)
            || !TryDouble(offsetUBox.Text, out double offsetU) || !TryDouble(offsetVBox.Text, out double offsetV)
            || !TryDouble(scaleUBox.Text, out double scaleU) || !TryDouble(scaleVBox.Text, out double scaleV)
            || !TryDouble(rotationBox.Text, out double rotation)
            || wrapUBox.SelectedItem is not TextureAddressMode wrapU || wrapVBox.SelectedItem is not TextureAddressMode wrapV)
        {
            mappingPicker?.SetSelected(null);
            return;
        }
        ComposerTextureSlotModel slot = new(
            MaterialTextureSlot.BaseColor, string.Empty, null, null,
            offsetU, offsetV, scaleU, scaleV, rotation, wrapU, wrapV);
        mappingPicker.SetSelected(MatchMappingPreset(SelectedProjectionMode, tileMeters, slot));
    }

    /// <summary>Highlights the mapping preset matching the selected slot, if any.</summary>
    private void SyncMappingHighlight()
    {
        if (mappingPicker == null || currentModel == null || mappingSlotBox.SelectedItem is not TextureSlotChoice choice)
            return;
        ComposerTextureSlotModel slot = currentModel.TextureSlot(choice.Slot);
        mappingPicker.SetSelected(MappingPresets.All.FirstOrDefault(p =>
            p.BoxProjection == SelectedProjectionMode
            && Nearly(currentModel.TextureTileMeters, p.TileMeters)
            && Nearly(slot.OffsetU, p.OffsetU) && Nearly(slot.OffsetV, p.OffsetV)
            && Nearly(slot.ScaleU, p.ScaleU) && Nearly(slot.ScaleV, p.ScaleV)
            && Nearly(slot.RotationDegrees, p.RotationDegrees)
            && slot.WrapU == p.WrapU && slot.WrapV == p.WrapV));
    }

    private bool SelectedProjectionMode =>
        projectionModeBox.SelectedItem is ProjectionChoice choice && choice.BoxProjection;

    /// <summary>Loads the UV mapping controls from the currently selected texture slot.</summary>
    private void LoadSelectedTextureMapping()
    {
        if (currentModel == null || mappingSlotBox.SelectedItem is not TextureSlotChoice choice)
            return;

        ComposerTextureSlotModel slot = currentModel.TextureSlot(choice.Slot);
        offsetUBox.Text = Format(slot.OffsetU);
        offsetVBox.Text = Format(slot.OffsetV);
        scaleUBox.Text = Format(slot.ScaleU);
        scaleVBox.Text = Format(slot.ScaleV);
        rotationBox.Text = Format(slot.RotationDegrees);
        wrapUBox.SelectedItem = slot.WrapU;
        wrapVBox.SelectedItem = slot.WrapV;
    }

    /// <summary>Builds a texture slot row with its path display and browse and clear buttons.</summary>
    private Control BuildTextureSlotRow(MaterialTextureSlot slot)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("150,*,Auto,Auto"),
            ColumnSpacing = 8
        };
        TextBox pathBox = texturePathBoxes[slot];
        TextBlock slotLabel = new()
        {
            Text = ComposerSceneSession.TextureSlotLabel(slot),
            VerticalAlignment = VerticalAlignment.Center
        };
        MirrorTip(pathBox, slotLabel);
        row.Children.Add(slotLabel);
        row.Children.Add(pathBox);
        Grid.SetColumn(pathBox, 1);
        Button browse = Tip(NewButton("Browse…"), $"Assigns an image file to the {ComposerSceneSession.TextureSlotLabel(slot)} slot on every targeted object. The image is decoded once and shared; later per-slot mapping edits stay independent per object.");
        browse.Click += async (_, _) => await BrowseTextureAsync(slot);
        row.Children.Add(browse);
        Grid.SetColumn(browse, 2);
        Button clear = Tip(NewButton("Clear"), $"Removes the {ComposerSceneSession.TextureSlotLabel(slot)} texture from every targeted object, returning the slot to a plain value.");
        clear.Click += async (_, _) => await ClearTextureAsync(slot);
        row.Children.Add(clear);
        Grid.SetColumn(clear, 3);
        return row;
    }

    /// <summary>Updates the preset summary line for the currently selected preset.</summary>
    private void UpdatePresetSummary()
    {
        if (presetBox.SelectedItem is not MaterialPreset preset)
        {
            presetSummary.Text = "Custom values — the current material differs from every library preset.";
            return;
        }
        presetSummary.Text = $"{preset.Summary}  Metallic {preset.Material.Metallic:0.##}, roughness {preset.Material.Roughness:0.##}.";
    }

    /// <summary>Finds the library preset matching the model's governed values, or null when it deviates.</summary>
    internal static MaterialPreset? FindMatchingPreset(ComposerMaterialModel model) =>
        FindMatchingPreset(model.BaseColor, model.DirectProperties);

    /// <summary>Finds the library preset matching the given dialog values, or null when they deviate.</summary>
    internal static MaterialPreset? FindMatchingPreset(Vec3 baseColor, ComposerMaterialProperties properties)
    {
        foreach (MaterialPreset preset in MaterialPresetLibrary.Common)
        {
            if (MatchesPreset(baseColor, properties, preset.Material))
                return preset;
        }
        return null;
    }

    /// <summary>Compares every preset-governed value. Textures, light links, and double-sided rendering are preserved rather than governed, so they never break a match.</summary>
    private static bool MatchesPreset(Vec3 baseColor, ComposerMaterialProperties properties, Material material)
    {
        return Nearly(baseColor, material.Color)
            && Nearly(properties.Metallic, material.Metallic)
            && Nearly(properties.Roughness, material.Roughness)
            && Nearly(properties.Transmission, material.Transmission)
            && Nearly(properties.Alpha, material.Alpha)
            && Nearly(properties.Emission, material.Emission)
            && Nearly(properties.EmissionColor, material.EmissionColor)
            && properties.AlphaMode == material.AlphaMode
            && Nearly(properties.AlphaCutoff, material.AlphaCutoff)
            && Nearly(properties.Ior, material.Ior)
            && Nearly(properties.Thickness, material.Thickness)
            && Nearly(properties.AttenuationColor, material.AttenuationColor)
            && Nearly(properties.AttenuationDistance, material.AttenuationDistance)
            && Nearly(properties.Clearcoat, material.Clearcoat)
            && Nearly(properties.ClearcoatRoughness, material.ClearcoatRoughness)
            && Nearly(properties.NormalScale, material.NormalScale)
            && Nearly(properties.OcclusionStrength, material.OcclusionStrength)
            && Nearly(properties.Specular, material.Specular)
            && Nearly(properties.SpecularColor, material.SpecularColor)
            && Nearly(properties.SheenColor, material.SheenColor)
            && Nearly(properties.SheenRoughness, material.SheenRoughness)
            && Nearly(properties.Anisotropy, material.Anisotropy)
            && Nearly(properties.AnisotropyRotation, material.AnisotropyRotation)
            && Nearly(properties.Iridescence, material.Iridescence)
            && Nearly(properties.IridescenceIor, material.IridescenceIor)
            && Nearly(properties.IridescenceThicknessMinimum, material.IridescenceThicknessMinimum)
            && Nearly(properties.IridescenceThicknessMaximum, material.IridescenceThicknessMaximum);
    }

    /// <summary>Compares two doubles within preset-matching tolerance.</summary>
    private static bool Nearly(double a, double b) => Math.Abs(a - b) <= 1e-9;

    /// <summary>Compares two colors channel by channel within preset-matching tolerance.</summary>
    private static bool Nearly(Vec3 a, Vec3 b) => Nearly(a.X, b.X) && Nearly(a.Y, b.Y) && Nearly(a.Z, b.Z);

    /// <summary>Finds the finish preset matching the given values, or null.</summary>
    internal static FinishPreset? MatchFinishPreset(double metallic, double roughness) =>
        FinishPresets.All.FirstOrDefault(p => Nearly(metallic, p.Metallic) && Nearly(roughness, p.Roughness));

    /// <summary>Finds the alpha preset matching the given values, or null.</summary>
    internal static AlphaPreset? MatchAlphaPreset(MaterialAlphaMode mode, double alpha, double cutoff) =>
        AlphaPresets.All.FirstOrDefault(p => p.AlphaMode == mode && Nearly(alpha, p.Alpha) && Nearly(cutoff, p.AlphaCutoff));

    /// <summary>Finds the glow preset matching the given values, or null.</summary>
    internal static GlowPreset? MatchGlowPreset(double emission, Vec3 color) =>
        GlowPresets.All.FirstOrDefault(p => Nearly(emission, p.Emission) && Nearly(color, p.EmissionColor));

    /// <summary>Finds the glass preset matching the given values, or null.</summary>
    internal static GlassPreset? MatchGlassPreset(double transmission, double thickness, Vec3 attenuationColor, double attenuationDistance, double ior) =>
        GlassPresets.All.FirstOrDefault(p =>
            Nearly(transmission, p.Transmission) && Nearly(thickness, p.Thickness)
            && Nearly(attenuationColor, p.AttenuationColor) && Nearly(attenuationDistance, p.AttenuationDistance)
            && Nearly(ior, p.Ior));

    /// <summary>Finds the detail-effect preset matching the given values, or null.</summary>
    internal static DetailEffectPreset? MatchDetailEffectPreset(ComposerMaterialProperties properties) =>
        DetailEffectPresets.All.FirstOrDefault(p =>
            Nearly(properties.NormalScale, p.NormalScale) && Nearly(properties.OcclusionStrength, p.OcclusionStrength)
            && Nearly(properties.Specular, p.Specular) && Nearly(properties.SpecularColor, p.SpecularColor)
            && Nearly(properties.SheenColor, p.SheenColor) && Nearly(properties.SheenRoughness, p.SheenRoughness)
            && Nearly(properties.Anisotropy, p.Anisotropy) && Nearly(properties.AnisotropyRotation, p.AnisotropyRotation)
            && Nearly(properties.Iridescence, p.Iridescence) && Nearly(properties.IridescenceIor, p.IridescenceIor)
            && Nearly(properties.IridescenceThicknessMinimum, p.IridescenceThicknessMinimum)
            && Nearly(properties.IridescenceThicknessMaximum, p.IridescenceThicknessMaximum));

    /// <summary>Finds the color preset matching the given color, or null.</summary>
    internal static ColorPreset? MatchColorPreset(Vec3 color) =>
        ColorPresets.All.FirstOrDefault(p => Nearly(color, p.Color));

    /// <summary>Finds the mapping preset matching the given slot state, or null.</summary>
    internal static MappingPreset? MatchMappingPreset(bool boxProjection, double tileMeters, ComposerTextureSlotModel slot) =>
        MappingPresets.All.FirstOrDefault(p =>
            p.BoxProjection == boxProjection && Nearly(tileMeters, p.TileMeters)
            && Nearly(slot.OffsetU, p.OffsetU) && Nearly(slot.OffsetV, p.OffsetV)
            && Nearly(slot.ScaleU, p.ScaleU) && Nearly(slot.ScaleV, p.ScaleV)
            && Nearly(slot.RotationDegrees, p.RotationDegrees)
            && slot.WrapU == p.WrapU && slot.WrapV == p.WrapV);

    /// <summary>Selects the preset matching the loaded model, or unassigned when it deviates.</summary>
    private void SyncPresetSelection(ComposerMaterialModel model) =>
        SelectPresetOrUnassigned(FindMatchingPreset(model));

    /// <summary>Selects the preset matching the current dialog values, or unassigned when they deviate.</summary>
    private void SyncPresetSelectionFromDialog()
    {
        if (!TryReadColor(out Vec3 color) || !TryReadMaterialProperties(out ComposerMaterialProperties? properties, out _))
        {
            presetBox.SelectedItem = null;
            return;
        }
        SelectPresetOrUnassigned(FindMatchingPreset(color, properties!));
    }

    /// <summary>Selects the given preset, resetting the category filter when it would hide the match.</summary>
    private void SelectPresetOrUnassigned(MaterialPreset? match)
    {
        if (match != null
            && presetBox.ItemsSource is System.Collections.IEnumerable items
            && !items.Cast<object>().Contains(match))
            categoryBox.SelectedIndex = 0;
        presetBox.SelectedItem = match;
    }

    /// <summary>Reads and validates the entered material properties from the editor controls.</summary>
    private bool TryReadMaterialProperties(out ComposerMaterialProperties? properties, out string error)
    {
        properties = null;
        error = string.Empty;

        if (!TryRange(metallicBox.Text, 0.0, 1.0, out double metallic))
            return Fail("Metallic must be between 0 and 1.", out error);
        if (!TryRange(roughnessBox.Text, 0.02, 1.0, out double roughness))
            return Fail("Roughness must be between 0.02 and 1.", out error);
        if (!TryRange(transmissionBox.Text, 0.0, 1.0, out double transmission))
            return Fail("Transmission must be between 0 and 1.", out error);
        if (!TryRange(opacityBox.Text, 0.0, 1.0, out double alpha))
            return Fail("Opacity must be between 0 and 1.", out error);
        if (!TryRange(iorBox.Text, 1.0, 2.333, out double ior))
            return Fail("IOR must be between 1.0 and 2.333.", out error);
        if (!TryRange(emissionBox.Text, 0.0, 100000.0, out double emission))
            return Fail("Emission strength must be between 0 and 100000.", out error);
        if (!TryParseHex(emissionColorBox.Text, out Vec3 emissionColor))
            return Fail("Emission color must be a hex color such as #FFFFFF.", out error);
        if (!TryRange(thicknessBox.Text, 0.0, double.MaxValue, out double thickness))
            return Fail("Thickness must be zero or greater meters.", out error);
        if (!TryParseHex(attenuationColorBox.Text, out Vec3 attenuationColor))
            return Fail("Attenuation color must be a hex color such as #FFFFFF.", out error);
        if (!TryRange(attenuationDistanceBox.Text, 0.0, double.MaxValue, out double attenuationDistance))
            return Fail("Attenuation distance must be zero or greater meters.", out error);
        if (!TryRange(clearcoatBox.Text, 0.0, 1.0, out double clearcoat))
            return Fail("Clearcoat must be between 0 and 1.", out error);
        if (!TryRange(clearcoatRoughnessBox.Text, 0.0, 1.0, out double clearcoatRoughness))
            return Fail("Clearcoat roughness must be between 0 and 1.", out error);
        if (!TryRange(normalScaleBox.Text, -8.0, 8.0, out double normalScale))
            return Fail("Normal scale must be between -8 and 8.", out error);
        if (!TryRange(occlusionStrengthBox.Text, 0.0, 1.0, out double occlusionStrength))
            return Fail("Occlusion strength must be between 0 and 1.", out error);
        if (!TryRange(specularBox.Text, 0.0, 1.0, out double specular))
            return Fail("Specular must be between 0 and 1.", out error);
        if (!TryParseHex(specularColorBox.Text, out Vec3 specularColor))
            return Fail("Specular color must be a hex color such as #FFFFFF.", out error);
        if (!TryParseHex(sheenColorBox.Text, out Vec3 sheenColor))
            return Fail("Sheen color must be a hex color such as #FFFFFF.", out error);
        if (!TryRange(sheenRoughnessBox.Text, 0.0, 1.0, out double sheenRoughness))
            return Fail("Sheen roughness must be between 0 and 1.", out error);
        if (!TryRange(anisotropyBox.Text, 0.0, 1.0, out double anisotropy))
            return Fail("Anisotropy must be between 0 and 1.", out error);
        if (!TryDouble(anisotropyRotationBox.Text, out double anisotropyRotation) || !double.IsFinite(anisotropyRotation))
            return Fail("Anisotropy rotation must be finite radians.", out error);
        if (!TryRange(iridescenceBox.Text, 0.0, 1.0, out double iridescence))
            return Fail("Iridescence must be between 0 and 1.", out error);
        if (!TryRange(iridescenceIorBox.Text, 1.0, double.MaxValue, out double iridescenceIor))
            return Fail("Iridescence IOR must be 1.0 or greater.", out error);
        if (!TryRange(iridescenceThicknessMinBox.Text, 0.0, double.MaxValue, out double iridescenceThicknessMinimum))
            return Fail("Iridescence minimum thickness must be zero or greater nanometers.", out error);
        if (!TryRange(iridescenceThicknessMaxBox.Text, 0.0, double.MaxValue, out double iridescenceThicknessMaximum))
            return Fail("Iridescence maximum thickness must be zero or greater nanometers.", out error);
        if (alphaModeBox.SelectedItem is not MaterialAlphaMode alphaMode)
            return Fail("Choose an alpha mode.", out error);
        if (!TryRange(alphaCutoffBox.Text, 0.0, 1.0, out double alphaCutoff))
            return Fail("Alpha cutoff must be between 0 and 1.", out error);

        properties = new ComposerMaterialProperties(
            metallic,
            roughness,
            transmission,
            alpha,
            emission,
            emissionColor,
            alphaMode,
            alphaCutoff,
            doubleSidedBox.IsChecked == true,
            ior,
            thickness,
            attenuationColor,
            attenuationDistance,
            clearcoat,
            clearcoatRoughness,
            normalScale,
            occlusionStrength,
            specular,
            specularColor,
            sheenColor,
            sheenRoughness,
            anisotropy,
            anisotropyRotation,
            iridescence,
            iridescenceIor,
            iridescenceThicknessMinimum,
            iridescenceThicknessMaximum);
        return true;
    }

    /// <summary>Synchronizes the hex box and color swatch with the RGB channel boxes.</summary>
    private void SyncColorFromChannels()
    {
        if (synchronizingColor || !TryReadRgb(out Vec3 color))
            return;
        synchronizingColor = true;
        hexBox.Text = ToHex(color);
        colorSwatch.Background = ToBrush(color);
        synchronizingColor = false;
    }

    /// <summary>Sets the RGB, hex, and swatch editors to the given color.</summary>
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

    /// <summary>Reads the entered base color from the hex box or the RGB channel boxes.</summary>
    private bool TryReadColor(out Vec3 color)
    {
        if (TryParseHex(hexBox.Text, out color))
            return true;
        return TryReadRgb(out color);
    }

    /// <summary>Reads the entered base color from the RGB channel boxes.</summary>
    private bool TryReadRgb(out Vec3 color)
    {
        color = Vec3.Zero;
        if (!TryByte(redBox.Text, out int r) || !TryByte(greenBox.Text, out int g) || !TryByte(blueBox.Text, out int b))
            return false;
        color = new Vec3(r / 255.0, g / 255.0, b / 255.0);
        return true;
    }

    /// <summary>Reads the entered box tile size and returns true when it is greater than zero meters.</summary>
    private bool TryReadTileMeters(out double value)
    {
        string text = tileMetersBox.Text?.Trim() ?? string.Empty;
        bool ok = TryDouble(text, out value);
        return ok && value > 1e-6;
    }

    /// <summary>Parses a hex color string into a normalized color vector.</summary>
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

    /// <summary>Parses the given text and returns true when the value lies within the given range.</summary>
    private static bool TryRange(string? text, double min, double max, out double value) =>
        TryDouble(text, out value) && value >= min && value <= max;

    /// <summary>Parses the given text as a finite number using the current or invariant culture.</summary>
    private static bool TryDouble(string? text, out double value)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        bool ok = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                  double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return ok && double.IsFinite(value);
    }

    /// <summary>Sets the given error message and returns false.</summary>
    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    /// <summary>Parses the given text as a byte value between 0 and 255.</summary>
    private static bool TryByte(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 255;

    /// <summary>Converts a normalized color channel to a byte.</summary>
    private static byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255.0), 0, 255);
    /// <summary>Formats the given color as a hex string.</summary>
    private static string ToHex(Vec3 color) => $"#{ToByte(color.X):X2}{ToByte(color.Y):X2}{ToByte(color.Z):X2}";
    /// <summary>Creates a solid brush from the given color.</summary>
    private static IBrush ToBrush(Vec3 color) => new SolidColorBrush(Color.FromRgb(ToByte(color.X), ToByte(color.Y), ToByte(color.Z)));
    /// <summary>Formats the given value with invariant culture.</summary>
    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Creates a text box for editing one RGB channel value.</summary>
    private static TextBox ChannelBox() => new() { MinWidth = 58, TextAlignment = TextAlignment.Right };
    /// <summary>Creates a right-aligned text box for editing a material property.</summary>
    private static TextBox PropertyBox() => new() { MinWidth = 88, TextAlignment = TextAlignment.Right };

    /// <summary>Adds a labeled RGB editor to the given grid at the given column.</summary>
    private static void AddRgb(Grid grid, string label, Control editor, int column)
    {
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        MirrorTip(editor, text);
        grid.Children.Add(text);
        Grid.SetColumn(text, column);
        grid.Children.Add(editor);
        Grid.SetColumn(editor, column + 1);
    }

    /// <summary>Copies the editor tooltip onto its label so hovering either shows the same help.</summary>
    private static void MirrorTip(Control editor, TextBlock label)
    {
        object? tip = ToolTip.GetTip(editor);
        if (tip != null)
            ToolTip.SetTip(label, tip);
    }

    /// <summary>Creates a section heading with the given text.</summary>
    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 6, 0, 0)
    };

    /// <summary>Creates a row pairing the given label with the given control.</summary>
    private static Control LabeledControl(string label, Control control)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        MirrorTip(control, text);
        row.Children.Add(text);
        row.Children.Add(control);
        Grid.SetColumn(control, 1);
        return row;
    }

    /// <summary>Creates a row with two labeled property editors.</summary>
    private static Control TwoPropertyRow(string leftLabel, Control left, string rightLabel, Control right)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,16,*,Auto"),
            ColumnSpacing = 8
        };
        TextBlock leftText = new() { Text = leftLabel, VerticalAlignment = VerticalAlignment.Center };
        MirrorTip(left, leftText);
        row.Children.Add(leftText);
        row.Children.Add(left);
        Grid.SetColumn(left, 1);
        TextBlock rightText = new() { Text = rightLabel, VerticalAlignment = VerticalAlignment.Center };
        MirrorTip(right, rightText);
        row.Children.Add(rightText);
        Grid.SetColumn(rightText, 3);
        row.Children.Add(right);
        Grid.SetColumn(right, 4);
        return row;
    }

    /// <summary>Creates a button with the given text.</summary>
    private static Button NewButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6),
        MinWidth = 78
    };
}
