/*
 * `ComposerSceneSession` is the synchronization and transaction boundary around the live scene. Code in these
 * partial files takes the session gate before touching shared mutable scene data, records logical edits for
 * undo/redo, preserves procedural metadata when possible, and invalidates renderer caches when geometry/material
 * state changes. UI code should ask the session to perform edits instead of modifying scene objects directly.
 *
 * `ComposerMaterialProperties` carries only direct PBR surface values for a material edit; texture identity and
 * object identity are deliberately excluded so this snapshot can be applied independently of texture/projection
 * changes. Record value semantics make the snapshot easy to compare/copy without sharing mutable state. Its
 * constructor values (`Metallic`, `Roughness`, `Transmission`, `Alpha`, `Emission`, `EmissionColor`, `AlphaMode`,
 * `AlphaCutoff`, `DoubleSided`, `Ior`, `Thickness`, `AttenuationColor`) travel together because consumers need a
 * consistent snapshot rather than reading those values independently from mutable objects.
 *
 * `ComposerTextureSlotModel` is the inspector-facing snapshot of one texture channel. Besides resource identity
 * it carries UV offset/scale/rotation and U/V wrap modes, which means the UI can edit sampling behavior without
 * receiving a live mutable `Material`. Record value semantics make the snapshot easy to compare/copy without
 * sharing mutable state. Its constructor values (`Slot`, `Label`, `Name`, `Path`, `OffsetU`, `OffsetV`, `ScaleU`,
 * `ScaleV`, `RotationDegrees`, `WrapU`, `WrapV`) travel together because consumers need a consistent snapshot
 * rather than reading those values independently from mutable objects.
 *
 * `ComposerMaterialModel` is the complete material-inspector snapshot for one object: object identity, direct PBR
 * values, texture slots, and stored projection/tiling metadata travel together so a refresh sees one internally
 * consistent material state. Record value semantics make the snapshot easy to compare/copy without sharing
 * mutable state. Its constructor values (`ObjectId`, `ObjectName`, `BaseColor`, `Metallic`, `Roughness`,
 * `Transmission`, `Alpha`, `Emission`, `EmissionColor`, `AlphaMode`, `AlphaCutoff`, `DoubleSided`) travel
 * together because consumers need a consistent snapshot rather than reading those values independently from
 * mutable objects.
 *
 * `ComposerSceneSession` is one slice of a partial type whose shared state/invariants continue in sibling files.
 *
 * `HasTexture` returns true when either the texture’s resource name or source path is nonblank. The model
 * therefore treats both embedded/named textures and path-backed textures as populated slots.
 *
 * `GetMaterialModel` takes the session lock, resolves the selected group’s first material, and copies direct PBR
 * values, every texture slot’s sampling settings, and stored projection/tiling metadata into an inspector
 * snapshot. Returning `null` means the id is missing or has no material. It takes `sceneGate` before touching the
 * live scene and releases it in `finally`, so readers/renderers cannot observe a half-completed mutation.
 * Procedural/object-library metadata is accessed through the registry so editable primitive identity survives
 * operations that should preserve it.
 *
 * `ApplyMaterialPreset` applies the preset’s complete material definition through the common material-edit
 * transaction, so the preset change participates in undo and renderer-cache invalidation.
 *
 * `SetObjectBaseColor` validates that all RGB channels are finite values in 0–1, then changes only base color
 * through the common material-edit transaction.
 *
 * `SetObjectMaterialProperties` rebuilds each material in the selected subtree with the new direct PBR values
 * while preserving base color, light id, and every assigned texture map. This keeps scalar property editing
 * independent from preset and texture tools.
 *
 * `SetObjectTexture` is the base-color convenience overload; it delegates to the slot-aware form so validation,
 * projection handling, and undo behavior stay identical.
 *
 * `ClearObjectTexture` is the base-color convenience overload that delegates to the slot-aware form.
 *
 * `ClearObjectTexture` validates the requested texture slot and clears only that slot through the common material
 * transaction, leaving the other PBR texture inputs untouched.
 *
 * `SetObjectTextureProjectionMode` switches the shared geometry UV source between box-projected real-world tiling
 * and existing/authored UVs. Returning a procedural leaf to authored UVs also rebuilds primitive shadow geometry
 * so rendering uses the regenerated mapping.
 *
 * `ApplyMaterialEdit` is the transaction wrapper for every material mutation. It commits a pending
 * procedural-parameter preview for the same object, captures before/after geometry/material state, rebuilds world
 * and selection-derived data, pushes one undo command, marks the scene unsaved, and invalidates renderer
 * material/texture caches. It takes `sceneGate` before touching the live scene and releases it in `finally`, so
 * readers/renderers cannot observe a half-completed mutation. History bookkeeping surrounds the mutation so
 * internal steps collapse into the intended user-level undo transaction.
 *
 * `CreateTextureSlotModel` converts a nullable `TextureMap` into inspector values with neutral defaults—zero
 * offset/rotation, unit scale, repeat wrapping—so empty and populated slots use the same editor model.
 *
 * `TextureSlotLabel` maps each texture-slot enum to the concise editor label users see, with `ToString()` as a
 * fallback for future enum values.
 *
 * `ValidateColor` enforces the normalized finite 0–1 RGB range used by the material/rendering pipeline.
 *
 * `ValidateMaterialProperties` validates the complete direct-PBR edit as one unit so no invalid scalar can leave
 * a partially applied material.
 *
 * `ValidateUnit` checks the invariants required for unit and throws/reports an error for non-finite,
 * out-of-range, or otherwise unsupported values. Keeping validation next to mutation prevents invalid state from
 * propagating into renderers.
 */
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record ComposerMaterialProperties(
    double Metallic,
    double Roughness,
    double Transmission,
    double Alpha,
    double Emission,
    Vec3 EmissionColor,
    MaterialAlphaMode AlphaMode,
    double AlphaCutoff,
    bool DoubleSided,
    double Ior,
    double Thickness,
    Vec3 AttenuationColor,
    double AttenuationDistance,
    double Clearcoat,
    double ClearcoatRoughness,
    double NormalScale,
    double OcclusionStrength);


