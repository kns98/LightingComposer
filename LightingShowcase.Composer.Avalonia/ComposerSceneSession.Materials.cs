// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerSceneSession.Materials.cs
// Purpose: Material editing side of the scene session.
//
// Applies PBR property, preset, and texture-slot changes, and assigns image files to texture inputs.
// -----------------------------------------------------------------------------

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
    double OcclusionStrength,
    double Specular,
    Vec3 SpecularColor,
    Vec3 SheenColor,
    double SheenRoughness,
    double Anisotropy,
    double AnisotropyRotation,
    double Iridescence,
    double IridescenceIor,
    double IridescenceThicknessMinimum,
    double IridescenceThicknessMaximum);


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
    double Specular,
    Vec3 SpecularColor,
    Vec3 SheenColor,
    double SheenRoughness,
    double Anisotropy,
    double AnisotropyRotation,
    double Iridescence,
    double IridescenceIor,
    double IridescenceThicknessMinimum,
    double IridescenceThicknessMaximum,
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
        OcclusionStrength,
        Specular,
        SpecularColor,
        SheenColor,
        SheenRoughness,
        Anisotropy,
        AnisotropyRotation,
        Iridescence,
        IridescenceIor,
        IridescenceThicknessMinimum,
        IridescenceThicknessMaximum);

    /// <summary>Returns the model for the given texture slot.</summary>
    public ComposerTextureSlotModel TextureSlot(MaterialTextureSlot slot) =>
        TextureSlots.First(entry => entry.Slot == slot);
}

internal sealed partial class ComposerSceneSession
{
    /// <summary>Returns the material model for the given object, or null when the object has no material.</summary>
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
                material.Specular,
                material.SpecularColor,
                material.SheenColor,
                material.SheenRoughness,
                material.Anisotropy,
                material.AnisotropyRotation,
                material.Iridescence,
                material.IridescenceIor,
                material.IridescenceThicknessMinimum,
                material.IridescenceThicknessMaximum,
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

    /// <summary>Applies the given preset to the material of the object with the given id.</summary>
    public bool ApplyMaterialPreset(int id, MaterialPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyMaterialEdit(id, $"Apply material: {preset.Name}", group => group.ApplyMaterialPreset(preset.Material));
    }

