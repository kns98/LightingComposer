using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record ComposerMaterialModel(
    int ObjectId,
    string ObjectName,
    Vec3 BaseColor,
    double Metallic,
    double Roughness,
    double Transmission,
    double Alpha,
    string? TextureName,
    string? TexturePath,
    bool HasStoredTextureProjection,
    bool UsesBoxProjection,
    double TextureTileMeters);

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
                material.Texture?.Name,
                material.Texture?.SourcePath,
                hasStoredProjection,
                boxProjection,
                tileMeters);
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

    public bool SetObjectTexture(int id, string path, double tileMeters, bool boxProjection)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Select an image texture file.", nameof(path));
        if (!double.IsFinite(tileMeters) || tileMeters <= 1e-6)
            throw new ArgumentOutOfRangeException(nameof(tileMeters), "Texture tile size must be greater than zero meters.");

        // Decode before taking the scene lock; large image files should not block
        // viewport/session reads while their pixels are being loaded.
        TextureMap texture = TextureMap.FromFile(path);
        return ApplyMaterialEdit(
            id,
            $"Set texture: {Path.GetFileName(path)}",
            group => group.ApplyTexture(texture, tileMeters, boxProjection));
    }

    public bool ClearObjectTexture(int id)
    {
        return ApplyMaterialEdit(id, "Clear base-color texture", group => group.ClearTexture());
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
}