internal sealed record ComposerTextureSlotModel(
    MaterialTextureSlot Slot,
    string Label,
    string? Name,
    string? Path,
    double OffsetU,
    double OffsetV,
    double ScaleU,
    double ScaleV,
    double RotationDegrees,
    TextureAddressMode WrapU,
    TextureAddressMode WrapV)
{
    public bool HasTexture => !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(Path);
}

internal sealed record ComposerMaterialModel(
    int ObjectId,
    string ObjectName,
    Vec3 BaseColor,
    double Metallic,
    double Roughness,
    double Transmission,
    double Alpha,
    double Emission,
    Vec3 EmissionColor,
    MaterialAlphaMode AlphaMode,
    double AlphaCutoff,
    bool DoubleSided,
    double Ior,
    double Thickness,
    Vec3 AttenuationColor,
    double AttenuationDistance,
    double Clearcoat,
    double ClearcoatRoughness,
    double NormalScale,
    double OcclusionStrength,
    string? TextureName,
    string? TexturePath,
    bool HasStoredTextureProjection,
    bool UsesBoxProjection,
    double TextureTileMeters,
    IReadOnlyList<ComposerTextureSlotModel> TextureSlots)
{
    public ComposerMaterialProperties DirectProperties => new(
        Metallic,
        Roughness,
        Transmission,
        Alpha,
        Emission,
        EmissionColor,
        AlphaMode,
        AlphaCutoff,
        DoubleSided,
        Ior,
        Thickness,
        AttenuationColor,
        AttenuationDistance,
        Clearcoat,
        ClearcoatRoughness,
        NormalScale,
        OcclusionStrength);

    public ComposerTextureSlotModel TextureSlot(MaterialTextureSlot slot) =>
        TextureSlots.First(entry => entry.Slot == slot);
}

internal sealed partial class ComposerSceneSession
{
    public ComposerMaterialModel? GetMaterialModel(int id)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(id);
            Material? material = group?.FirstMaterialOrDefault();
            if (group == null || material == null)
                return null;

