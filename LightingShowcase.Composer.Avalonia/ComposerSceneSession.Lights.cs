// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerSceneSession.Lights.cs
// Purpose: Light editing side of the scene session.
//
// Adds, removes, and updates SceneLight objects and exposes them as editor models for the Lighting Editor window.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed record ComposerLightModel(
    int Index,
    string Id,
    SceneLightKind Kind,
    Vec3 Position,
    Vec3 Direction,
    Vec3 Color,
    double Intensity,
    double Range,
    double InnerConeAngle,
    double OuterConeAngle,
    bool Enabled,
    bool CastsShadow,
    bool IsImported,
    bool IsDefault)
{
    public string DisplayLabel => $"{Index + 1}. {Id} ({Kind}){(Enabled ? string.Empty : " [off]")}";
}

internal sealed partial class ComposerSceneSession
{
    private int? selectedLightIndex;
    private bool showLightMarkers = true;
    private SceneLight[]? lightMoveBefore;
    private int? lightMoveIndex;

    public int? SelectedLightIndex
    {
        get
        {
            sceneGate.Wait();
            try { return selectedLightIndex; }
            finally { sceneGate.Release(); }
        }
    }

    public bool ShowLightMarkers
    {
        get
        {
            sceneGate.Wait();
            try { return showLightMarkers; }
            finally { sceneGate.Release(); }
        }
    }