    /// <summary>Applies the given preset to every topmost object in the set. Returns how many were updated.</summary>
    public int ApplyMaterialPreset(IEnumerable<int> ids, MaterialPreset preset)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyMaterialEditMany(
            ids,
            $"Apply material: {preset.Name}",
            count => $"Apply material: {preset.Name} ({count} objects)",
            group => group.ApplyMaterialPreset(preset.Material));
    }

    /// <summary>Sets the base color of the object with the given id.</summary>
    public bool SetObjectBaseColor(int id, Vec3 color)
    {
        ValidateColor(color);
        return ApplyMaterialEdit(id, "Set base color", group => group.ApplyBaseColor(color));
    }

    /// <summary>Sets the base color of every topmost object in the set. Returns how many were updated.</summary>
    public int SetObjectBaseColor(IEnumerable<int> ids, Vec3 color)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ValidateColor(color);
        return ApplyMaterialEditMany(
            ids,
            "Set base color",
            count => $"Set base color ({count} objects)",
            group => group.ApplyBaseColor(color));
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
            group.ApplyMaterialProperties(MaterialPropertiesFunc(properties)));
    }

    /// <summary>
    /// Applies explicit PBR/material values to every topmost object in the set
    /// while preserving each triangle's base color and texture maps. Returns
    /// how many objects were updated.
    /// </summary>
    public int SetObjectMaterialProperties(IEnumerable<int> ids, ComposerMaterialProperties properties)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(properties);
        ValidateMaterialProperties(properties);

        return ApplyMaterialEditMany(
            ids,
            "Set material properties",
            count => $"Set material properties ({count} objects)",
            group => group.ApplyMaterialProperties(MaterialPropertiesFunc(properties)));
    }

    /// <summary>Builds the per-material mapping that overwrites direct PBR values but keeps colors and textures.</summary>
    private static Func<Material, Material> MaterialPropertiesFunc(ComposerMaterialProperties properties) => material => new Material(
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
        material.ClearcoatUsesTransmissionTexture,
        properties.Specular,
        properties.SpecularColor,
        material.SpecularTexture,
        material.SpecularColorTexture,
        properties.SheenColor,
        properties.SheenRoughness,
        material.SheenColorTexture,
        material.SheenRoughnessTexture,
        properties.Anisotropy,
        properties.AnisotropyRotation,
        material.AnisotropyTexture,
        properties.Iridescence,
        properties.IridescenceIor,
        properties.IridescenceThicknessMinimum,
        properties.IridescenceThicknessMaximum,
        material.IridescenceTexture,
        material.IridescenceThicknessTexture);

    /// <summary>Assigns the image at the given path to the base-color texture of the object with the given id.</summary>
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
            group => ApplyDecodedTexture(group, slot, texture, tileMeters, boxProjection));
    }

    /// <summary>Assigns an image file to one PBR texture input on every topmost object in the set.</summary>
    public int SetObjectTexture(
        IEnumerable<int> ids,
        MaterialTextureSlot slot,
        string path,
        double tileMeters,
        bool boxProjection)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Select an image texture file.", nameof(path));
        if (!double.IsFinite(tileMeters) || tileMeters <= 1e-6)
            throw new ArgumentOutOfRangeException(nameof(tileMeters), "Texture tile size must be greater than zero meters.");
        if (!Enum.IsDefined(typeof(MaterialTextureSlot), slot))
            throw new ArgumentOutOfRangeException(nameof(slot));

        // Decode once before taking the scene lock; TextureMap is immutable so
        // every object in the batch can safely share the decoded pixels.
        TextureMap texture = TextureMap.FromFile(path);
        string fileName = Path.GetFileName(path);
        return ApplyMaterialEditMany(
            ids,
            $"Set {TextureSlotLabel(slot)} texture: {fileName}",
            count => $"Set {TextureSlotLabel(slot)} texture: {fileName} ({count} objects)",
            group => ApplyDecodedTexture(group, slot, texture, tileMeters, boxProjection));
    }

    /// <summary>Applies one decoded texture to one group. The caller must hold the scene lock.</summary>
    private void ApplyDecodedTexture(SceneObjectGroup group, MaterialTextureSlot slot, TextureMap texture, double tileMeters, bool boxProjection)
    {
        if (!boxProjection)
        {
            group.SetTextureProjectionMode(tileMeters, boxProjection: false);
            if (group.HasParametricPrimitive && group.Children.Count == 0)
                scene.RebuildPrimitiveShadowGeometry(group);
        }
        group.ApplyTexture(slot, texture, tileMeters, boxProjection);
    }

    /// <summary>Clears the base-color texture of the object with the given id.</summary>
    public bool ClearObjectTexture(int id) => ClearObjectTexture(id, MaterialTextureSlot.BaseColor);

    /// <summary>Clears the given texture slot of the object with the given id.</summary>
    public bool ClearObjectTexture(int id, MaterialTextureSlot slot)
    {
        if (!Enum.IsDefined(typeof(MaterialTextureSlot), slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        return ApplyMaterialEdit(id, $"Clear {TextureSlotLabel(slot)} texture", group => group.ClearTexture(slot));
    }

    /// <summary>Clears the given texture slot on every topmost object in the set. Returns how many were updated.</summary>
    public int ClearObjectTexture(IEnumerable<int> ids, MaterialTextureSlot slot)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!Enum.IsDefined(typeof(MaterialTextureSlot), slot))
            throw new ArgumentOutOfRangeException(nameof(slot));
        return ApplyMaterialEditMany(
            ids,
            $"Clear {TextureSlotLabel(slot)} texture",
            count => $"Clear {TextureSlotLabel(slot)} texture ({count} objects)",
            group => group.ClearTexture(slot));
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

    /// <summary>Applies the UV projection mode and the per-slot mapping transform to the given texture slot in one edit.</summary>
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
            group => ApplyTextureMappingCore(group, slot, tileMeters, boxProjection, offsetU, offsetV, scaleU, scaleV, rotationDegrees, wrapU, wrapV));
    }

    /// <summary>Applies the UV projection mode and the per-slot mapping transform to the given texture slot on every topmost object in the set.</summary>
    public int SetObjectTextureMappingAndProjection(
        IEnumerable<int> ids,
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
        ArgumentNullException.ThrowIfNull(ids);
        if (!double.IsFinite(tileMeters) || tileMeters <= 1e-6)
            throw new ArgumentOutOfRangeException(nameof(tileMeters), "Texture tile size must be greater than zero meters.");
        ValidateTextureMapping(offsetU, offsetV, scaleU, scaleV, rotationDegrees, wrapU, wrapV);
        return ApplyMaterialEditMany(
            ids,
            $"Map {TextureSlotLabel(slot)} texture",
            count => $"Map {TextureSlotLabel(slot)} texture ({count} objects)",
            group => ApplyTextureMappingCore(group, slot, tileMeters, boxProjection, offsetU, offsetV, scaleU, scaleV, rotationDegrees, wrapU, wrapV));
    }

    /// <summary>Applies one projection mode plus mapping transform to one group. The caller must hold the scene lock.</summary>
    private void ApplyTextureMappingCore(
        SceneObjectGroup group,
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
    }

    /// <summary>Applies a material change to the given object, then records it as an undoable edit.</summary>
    private bool ApplyMaterialEdit(int id, string description, Action<SceneObjectGroup> apply)
    {
        sceneGate.Wait();
        try
        {
            if (!TryApplyMaterialEditCore(id, description, apply, out IComposerEditCommand? command) || command == null)
                return false;

            editHistory.PushApplied(command);
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

    /// <summary>
    /// Applies a material change to every topmost object in the set and records
    /// one undo step. Returns how many objects were updated.
    /// </summary>
    private int ApplyMaterialEditMany(IEnumerable<int> ids, string singleDescription, Func<int, string> batchDescription, Action<SceneObjectGroup> apply)
    {
        sceneGate.Wait();
        try
        {
            IReadOnlyList<int> topmost = TopmostSelectionCore(ids);
            List<IComposerEditCommand> commands = new(topmost.Count);
            List<int> applied = new(topmost.Count);
            foreach (int id in topmost)
            {
                if (TryApplyMaterialEditCore(id, singleDescription, apply, out IComposerEditCommand? command) && command != null)
                {
                    commands.Add(command);
                    applied.Add(id);
                }
            }
            if (commands.Count == 0)
                return 0;

            editHistory.PushApplied(SingleOrBatch(batchDescription(applied.Count), commands, applied[0]));
            ScenePath = null;

            // Material/texture tables are part of the prepared raster/compute scene;
            // topology is unchanged, but these renderer resources must be rebuilt.
            InvalidateRendererCaches();
            return applied.Count;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>
    /// Applies one material change without touching undo history. The caller must
    /// hold the scene lock and push the returned command when it is not null.
    /// </summary>
    private bool TryApplyMaterialEditCore(int id, string description, Action<SceneObjectGroup> apply, out IComposerEditCommand? command)
    {
        command = null;
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
        command = new GeometryStateEditCommand(description, id, before, after);
        return true;
    }

    /// <summary>Creates a texture-slot model for the given slot, converting the stored rotation to degrees.</summary>
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

    /// <summary>Returns the display label for the given texture slot.</summary>
    internal static string TextureSlotLabel(MaterialTextureSlot slot) => slot switch
    {
        MaterialTextureSlot.BaseColor => "Base color",
        MaterialTextureSlot.MetallicRoughness => "Metallic / roughness",
        MaterialTextureSlot.Normal => "Normal",
        MaterialTextureSlot.Emissive => "Emissive",
        MaterialTextureSlot.Transmission => "Transmission",
        MaterialTextureSlot.Occlusion => "Occlusion",
        MaterialTextureSlot.Specular => "Specular",
        MaterialTextureSlot.SpecularColor => "Specular color",
        MaterialTextureSlot.SheenColor => "Sheen color",
        MaterialTextureSlot.SheenRoughness => "Sheen roughness",
        MaterialTextureSlot.Anisotropy => "Anisotropy",
        MaterialTextureSlot.Iridescence => "Iridescence",
        MaterialTextureSlot.IridescenceThickness => "Iridescence thickness",
        _ => slot.ToString()
    };

    /// <summary>Throws when a texture-mapping offset, scale, rotation, or wrap mode is invalid.</summary>
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

    /// <summary>Throws when the color has a non-finite channel or a channel outside 0 to 1.</summary>
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

    /// <summary>Throws when any material property falls outside its valid range.</summary>
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
        ValidateUnit(properties.Specular, nameof(properties.Specular));
        ValidateColor(properties.SpecularColor);
        ValidateColor(properties.SheenColor);
        ValidateUnit(properties.SheenRoughness, nameof(properties.SheenRoughness));
        ValidateUnit(properties.Anisotropy, nameof(properties.Anisotropy));
        if (!double.IsFinite(properties.AnisotropyRotation))
            throw new ArgumentOutOfRangeException(nameof(properties.AnisotropyRotation), "Anisotropy rotation must be finite radians.");
        ValidateUnit(properties.Iridescence, nameof(properties.Iridescence));
        if (!double.IsFinite(properties.IridescenceIor) || properties.IridescenceIor < 1.0)
            throw new ArgumentOutOfRangeException(nameof(properties.IridescenceIor), "Iridescence IOR must be 1.0 or greater.");
        if (!double.IsFinite(properties.IridescenceThicknessMinimum) || properties.IridescenceThicknessMinimum < 0.0)
            throw new ArgumentOutOfRangeException(nameof(properties.IridescenceThicknessMinimum), "Iridescence minimum thickness must be zero or greater nanometers.");
        if (!double.IsFinite(properties.IridescenceThicknessMaximum) || properties.IridescenceThicknessMaximum < 0.0)
            throw new ArgumentOutOfRangeException(nameof(properties.IridescenceThicknessMaximum), "Iridescence maximum thickness must be zero or greater nanometers.");
    }

    /// <summary>Throws when the named value falls outside the 0 to 1 range.</summary>
    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(name, "Value must be between 0 and 1.");
    }
}