            bool boxProjection = false;
            double tileMeters = 0.25;
            bool hasStoredProjection = ObjectLibraryRegistry.TryGetParametricTextureProjection(group, out boxProjection, out tileMeters);
            return new ComposerMaterialModel(
                group.Id,
                group.Name,
                material.Color,
                material.Metallic,
                material.Roughness,
                material.Transmission,
                material.Alpha,
                material.Emission,
                material.EmissionColor,
                material.AlphaMode,
                material.AlphaCutoff,
                material.DoubleSided,
                material.Ior,
                material.Thickness,
                material.AttenuationColor,
                material.AttenuationDistance,
                material.Clearcoat,
                material.ClearcoatRoughness,
                material.NormalScale,
                material.OcclusionStrength,
                material.Texture?.Name,
                material.Texture?.SourcePath,
                hasStoredProjection,
                boxProjection,
                tileMeters,
                Enum.GetValues<MaterialTextureSlot>()
                    .Select(slot => CreateTextureSlotModel(slot, material.GetTexture(slot)))
                    .ToArray());
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public bool ApplyMaterialPreset(int id, MaterialPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyMaterialEdit(id, $"Apply material: {preset.Name}", group => group.ApplyMaterialPreset(preset.Material));
    }

    public bool SetObjectBaseColor(int id, Vec3 color)
    {
        ValidateColor(color);
        return ApplyMaterialEdit(id, "Set base color", group => group.ApplyBaseColor(color));
    }

    /// <summary>
    /// Applies explicit PBR/material values to every material in the selected subtree while
    /// preserving each triangle's base color and all assigned texture maps. This keeps
    /// direct property editing independent from the material-library preset and texture tools.
    /// </summary>
    public bool SetObjectMaterialProperties(int id, ComposerMaterialProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ValidateMaterialProperties(properties);

        return ApplyMaterialEdit(id, "Set material properties", group =>
            group.ApplyMaterialProperties(material => new Material(
                material.Color,
                properties.Emission,
                material.LightId,
                material.Texture,
                properties.EmissionColor,
                material.EmissiveTexture,
                properties.Alpha,
                properties.AlphaMode == MaterialAlphaMode.Blend,
                properties.Metallic,
                properties.Roughness,
                properties.Transmission,
                material.MetallicRoughnessTexture,
                material.NormalTexture,
                material.OcclusionTexture,
                properties.NormalScale,
                properties.OcclusionStrength,
                properties.AlphaMode,
                properties.AlphaCutoff,
                properties.DoubleSided,
                material.TransmissionTexture,
                properties.Ior,
                properties.Thickness,
                properties.AttenuationColor,
                properties.AttenuationDistance,
                properties.Clearcoat,
                properties.ClearcoatRoughness,
                material.ClearcoatUsesTransmissionTexture)));
    }

    public bool SetObjectTexture(int id, string path, double tileMeters, bool boxProjection) =>
        SetObjectTexture(id, MaterialTextureSlot.BaseColor, path, tileMeters, boxProjection);

    /// <summary>Assigns an image file to one PBR texture input.</summary>
    public bool SetObjectTexture(
        int id,
        MaterialTextureSlot slot,
        string path,
        double tileMeters,
        bool boxProjection)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Select an image texture file.", nameof(path));
        if (!double.IsFinite(tileMeters) || tileMeters <= 1e-6)
            throw new ArgumentOutOfRangeException(nameof(tileMeters), "Texture tile size must be greater than zero meters.");
        if (!Enum.IsDefined(typeof(MaterialTextureSlot), slot))
            throw new ArgumentOutOfRangeException(nameof(slot));