    /// <summary>Returns editor models for every light in the scene.</summary>
    public IReadOnlyList<ComposerLightModel> GetLightInfos()
    {
        sceneGate.Wait();
        try
        {
            return scene.Lights.Select((light, index) => ToModel(index, light)).ToArray();
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns the editor model for the light at the given index, or null when out of range.</summary>
    public ComposerLightModel? GetLightInfo(int index)
    {
        sceneGate.Wait();
        try
        {
            return index >= 0 && index < scene.Lights.Count ? ToModel(index, scene.Lights[index]) : null;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Selects the light at the given index and clears the object selection.</summary>
    public bool SetSelectedLight(int? index)
    {
        sceneGate.Wait();
        try
        {
            int? normalized = index.HasValue && index.Value >= 0 && index.Value < scene.Lights.Count
                ? index
                : null;
            bool changed = selectedLightIndex != normalized || (normalized.HasValue && selectedObjectId.HasValue);
            selectedLightIndex = normalized;

            if (normalized.HasValue)
            {
                selectedObjectId = null;
                selectedObjectIds.Clear();
                selectedTriangleGroupId = null;
                selectedTriangleIndex = null;
                selectedMeshSelection = null;
                hoveredMeshSelection = null;
                meshMovePreviewLocal = Vec3.Zero;
                meshMovePreviewWorld = Vec3.Zero;
                selectionMode = ComposerSelectionMode.Object;
                selectedOverlayBounds = null;
                selectedOverlayTriangles = Array.Empty<Triangle>();
            }

            return changed;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Shows or hides light markers and returns true when the setting changed.</summary>
    public bool SetShowLightMarkers(bool visible)
    {
        sceneGate.Wait();
        try
        {
            if (showLightMarkers == visible)
                return false;
            showLightMarkers = visible;
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Adds a light of the given kind above the scene center and selects it.</summary>
    public int AddLight(SceneLightKind kind)
    {
        sceneGate.Wait();
        try
        {
            SceneLight[] before = CloneLights();
            Aabb? sceneBounds = scene.GetSceneBounds();
            Vec3 center = sceneBounds is Aabb bounds ? (bounds.Min + bounds.Max) * 0.5 : new Vec3(0, 0.5, 0);
            Vec3 extent = sceneBounds is Aabb b ? b.Max - b.Min : new Vec3(2, 2, 2);
            double lift = Math.Max(1.0, Math.Max(extent.Y, extent.Length() * 0.3));
            Vec3 position = center + new Vec3(0.0, lift, 0.0);
            string baseName = kind switch
            {
                SceneLightKind.Directional => "directional",
                SceneLightKind.Spot => "spot",
                _ => "point"
            };
            string id = UniqueLightId(baseName);
            Vec3 direction = new Vec3(0.0, -1.0, 0.0);
            double range = kind == SceneLightKind.Directional ? 0.0 : Math.Max(0.0, extent.Length() * 2.0);
            scene.Lights.Add(new SceneLight(
                id,
                position,
                new Vec3(1.0, 1.0, 1.0),
                5.0,
                enabled: true,
                kind: kind,
                direction: direction,
                range: range,
                innerConeAngle: Math.PI / 12.0,
                outerConeAngle: Math.PI / 6.0,
                castsShadow: true));

            selectedLightIndex = scene.Lights.Count - 1;
            selectedObjectId = null;
            selectedObjectIds.Clear();
            RebuildSelectionOverlayCache();
            CommitLightCollectionEdit($"Add {kind.ToString().ToLowerInvariant()} light", before);
            return selectedLightIndex.Value;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Deletes the light at the given index and repairs the selection.</summary>
    public bool DeleteLight(int index)
    {
        sceneGate.Wait();
        try
        {
            if (index < 0 || index >= scene.Lights.Count)
                return false;

            SceneLight[] before = CloneLights();
            scene.Lights.RemoveAt(index);
            if (scene.Lights.Count == 0)
                selectedLightIndex = null;
            else if (selectedLightIndex == index)
                selectedLightIndex = Math.Min(index, scene.Lights.Count - 1);
            else if (selectedLightIndex is int selected && selected > index)
                selectedLightIndex = selected - 1;

            lightMoveBefore = null;
            lightMoveIndex = null;
            CommitLightCollectionEdit("Delete light", before);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Applies the edited model to the light at the given index after validating its values.</summary>
    public bool UpdateLight(int index, ComposerLightModel model)
    {
        sceneGate.Wait();
        try
        {
            if (index < 0 || index >= scene.Lights.Count)
                return false;
            if (!IsFinite(model.Position) || !IsFinite(model.Direction) || !IsFinite(model.Color) ||
                !double.IsFinite(model.Intensity) || !double.IsFinite(model.Range) ||
                !double.IsFinite(model.InnerConeAngle) || !double.IsFinite(model.OuterConeAngle))
            {
                return false;
            }

            SceneLight[] before = CloneLights();
            SceneLight target = scene.Lights[index];
            string id = string.IsNullOrWhiteSpace(model.Id) ? target.Id : model.Id.Trim();
            Vec3 direction = model.Direction.Normalize();
            if (direction.Length() < 1e-8)
                direction = new Vec3(0.0, 0.0, -1.0);

            target.Id = id;
            target.Kind = model.Kind;
            target.Position = model.Position;
            target.Direction = direction;
            target.Color = ClampColor(model.Color);
            target.Intensity = Math.Max(0.0, model.Intensity);
            target.Range = Math.Max(0.0, model.Range);
            target.InnerConeAngle = Math.Clamp(model.InnerConeAngle, 0.0, Math.PI / 2.0);
            target.OuterConeAngle = Math.Clamp(Math.Max(target.InnerConeAngle, model.OuterConeAngle), 0.0, Math.PI / 2.0);
            target.Enabled = model.Enabled;
            target.CastsShadow = model.CastsShadow;
            // Imported/default provenance is intentionally retained rather than editable.

            CommitLightCollectionEdit("Edit light", before);
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns the world-space center of the object with the given id.</summary>
    public Vec3? GetObjectAimCenter(int objectId)
    {
        sceneGate.Wait();
        try
        {
            SceneObjectGroup? group = scene.GroupById(objectId);
            if (group == null || !group.SelfAndDescendants().Any(candidate => candidate.LocalTriangles.Count > 0))
                return null;

            Aabb bounds = group.GetWorldBounds(includeHidden: true);
            Vec3 center = (bounds.Min + bounds.Max) * 0.5;
            return IsFinite(center) ? center : null;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns the index of the light marker under the given normalized viewport position.</summary>
    public int? PickLightMarker(
        CameraDefinition camera,
        double normalizedX,
        double normalizedY,
        int width,
        int height)
    {
        sceneGate.Wait();
        try
        {
            if (!showLightMarkers || width <= 0 || height <= 0)
                return null;
            double imageX = normalizedX * width;
            double imageY = normalizedY * height;
            return ComposerOverlayRenderer.TryPickLightMarker(
                scene.Lights,
                camera,
                width,
                height,
                imageX,
                imageY,
                out int lightIndex)
                ? lightIndex
                : null;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Returns a point bounding box at the selected light position for gizmo hit testing.</summary>
    public Aabb? GetSelectedLightGizmoBounds()
    {
        sceneGate.Wait();
        try
        {
            if (!showLightMarkers || selectedLightIndex is not int index || index < 0 || index >= scene.Lights.Count)
                return null;
            Vec3 p = scene.Lights[index].Position;
            return new Aabb(p, p);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Starts a move of the selected light and returns its current model.</summary>
    public ComposerLightModel? BeginSelectedLightMove()
    {
        sceneGate.Wait();
        try
        {
            if (!showLightMarkers || selectedLightIndex is not int index || index < 0 || index >= scene.Lights.Count)
                return null;
            lightMoveBefore = CloneLights();
            lightMoveIndex = index;
            return ToModel(index, scene.Lights[index]);
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Moves the selected light to the given position without committing an undo step.</summary>
    public bool PreviewSelectedLightPosition(Vec3 position)
    {
        sceneGate.Wait();
        try
        {
            if (lightMoveBefore == null || lightMoveIndex is not int index || index < 0 || index >= scene.Lights.Count)
                return false;
            if (!IsFinite(position))
                return false;
            scene.Lights[index].Position = position;
            ScenePath = null;
            InvalidateRendererCaches();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Commits the in-progress light move to the undo history.</summary>
    public bool CommitSelectedLightMove()
    {
        sceneGate.Wait();
        try
        {
            if (lightMoveBefore == null || lightMoveIndex is not int index || index < 0 || index >= scene.Lights.Count)
                return false;

            SceneLight[] before = lightMoveBefore;
            lightMoveBefore = null;
            lightMoveIndex = null;
            if (SameLights(before, scene.Lights))
                return false;

            editHistory.PushApplied(new LightCollectionEditCommand("Move light", before, scene.Lights));
            ScenePath = null;
            InvalidateRendererCaches();
            return true;
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Restores the light positions from before the in-progress move.</summary>
    public void CancelSelectedLightMove()
    {
        sceneGate.Wait();
        try
        {
            if (lightMoveBefore == null)
                return;
            RestoreLights(lightMoveBefore);
            lightMoveBefore = null;
            lightMoveIndex = null;
            InvalidateRendererCaches();
        }
        finally
        {
            sceneGate.Release();
        }
    }

    /// <summary>Pushes a light collection change onto the undo history when the lights differ.</summary>
    private void CommitLightCollectionEdit(string description, SceneLight[] before)
    {
        if (SameLights(before, scene.Lights))
            return;
        editHistory.PushApplied(new LightCollectionEditCommand(description, before, scene.Lights));
        ScenePath = null;
        InvalidateRendererCaches();
    }

    /// <summary>Returns a deep copy of the current scene lights.</summary>
    private SceneLight[] CloneLights() => scene.Lights.Select(LightCollectionEditCommand.Clone).ToArray();

    /// <summary>Replaces the scene lights with clones of the given lights and repairs the selection.</summary>
    private void RestoreLights(IEnumerable<SceneLight> lights)
    {
        scene.Lights.Clear();
        foreach (SceneLight light in lights)
            scene.Lights.Add(LightCollectionEditCommand.Clone(light));
        if (selectedLightIndex is int selected && (selected < 0 || selected >= scene.Lights.Count))
            selectedLightIndex = scene.Lights.Count == 0 ? null : scene.Lights.Count - 1;
    }

    /// <summary>Returns a light id that is unique in the scene by appending a number when needed.</summary>
    private string UniqueLightId(string baseName)
    {
        HashSet<string> used = scene.Lights.Select(light => light.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName))
            return baseName;
        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName}_{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>Converts the scene light at the given index into an editor model.</summary>
    private static ComposerLightModel ToModel(int index, SceneLight light) => new(
        index,
        light.Id,
        light.Kind,
        light.Position,
        light.Direction,
        light.Color,
        light.Intensity,
        light.Range,
        light.InnerConeAngle,
        light.OuterConeAngle,
        light.Enabled,
        light.CastsShadow,
        light.IsImported,
        light.IsDefault);

    /// <summary>Clamps each color component to the 0 to 1 range.</summary>
    private static Vec3 ClampColor(Vec3 color) => new(
        Math.Clamp(color.X, 0.0, 1.0),
        Math.Clamp(color.Y, 0.0, 1.0),
        Math.Clamp(color.Z, 0.0, 1.0));

    /// <summary>Returns true when every component of the vector is finite.</summary>
    private static bool IsFinite(Vec3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    /// <summary>Returns true when both vectors hold identical components.</summary>
    private static bool EqualVec(Vec3 a, Vec3 b) =>
        a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>Returns true when both light lists hold identical lights in the same order.</summary>
    private static bool SameLights(IReadOnlyList<SceneLight> a, IReadOnlyList<SceneLight> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            SceneLight x = a[i];
            SceneLight y = b[i];
            if (x.Id != y.Id || x.Kind != y.Kind || !EqualVec(x.Position, y.Position) || !EqualVec(x.Direction, y.Direction) ||
                !EqualVec(x.Color, y.Color) || x.Intensity != y.Intensity || x.Range != y.Range ||
                x.InnerConeAngle != y.InnerConeAngle || x.OuterConeAngle != y.OuterConeAngle ||
                x.Enabled != y.Enabled || x.CastsShadow != y.CastsShadow ||
                x.IsImported != y.IsImported || x.IsDefault != y.IsDefault)
            {
                return false;
            }
        }
        return true;
    }
}
