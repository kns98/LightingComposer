/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 *
 * `MaterialEditorWindow` owns temporary Avalonia presentation/edit state. Values become durable only when
 * accepted and routed through the relevant session/controller operation, preserving validation and cancellation
 * semantics.
 *
 * `TextureSlotChoice` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`Slot`, `Label`) travel together because consumers need a consistent
 * snapshot rather than reading those values independently from mutable objects.
 *
 * `ProjectionChoice` is an immutable packet of related values. Record value semantics make it suitable for
 * snapshots, options, commands, or parsed intermediate data because callers can copy/compare it without sharing
 * mutable state. Its constructor values (`BoxProjection`, `Label`) travel together because consumers need a
 * consistent snapshot rather than reading those values independently from mutable objects.
 *
 * `ObjectId` is derived rather than separately stored: it evaluates `objectId`. Keeping the value computed from
 * its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `SelectedProjectionMode` is derived rather than separately stored: it evaluates `projectionModeBox.SelectedItem
 * is ProjectionChoice choice && choice.BoxProjection`. Keeping the value computed from its source fields prevents
 * a second cached flag/value from drifting out of sync.
 *
 * `ToString` returns the human-facing label/name for this value so Avalonia controls display meaningful text
 * instead of the generated record/type representation.
 *
 * `ToString` returns the human-facing label/name for this value so Avalonia controls display meaningful text
 * instead of the generated record/type representation.
 *
 * `RefreshFromScene` re-reads authoritative state and updates from scene so cached/presented data matches the
 * current scene after an edit or external change.
 *
 * `BuildContent` derives content from lower-level input data, resolving indexing/grouping/derived values once so
 * callers can operate on a coherent higher-level representation.
 *
 * `ApplyPresetAsync` applies preset async as a single semantic mutation. Validation, scene changes, undo
 * bookkeeping, and cache invalidation are kept inside this boundary rather than exposed as separate caller
 * responsibilities.
 *
 * `ApplyColorAsync` applies color async as a single semantic mutation. Validation, scene changes, undo
 * bookkeeping, and cache invalidation are kept inside this boundary rather than exposed as separate caller
 * responsibilities.
 *
 * `ApplyPropertiesAsync` applies properties async as a single semantic mutation. Validation, scene changes, undo
 * bookkeeping, and cache invalidation are kept inside this boundary rather than exposed as separate caller
 * responsibilities.
 *
 * `BrowseTextureAsync` asks the platform picker for texture async and only proceeds when the user returns a valid
 * local selection; cancellation remains a normal no-op path.
 *
 * `ApplyTextureMappingAsync` applies texture mapping async as a single semantic mutation. Validation, scene
 * changes, undo bookkeeping, and cache invalidation are kept inside this boundary rather than exposed as separate
 * caller responsibilities.
 *
 * `RunEditAsync` executes edit async as one coordinated action and centralizes success/failure handling so
 * callers do not each implement inconsistent exception/UI behavior. Potentially blocking/CPU work runs on a
 * worker task rather than Avalonia’s UI thread.
 *
 * `LoadModel` loads model from persistent/external data and converts it into validated internal scene state
 * rather than exposing parser-specific objects to the rest of the application.
 *
 * `LoadSelectedTextureMapping` loads selected texture mapping from persistent/external data and converts it into
 * validated internal scene state rather than exposing parser-specific objects to the rest of the application.
 *
 * `BuildTextureSlotRow` derives texture slot row from lower-level input data, resolving indexing/grouping/derived
 * values once so callers can operate on a coherent higher-level representation.
 *
 * `UpdatePresetSummary` updates preset summary from the newest input while preserving the
 * identities/metadata/caches that remain valid and invalidating only what the change makes stale.
 *
 * `SyncColorFromChannels` updates color from channels from the authoritative model so UI enable/check state
 * reflects what commands are actually valid right now.
 *
 * `SetColorEditors` sets color editors through the owning abstraction instead of exposing a mutable field. That
 * gives the method one place to validate the value and perform any history/cache/UI side effects required by the
 * change.
 *
 * `AddRgb` adds rgb to the owning collection/model while using this boundary to preserve indexing, ownership, and
 * derived-state invariants.
 */
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
        public override string ToString() => Label;
    }

    private sealed record ProjectionChoice(bool BoxProjection, string Label)
    {
        public override string ToString() => Label;
    }

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
        Width = 620;
        Height = 900;
        MinWidth = 500;
        MinHeight = 560;
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
            Text = "Material changes apply to the selected object and its child meshes.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.76,
            FontSize = 12
        };

        presetBox.SelectionChanged += (_, _) => UpdatePresetSummary();
        mappingSlotBox.SelectionChanged += (_, _) => LoadSelectedTextureMapping();
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
            Text = "Material",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Choose a library preset as a starting point, then override its PBR properties directly. Base color and image texture remain independent controls.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        });

        stack.Children.Add(Heading("Library preset"));
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

        stack.Children.Add(Heading("Direct material properties"));
        stack.Children.Add(new TextBlock
        {
            Text = "Values are written directly to the material used by the raster and ray renderers. 0–1 values use normalized PBR units; thickness and attenuation distance are meters.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12
        });
        stack.Children.Add(TwoPropertyRow("Metallic (0–1)", metallicBox, "Roughness (0.02–1)", roughnessBox));
        stack.Children.Add(TwoPropertyRow("Transmission (0–1)", transmissionBox, "Opacity (0–1)", opacityBox));
        stack.Children.Add(TwoPropertyRow("IOR (1–2.333)", iorBox, "Emission strength", emissionBox));
        stack.Children.Add(LabeledControl("Emission color", emissionColorBox));
        stack.Children.Add(TwoPropertyRow("Thickness (m)", thicknessBox, "Attenuation distance (m)", attenuationDistanceBox));
        stack.Children.Add(LabeledControl("Attenuation color", attenuationColorBox));
        stack.Children.Add(TwoPropertyRow("Clearcoat (0–1)", clearcoatBox, "Clearcoat roughness (0–1)", clearcoatRoughnessBox));
        stack.Children.Add(TwoPropertyRow("Normal scale (-8–8)", normalScaleBox, "Occlusion strength (0–1)", occlusionStrengthBox));
        stack.Children.Add(TwoPropertyRow("Alpha mode", alphaModeBox, "Alpha cutoff (0–1)", alphaCutoffBox));
        stack.Children.Add(doubleSidedBox);
        Button applyProperties = NewButton("Apply properties");
        applyProperties.HorizontalAlignment = HorizontalAlignment.Right;
        applyProperties.Click += async (_, _) => await ApplyPropertiesAsync();
        stack.Children.Add(applyProperties);

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

        stack.Children.Add(Heading("Texture mapping"));
        stack.Children.Add(LabeledControl("Mapping target", mappingSlotBox));
        stack.Children.Add(LabeledControl("UV source", projectionModeBox));
        stack.Children.Add(LabeledControl("UV set", uvSetBox));
        stack.Children.Add(LabeledControl("Box tile size (m)", tileMetersBox));
        stack.Children.Add(TwoPropertyRow("Offset U", offsetUBox, "Offset V", offsetVBox));
        stack.Children.Add(TwoPropertyRow("Scale U", scaleUBox, "Scale V", scaleVBox));
        stack.Children.Add(LabeledControl("Rotation (degrees)", rotationBox));
        stack.Children.Add(TwoPropertyRow("Wrap U", wrapUBox, "Wrap V", wrapVBox));
        Button applyMapping = NewButton("Apply mapping");
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
            $"Applied {preset.Category} — {preset.Name}. Direct-property fields were refreshed; assigned textures were preserved.");
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

    private async Task ApplyPropertiesAsync()
    {
        if (!TryReadMaterialProperties(out ComposerMaterialProperties? properties, out string error))
        {
            statusText.Text = error;
            return;
        }

        await RunEditAsync(
            () => session.SetObjectMaterialProperties(objectId, properties!),
            "Direct material properties applied. Base color and texture maps were preserved.");
    }

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
            () => session.SetObjectTexture(objectId, slot, path, tileMeters, boxProjection),
            boxProjection
                ? $"{ComposerSceneSession.TextureSlotLabel(slot)} texture {Path.GetFileName(path)} applied with {tileMeters:0.######} m box tiling."
                : $"{ComposerSceneSession.TextureSlotLabel(slot)} texture {Path.GetFileName(path)} applied using current/authored UVs.");
    }

    private async Task ClearTextureAsync(MaterialTextureSlot slot)
    {
        await RunEditAsync(
            () => session.ClearObjectTexture(objectId, slot),
            $"{ComposerSceneSession.TextureSlotLabel(slot)} texture cleared.");
    }

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
                objectId,
                choice.Slot,
                tileMeters,
                boxProjection,
                offsetU,
                offsetV,
                scaleU,
                scaleV,
                rotationDegrees,
                wrapU,
                wrapV),
            $"{choice.Label} mapping applied: offset ({offsetU:0.###}, {offsetV:0.###}), scale ({scaleU:0.###}, {scaleV:0.###}), rotation {rotationDegrees:0.###}°."
        );
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
    }

    private bool SelectedProjectionMode =>
        projectionModeBox.SelectedItem is ProjectionChoice choice && choice.BoxProjection;

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

    private Control BuildTextureSlotRow(MaterialTextureSlot slot)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("150,*,Auto,Auto"),
            ColumnSpacing = 8
        };
        row.Children.Add(new TextBlock
        {
            Text = ComposerSceneSession.TextureSlotLabel(slot),
            VerticalAlignment = VerticalAlignment.Center
        });
        TextBox pathBox = texturePathBoxes[slot];
        row.Children.Add(pathBox);
        Grid.SetColumn(pathBox, 1);
        Button browse = NewButton("Browse…");
        browse.Click += async (_, _) => await BrowseTextureAsync(slot);
        row.Children.Add(browse);
        Grid.SetColumn(browse, 2);
        Button clear = NewButton("Clear");
        clear.Click += async (_, _) => await ClearTextureAsync(slot);
        row.Children.Add(clear);
        Grid.SetColumn(clear, 3);
        return row;
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
            occlusionStrength);
        return true;
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
        bool ok = TryDouble(text, out value);
        return ok && value > 1e-6;
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

    private static bool TryRange(string? text, double min, double max, out double value) =>
        TryDouble(text, out value) && value >= min && value <= max;

    private static bool TryDouble(string? text, out double value)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        bool ok = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                  double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return ok && double.IsFinite(value);
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static bool TryByte(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 255;

    private static byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255.0), 0, 255);
    private static string ToHex(Vec3 color) => $"#{ToByte(color.X):X2}{ToByte(color.Y):X2}{ToByte(color.Z):X2}";
    private static IBrush ToBrush(Vec3 color) => new SolidColorBrush(Color.FromRgb(ToByte(color.X), ToByte(color.Y), ToByte(color.Z)));
    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static TextBox ChannelBox() => new() { MinWidth = 58, TextAlignment = TextAlignment.Right };
    private static TextBox PropertyBox() => new() { MinWidth = 88, TextAlignment = TextAlignment.Right };

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

    private static Control TwoPropertyRow(string leftLabel, Control left, string rightLabel, Control right)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,16,*,Auto"),
            ColumnSpacing = 8
        };
        row.Children.Add(new TextBlock { Text = leftLabel, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(left);
        Grid.SetColumn(left, 1);
        TextBlock rightText = new() { Text = rightLabel, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(rightText);
        Grid.SetColumn(rightText, 3);
        row.Children.Add(right);
        Grid.SetColumn(right, 4);
        return row;
    }

    private static Button NewButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6),
        MinWidth = 78
    };
}