        // Decode before taking the scene lock; large image files should not block
        // viewport/session reads while their pixels are being loaded.
        TextureMap texture = TextureMap.FromFile(path);
        return ApplyMaterialEdit(
            id,
            $"Set {TextureSlotLabel(slot)} texture: {Path.GetFileName(path)}",
            group =>
            {
                if (!boxProjection)
                {
                    group.SetTextureProjectionMode(tileMeters, boxProjection: false);
                    if (group.HasParametricPrimitive && group.Children.Count == 0)
                        scene.RebuildPrimitiveShadowGeometry(group);
                }
                group.ApplyTexture(slot, texture, tileMeters, boxProjection);
            });
    }

    public bool ClearObjectTexture(int id) => ClearObjectTexture(id, MaterialTextureSlot.BaseColor);

    public bool ClearObjectTexture(int id, MaterialTextureSlot slot)
    {
        if (!Enum.IsDefined(typeof(MaterialTextureSlot), slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        return ApplyMaterialEdit(id, $"Clear {TextureSlotLabel(slot)} texture", group => group.ClearTexture(slot));
    }

    /// <summary>
    /// Applies per-image UV transform/addressing controls to one texture input. Rotation
    /// is entered in degrees in the Composer UI and stored as radians on TextureMap.
    /// </summary>
    public bool SetObjectTextureMapping(
        int id,
        MaterialTextureSlot slot,
        double offsetU,
        double offsetV,
        double scaleU,
        double scaleV,
        double rotationDegrees,
        TextureAddressMode wrapU,
        TextureAddressMode wrapV)
    {
        ValidateTextureMapping(offsetU, offsetV, scaleU, scaleV, rotationDegrees, wrapU, wrapV);
        return ApplyMaterialEdit(
            id,
            $"Map {TextureSlotLabel(slot)} texture",
            group => group.ApplyTextureMapping(
                slot,
                offsetU,
                offsetV,
                scaleU,
                scaleV,
                rotationDegrees * Math.PI / 180.0,
                wrapU,
                wrapV));
    }

    /// <summary>
    /// Chooses the shared geometry UV source. Box projection generates one UV channel in
    /// real-world meters for all texture slots; authored/current UV mode leaves triangle
    /// UVs unchanged. Per-slot transforms remain independent.
    /// </summary>
    public bool SetObjectTextureProjectionMode(int id, double tileMeters, bool boxProjection)
    {
        if (!double.IsFinite(tileMeters) || tileMeters <= 1e-6)
            throw new ArgumentOutOfRangeException(nameof(tileMeters), "Texture tile size must be greater than zero meters.");
        return ApplyMaterialEdit(
            id,
            boxProjection ? "Use box-projected texture UVs" : "Use authored/current texture UVs",
            group =>
            {
                group.SetTextureProjectionMode(tileMeters, boxProjection);
                if (!boxProjection && group.HasParametricPrimitive && group.Children.Count == 0)
                    scene.RebuildPrimitiveShadowGeometry(group);
            });
    }

    public bool SetObjectTextureMappingAndProjection(
        int id,
        MaterialTextureSlot slot,
        double tileMeters,
        bool boxProjection,
        double offsetU,
        double offsetV,
        double scaleU,
        double scaleV,
        double rotationDegrees,
        TextureAddressMode wrapU,
        TextureAddressMode wrapV)
    {
        if (!double.IsFinite(tileMeters) || tileMeters <= 1e-6)
            throw new ArgumentOutOfRangeException(nameof(tileMeters), "Texture tile size must be greater than zero meters.");
        ValidateTextureMapping(offsetU, offsetV, scaleU, scaleV, rotationDegrees, wrapU, wrapV);
        return ApplyMaterialEdit(
            id,
            $"Map {TextureSlotLabel(slot)} texture",
            group =>
            {
                group.SetTextureProjectionMode(tileMeters, boxProjection);
                if (!boxProjection && group.HasParametricPrimitive && group.Children.Count == 0)
                    scene.RebuildPrimitiveShadowGeometry(group);
                group.ApplyTextureMapping(
                    slot,
                    offsetU,
                    offsetV,
                    scaleU,
                    scaleV,
                    rotationDegrees * Math.PI / 180.0,
                    wrapU,
                    wrapV);
            });
    }

    private bool ApplyMaterialEdit(int id, string description, Action<SceneObjectGroup> apply)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(id);
            if (group == null || group.FirstMaterialOrDefault() == null)
                return false;

            // A modeless procedural-parameter editor can hold a geometry baseline.
            // Commit it before a material mutation so closing that window later
            // cannot restore stale pre-material triangles.
            if (primitiveParameterPreview?.GroupId == id)
                CommitPrimitiveParameterEditCore(id);

            BakedGeometryState before = BakedGeometryState.Capture(group);
            apply(group);
            BakedGeometryState after = BakedGeometryState.Capture(group);

            scene.RebuildWorldGeometry();
            meshTopologyByGroup.Clear();
            RebuildSelectionOverlayCache();
            editHistory.PushApplied(new GeometryStateEditCommand(description, id, before, after));
            ScenePath = null;

            // Material/texture tables are part of the prepared raster/compute scene;
            // topology is unchanged, but these renderer resources must be rebuilt.
            InvalidateRendererCaches();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    private static ComposerTextureSlotModel CreateTextureSlotModel(MaterialTextureSlot slot, TextureMap? texture) => new(
        slot,
        TextureSlotLabel(slot),
        texture?.Name,
        texture?.SourcePath,
        texture?.OffsetU ?? 0.0,
        texture?.OffsetV ?? 0.0,
        texture?.ScaleU ?? 1.0,
        texture?.ScaleV ?? 1.0,
        (texture?.Rotation ?? 0.0) * 180.0 / Math.PI,
        texture?.WrapU ?? TextureAddressMode.Repeat,
        texture?.WrapV ?? TextureAddressMode.Repeat);

    internal static string TextureSlotLabel(MaterialTextureSlot slot) => slot switch
    {
        MaterialTextureSlot.BaseColor => "Base color",
        MaterialTextureSlot.MetallicRoughness => "Metallic / roughness",
        MaterialTextureSlot.Normal => "Normal",
        MaterialTextureSlot.Emissive => "Emissive",
        MaterialTextureSlot.Transmission => "Transmission",
        MaterialTextureSlot.Occlusion => "Occlusion",
        _ => slot.ToString()
    };

    private static void ValidateTextureMapping(
        double offsetU,
        double offsetV,
        double scaleU,
        double scaleV,
        double rotationDegrees,
        TextureAddressMode wrapU,
        TextureAddressMode wrapV)
    {
        if (!double.IsFinite(offsetU) || !double.IsFinite(offsetV))
            throw new ArgumentOutOfRangeException(nameof(offsetU), "Texture offsets must be finite values.");
        if (!double.IsFinite(scaleU) || Math.Abs(scaleU) <= 1e-9 ||
            !double.IsFinite(scaleV) || Math.Abs(scaleV) <= 1e-9)
            throw new ArgumentOutOfRangeException(nameof(scaleU), "Texture U/V scale must be finite and non-zero.");
        if (!double.IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees), "Texture rotation must be finite degrees.");
        if (!Enum.IsDefined(typeof(TextureAddressMode), wrapU) || !Enum.IsDefined(typeof(TextureAddressMode), wrapV))
            throw new ArgumentOutOfRangeException(nameof(wrapU), "Choose a valid texture address mode.");
    }

    private static void ValidateColor(Vec3 color)
    {
        if (!double.IsFinite(color.X) || !double.IsFinite(color.Y) || !double.IsFinite(color.Z) ||
            color.X < 0.0 || color.X > 1.0 ||
            color.Y < 0.0 || color.Y > 1.0 ||
            color.Z < 0.0 || color.Z > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(color), "Color channels must be finite values from 0 to 1.");
        }
    }

    private static void ValidateMaterialProperties(ComposerMaterialProperties properties)
    {
        ValidateUnit(properties.Metallic, nameof(properties.Metallic));
        if (!double.IsFinite(properties.Roughness) || properties.Roughness < 0.02 || properties.Roughness > 1.0)
            throw new ArgumentOutOfRangeException(nameof(properties.Roughness), "Roughness must be between 0.02 and 1.");
        ValidateUnit(properties.Transmission, nameof(properties.Transmission));
        ValidateUnit(properties.Alpha, nameof(properties.Alpha));
        if (!double.IsFinite(properties.Emission) || properties.Emission < 0.0 || properties.Emission > 100000.0)
            throw new ArgumentOutOfRangeException(nameof(properties.Emission), "Emission strength must be between 0 and 100000.");
        ValidateColor(properties.EmissionColor);
        ValidateUnit(properties.AlphaCutoff, nameof(properties.AlphaCutoff));
        if (!double.IsFinite(properties.Ior) || properties.Ior < 1.0 || properties.Ior > 2.333)
            throw new ArgumentOutOfRangeException(nameof(properties.Ior), "IOR must be between 1.0 and 2.333.");
        if (!double.IsFinite(properties.Thickness) || properties.Thickness < 0.0)
            throw new ArgumentOutOfRangeException(nameof(properties.Thickness), "Thickness must be zero or greater meters.");
        ValidateColor(properties.AttenuationColor);
        if (!double.IsFinite(properties.AttenuationDistance) || properties.AttenuationDistance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(properties.AttenuationDistance), "Attenuation distance must be zero or greater meters.");
        ValidateUnit(properties.Clearcoat, nameof(properties.Clearcoat));
        ValidateUnit(properties.ClearcoatRoughness, nameof(properties.ClearcoatRoughness));
        if (!double.IsFinite(properties.NormalScale) || properties.NormalScale < -8.0 || properties.NormalScale > 8.0)
            throw new ArgumentOutOfRangeException(nameof(properties.NormalScale), "Normal scale must be between -8 and 8.");
        ValidateUnit(properties.OcclusionStrength, nameof(properties.OcclusionStrength));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(name, "Value must be between 0 and 1.");
    }
}
