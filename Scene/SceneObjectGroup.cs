// -----------------------------------------------------------------------------
// File: Scene/SceneObjectGroup.cs
// Purpose: Editable recursive object group.
//
// A group can now contain triangles and child groups. Top-level groups are shown
// in the editor as selectable objects; ungrouping promotes child groups back into
// the scene so compound props such as tables can be edited as legs/top pieces.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Editable scene node containing local triangles, transform state, and optional child groups.</summary>
public sealed class SceneObjectGroup
{
    public int Id { get; }
    public string Name { get; set; }
    public List<Triangle> LocalTriangles { get; } = new();
    public List<SceneObjectGroup> Children { get; } = new();
    public SceneObjectGroup? Parent { get; internal set; }
    public Vec3 Pivot { get; private set; }
    public Vec3 Position { get; set; } = Vec3.Zero;
    public Vec3 Rotation { get; set; } = Vec3.Zero;
    public Vec3 Scale { get; set; } = new(1, 1, 1);
    public Material? ColorOverride { get; set; }

    /// <summary>
    /// Transient editor-only material used to highlight the current selection.
    /// It is intentionally ignored by serializers and cloning so selection state
    /// never becomes authored scene data.
    /// </summary>
    public Material? PreviewColorOverride { get; set; }

    /// <summary>
    /// Semantic primitive identifier used by native scene serializers.
    /// Examples: cuboid, rectangle, sphere, cylinder, cone, torus, capsule.
    /// Empty/null means the object is stored as ordinary mesh geometry.
    /// </summary>
    public string? PrimitiveKind { get; set; }

    /// <summary>
    /// Original menu/library primitive name used to rebuild procedural objects when saving/loading.
    /// This is intentionally optional so imported meshes remain simple named mesh objects.
    /// </summary>
    public string? PrimitiveSourceName { get; set; }

    /// <summary>Authored primitive parameters. For editor-created objects this is the real model; LocalTriangles are only the render/pick shadow mesh.</summary>
    public Dictionary<string, double> PrimitiveParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Renderer geometry is always triangulated, but the editor can retain the
    // polygon topology that those triangles came from. Each entry contains the
    // local-triangle indices that together form one logical face. This metadata
    // is deliberately separate from Triangle so renderers remain triangle-only.
    private readonly List<int[]> logicalFaceTriangleGroups = new();

    /// <summary>Persistent logical polygon faces for editor topology. Empty means they should be reconstructed conservatively.</summary>
    public IReadOnlyList<int[]> LogicalFaceTriangleGroups => logicalFaceTriangleGroups;

    /// <summary>True when this node carries explicit logical-face topology for its current local triangle list.</summary>
    public bool HasLogicalFaceTopology => logicalFaceTriangleGroups.Count > 0;

    /// <summary>Replaces the logical-face topology with validated triangle-index groups.</summary>
    public void SetLogicalFaceTriangleGroups(IEnumerable<IEnumerable<int>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        logicalFaceTriangleGroups.Clear();
        foreach (IEnumerable<int> source in groups)
        {
            int[] indices = source
                .Where(index => index >= 0 && index < LocalTriangles.Count)
                .Distinct()
                .ToArray();
            if (indices.Length > 0)
                logicalFaceTriangleGroups.Add(indices);
        }
    }

    /// <summary>Clears logical-face metadata after an operation whose topology is no longer known.</summary>
    public void ClearLogicalFaceTriangleGroups() => logicalFaceTriangleGroups.Clear();

    /// <summary>True when this object can be regenerated from PrimitiveKind + PrimitiveParameters.</summary>
    public bool HasParametricPrimitive => !string.IsNullOrWhiteSpace(PrimitiveKind) && PrimitiveParameters.Count > 0;


    /// <summary>Returns editor metadata describing how gizmos should change authored parameters for this object.</summary>
    public PrimitiveGizmoEditMetadata GetGizmoEditMetadata()
    {
        return ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is ISceneObjectDefinition definition
            ? definition.GizmoMetadata
            : PrimitiveGizmoEditMetadata.MeshFallback;
    }


    /// <summary>Applies an incremental gizmo move directly to authored primitive origin parameters.</summary>
    public bool ApplyParametricMoveDelta(Vec3 delta)
    {
        return HasParametricPrimitive
            && Children.Count == 0
            && ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is ISceneObjectDefinition definition
            && definition.ApplyMoveDelta(PrimitiveParameters, delta);
    }


    /// <summary>Applies an incremental gizmo scale directly to authored primitive size parameters.</summary>
    public bool ApplyParametricScaleDelta(char axis, double factor)
    {
        factor = SanitizeScale(factor);
        return Math.Abs(factor - 1.0) > 1e-12
            && HasParametricPrimitive
            && Children.Count == 0
            && ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is ISceneObjectDefinition definition
            && definition.ApplyScaleDelta(PrimitiveParameters, axis, factor);
    }


    /// <summary>
    /// Applies pending move/scale gizmo transforms to authored primitive parameters.
    /// Returns true when the shadow mesh should be regenerated from parameters.
    /// Rotation intentionally remains as object transform metadata because most
    /// primitive definitions do not have intrinsic rotation fields.
    /// </summary>
    public bool ApplyPendingTransformToPrimitiveParameters()
    {
        if (!HasParametricPrimitive || Children.Count > 0)
            return false;

        if (ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is not ISceneObjectDefinition definition)
            return false;

        bool changed = definition.ApplyPendingTransform(PrimitiveParameters, Position, Scale);
        if (changed)
        {
            Position = Vec3.Zero;
            Scale = new Vec3(1, 1, 1);
        }

        return changed;
    }


    private bool AddParameter(string key, double delta)
    {
        if (!double.IsFinite(delta) || Math.Abs(delta) <= 1e-12)
            return false;
        PrimitiveParameters[key] = ReadParameter(key, 0.0) + delta;
        return true;
    }

    private bool MultiplyParameter(string key, double factor)
    {
        if (!PrimitiveParameters.ContainsKey(key) || !double.IsFinite(factor) || Math.Abs(factor - 1.0) <= 1e-12)
            return false;
        PrimitiveParameters[key] = Math.Max(1e-6, ReadParameter(key, 1.0) * factor);
        return true;
    }

    private double ReadParameter(string key, double fallback) =>
        PrimitiveParameters.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;

    private static double SanitizeScale(double value) => double.IsFinite(value) && value > 1e-6 ? value : 1.0;

    private static string NormalizePrimitiveKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>Controls whether this group and its descendants participate in preview, raytracing, export, and bounds.</summary>
    public bool Visible { get; set; } = true;
    public bool IsSelectable { get; }

    public bool HasChildren => Children.Count > 0;
    public bool HasLocalGeometry => LocalTriangles.Count > 0;

    public SceneObjectGroup(int id, string name, bool selectable = true)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"Object {id}" : name;
        IsSelectable = selectable;
    }

    public void AddChild(SceneObjectGroup child, bool recalculatePivot = true)
    {
        if (child == null) throw new ArgumentNullException(nameof(child));
        if (ReferenceEquals(child, this)) throw new InvalidOperationException("A group cannot be parented to itself.");
        if (child.ContainsDescendant(Id)) throw new InvalidOperationException("A group cannot be parented below one of its descendants.");

        child.Parent?.Children.Remove(child);
        child.Parent = this;
        Children.Add(child);
        if (recalculatePivot)
            RecalculatePivot();
    }

    public bool RemoveChild(SceneObjectGroup child)
    {
        if (!Children.Remove(child)) return false;
        child.Parent = null;
        RecalculatePivot();
        return true;
    }

    public IEnumerable<SceneObjectGroup> SelfAndDescendants()
    {
        yield return this;
        foreach (SceneObjectGroup child in Children)
        {
            foreach (SceneObjectGroup descendant in child.SelfAndDescendants())
                yield return descendant;
        }
    }

    public bool ContainsDescendant(int id) => SelfAndDescendants().Any(g => g.Id == id);

    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, Material material)
    {
        AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1), material);
    }

    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, Vec2 uvA, Vec2 uvB, Vec2 uvC, Material material)
    {
        logicalFaceTriangleGroups.Clear();
        LocalTriangles.Add(new Triangle(a, b, c, uvA, uvB, uvC, material, Id));
    }

    public void AddTriangle(
        Vec3 a, Vec3 b, Vec3 c,
        Vec2 uvA, Vec2 uvB, Vec2 uvC,
        Vec3 normalA, Vec3 normalB, Vec3 normalC,
        Material material)
    {
        logicalFaceTriangleGroups.Clear();
        LocalTriangles.Add(new Triangle(a, b, c, uvA, uvB, uvC, normalA, normalB, normalC, material, Id));
    }

    /// <summary>Adds a prebuilt primitive batch without per-triangle scene-stack dispatch.</summary>
    public void AddTriangles(IEnumerable<Triangle> triangles)
    {
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));
        logicalFaceTriangleGroups.Clear();
        LocalTriangles.AddRange(triangles);
    }

    /// <summary>Preallocates local triangle storage for large imported primitives.</summary>
    public void EnsureTriangleCapacity(int additionalTriangleCount)
    {
        if (additionalTriangleCount <= 0)
            return;
        LocalTriangles.EnsureCapacity(checked(LocalTriangles.Count + additionalTriangleCount));
    }

    public void RecalculatePivot()
    {
        bool hasPoint = false;
        Vec3 min = Vec3.Zero;
        Vec3 max = Vec3.Zero;

        foreach (Triangle tri in LocalTriangles)
        {
            IncludePoint(tri.A, ref hasPoint, ref min, ref max);
            IncludePoint(tri.B, ref hasPoint, ref min, ref max);
            IncludePoint(tri.C, ref hasPoint, ref min, ref max);
        }

        foreach (SceneObjectGroup child in Children)
        {
            if (!child.TryGetWorldBounds(includeHidden: false, out Aabb childBounds))
                continue;
            IncludePoint(childBounds.Min, ref hasPoint, ref min, ref max);
            IncludePoint(childBounds.Max, ref hasPoint, ref min, ref max);
        }

        Pivot = hasPoint ? (min + max) * 0.5 : Vec3.Zero;
    }

    /// <summary>Bakes this group's pending transform into all contained geometry, including descendants.</summary>
    public void BakeCurrentTransform()
    {
        foreach (SceneObjectGroup child in Children)
            child.BakeCurrentTransform();

        if (HasPendingTransform())
        {
            Vec3 fixedPivot = Pivot;
            Vec3 position = Position;
            Vec3 rotation = Rotation;
            Vec3 scale = Scale;
            ApplyBakedTransform(fixedPivot, position, rotation, scale, inverse: false);
        }

        ResetTransformState();
        foreach (SceneObjectGroup child in Children)
            child.RecalculatePivot();
        RecalculatePivot();
    }

    /// <summary>
    /// Applies one transform directly to authored triangle positions and normals.
    /// The group transform fields remain identity, so renderers consume ordinary
    /// baked geometry and pay no per-frame transform cost.
    /// </summary>
    public void BakeTransform(Vec3 position, Vec3 rotation, Vec3 scale)
    {
        foreach (SceneObjectGroup child in Children)
            child.BakeCurrentTransform();

        if (!IsIdentityTransform(position, rotation, scale))
            ApplyBakedTransform(Pivot, position, rotation, scale, inverse: false);

        ResetTransformState();
        foreach (SceneObjectGroup child in Children)
            child.RecalculatePivot();
        RecalculatePivot();
    }

    /// <summary>
    /// Applies or reverses a previously baked transform around a fixed pivot.
    /// This is used by the composer's undo/redo command without storing a second
    /// copy of potentially very large meshes.
    /// </summary>
    public void ApplyBakedTransform(Vec3 fixedPivot, Vec3 position, Vec3 rotation, Vec3 scale, bool inverse)
    {
        if (inverse)
        {
            ApplyPointTransformRecursively(
                point => TransformConverter.ApplyInverseSrt(point, fixedPivot, position, rotation, scale),
                normal => TransformConverter.ApplyInverseSrtNormal(normal, rotation, scale));
        }
        else
        {
            ApplyPointTransformRecursively(
                point => TransformConverter.ApplySrt(point, fixedPivot, position, rotation, scale),
                normal => TransformConverter.ApplySrtNormal(normal, rotation, scale));
        }

        // The transformed triangles are now the authoring source of truth. Clear
        // procedural metadata so save/load cannot regenerate the pre-transform
        // primitive and discard the baked vertex positions.
        ClearParametricMetadataRecursively();
        ResetTransformState();
        foreach (SceneObjectGroup child in Children)
            child.RecalculatePivot();
        RecalculatePivot();
    }

    public void ApplyColor(Material material)
    {
        if (material == null) throw new ArgumentNullException(nameof(material));

        ApplyMaterialRecursively(tri =>
        {
            Material updated = new(
                material.Color,
                tri.Material.Emission,
                tri.Material.LightId,
                tri.Material.Texture,
                tri.Material.EmissionColor,
                tri.Material.EmissiveTexture,
                tri.Material.Alpha,
                tri.Material.AlphaBlend,
                tri.Material.Metallic,
                tri.Material.Roughness,
                tri.Material.Transmission,
                tri.Material.MetallicRoughnessTexture,
                tri.Material.NormalTexture,
                tri.Material.OcclusionTexture,
                tri.Material.NormalScale,
                tri.Material.OcclusionStrength,
                tri.Material.AlphaMode,
                tri.Material.AlphaCutoff,
                tri.Material.DoubleSided);
            return new Triangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, tri.NormalA, tri.NormalB, tri.NormalC, updated, tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>Assigns one complete immutable material to all triangles in this subtree without changing geometry.</summary>
    public void ApplyMaterial(Material material)
    {
        if (material == null) throw new ArgumentNullException(nameof(material));
        ApplyMaterialRecursively(tri => new Triangle(
            tri.A, tri.B, tri.C,
            tri.UvA, tri.UvB, tri.UvC,
            tri.NormalA, tri.NormalB, tri.NormalC,
            material,
            tri.GroupId));
        ColorOverride = null;
    }

    /// <summary>
    /// Changes only the base color of every material in this subtree. Geometry,
    /// object transforms, procedural metadata, texture maps and PBR values remain intact.
    /// </summary>
    public void ApplyBaseColor(Vec3 color)
    {
        ApplyMaterialRecursively(tri =>
        {
            Material updated = tri.Material.WithColor(color);
            return new Triangle(
                tri.A, tri.B, tri.C,
                tri.UvA, tri.UvB, tri.UvC,
                tri.NormalA, tri.NormalB, tri.NormalC,
                updated,
                tri.GroupId);
        });
        ColorOverride = null;
    }

    /// <summary>
    /// Applies one material-library preset while preserving assigned image maps.
    /// This is a material-only edit and deliberately does not bake object transforms
    /// or convert parameterized primitives to ordinary meshes.
    /// </summary>
    public void ApplyMaterialPreset(Material preset)
    {
        if (preset == null) throw new ArgumentNullException(nameof(preset));

        ApplyMaterialRecursively(tri =>
        {
            Material updated = tri.Material.WithPreset(preset);
            return new Triangle(
                tri.A, tri.B, tri.C,
                tri.UvA, tri.UvB, tri.UvC,
                tri.NormalA, tri.NormalB, tri.NormalC,
                updated,
                tri.GroupId);
        });
        ColorOverride = null;
    }

    public void ApplyTexture(TextureMap texture)
    {
        ApplyTexture(MaterialTextureSlot.BaseColor, texture, TextureRepeatWorldUnits, forceBoxProjection: true);
    }

    /// <summary>Assigns a base-color texture, retaining the original public API.</summary>
    public void ApplyTexture(TextureMap texture, double tileWorldUnits, bool forceBoxProjection = true)
    {
        ApplyTexture(MaterialTextureSlot.BaseColor, texture, tileWorldUnits, forceBoxProjection);
    }

    /// <summary>
    /// Assigns one PBR texture slot. All slots share the triangle UV channel; when box
    /// projection is requested those UVs are regenerated once in scene-meter units.
    /// Per-texture offset/scale/rotation and addressing remain properties of TextureMap.
    /// </summary>
    public void ApplyTexture(
        MaterialTextureSlot slot,
        TextureMap texture,
        double tileWorldUnits,
        bool forceBoxProjection = false)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));

        Aabb bounds = GetWorldBounds();
        double safeTileWorldUnits = SanitizeTileWorldUnits(tileWorldUnits);
        ApplyMaterialRecursively(tri =>
        {
            Material updated = tri.Material.WithTexture(slot, texture);
            if (!forceBoxProjection)
            {
                return new Triangle(
                    tri.A, tri.B, tri.C,
                    tri.UvA, tri.UvB, tri.UvC,
                    tri.NormalA, tri.NormalB, tri.NormalC,
                    updated,
                    tri.GroupId);
            }

            return new Triangle(
                tri.A, tri.B, tri.C,
                GenerateBoxUv(tri.A, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.B, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.C, tri.Normal, bounds, safeTileWorldUnits),
                tri.NormalA, tri.NormalB, tri.NormalC,
                updated,
                tri.GroupId);
        });
        ObjectLibraryRegistry.StoreParametricTextureProjection(this, safeTileWorldUnits, forceBoxProjection);
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>
    /// Updates the transform and address mode of one texture slot without touching geometry,
    /// materials in other slots, or procedural primitive parameters.
    /// </summary>
    public void ApplyTextureMapping(
        MaterialTextureSlot slot,
        double offsetU,
        double offsetV,
        double scaleU,
        double scaleV,
        double rotationRadians,
        TextureAddressMode wrapU,
        TextureAddressMode wrapV)
    {
        ApplyMaterialRecursively(tri =>
        {
            TextureMap? texture = tri.Material.GetTexture(slot);
            if (texture == null)
                return tri;

            TextureMap mapped = texture
                .WithAddressing(wrapU, wrapV)
                .WithTextureTransform(offsetU, offsetV, scaleU, scaleV, rotationRadians);
            Material updated = tri.Material.WithTexture(slot, mapped);
            return new Triangle(
                tri.A, tri.B, tri.C,
                tri.UvA, tri.UvB, tri.UvC,
                tri.NormalA, tri.NormalB, tri.NormalC,
                updated,
                tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>Reprojects all textured triangles using a chosen scene-unit tile size.</summary>
    public void RetileTexture(double tileWorldUnits)
    {
        Aabb bounds = GetWorldBounds();
        double safeTileWorldUnits = SanitizeTileWorldUnits(tileWorldUnits);
        ApplyMaterialRecursively(tri =>
        {
            if (!tri.Material.HasAnyTexture)
                return tri;

            return new Triangle(
                tri.A, tri.B, tri.C,
                GenerateBoxUv(tri.A, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.B, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.C, tri.Normal, bounds, safeTileWorldUnits),
                tri.NormalA, tri.NormalB, tri.NormalC,
                tri.Material,
                tri.GroupId);
        });
        ObjectLibraryRegistry.StoreParametricTextureProjection(this, safeTileWorldUnits, forceBoxProjection: true);
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>
    /// Changes the shared UV projection mode. Authored/current UV mode is non-destructive:
    /// it stops future box reprojection but does not attempt to reconstruct UVs that were
    /// already overwritten on an imported mesh. Parametric primitives regain generated UVs
    /// on their next procedural regeneration.
    /// </summary>
    public void SetTextureProjectionMode(double tileWorldUnits, bool boxProjection)
    {
        double safeTileWorldUnits = SanitizeTileWorldUnits(tileWorldUnits);
        if (boxProjection)
            RetileTexture(safeTileWorldUnits);
        else
            ObjectLibraryRegistry.StoreParametricTextureProjection(this, safeTileWorldUnits, forceBoxProjection: false);
    }

    public void ClearTexture()
    {
        ClearTexture(MaterialTextureSlot.BaseColor);
    }

    /// <summary>Clears one PBR texture input while retaining the remaining texture maps.</summary>
    public void ClearTexture(MaterialTextureSlot slot)
    {
        ApplyMaterialRecursively(tri =>
        {
            Material updated = tri.Material.WithTexture(slot, null);
            return ReferenceEquals(updated, tri.Material)
                ? tri
                : new Triangle(
                    tri.A, tri.B, tri.C,
                    tri.UvA, tri.UvB, tri.UvC,
                    tri.NormalA, tri.NormalB, tri.NormalC,
                    updated,
                    tri.GroupId);
        });
        if (!SelfAndDescendants().Any(group => group.LocalTriangles.Any(tri => tri.Material.HasAnyTexture)))
            ObjectLibraryRegistry.ClearParametricTextureProjection(this);
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>Counts local mesh triangles in this group and every child group.</summary>
    public int CountLocalTrianglesRecursively()
    {
        int count = LocalTriangles.Count;
        foreach (SceneObjectGroup child in Children)
            count += child.CountLocalTrianglesRecursively();
        return count;
    }

    /// <summary>
    /// Reduces triangle count in this group and its descendants using a fast
    /// spatial decimator.  Transforms are baked first so simplification operates
    /// on the visible object, and materials/UVs remain attached to retained
    /// triangles.
    /// </summary>
    public int SimplifyGeometry(double keepFraction)
    {
        BakeCurrentTransform();
        int before = CountLocalTrianglesRecursively();
        SimplifyLocalGeometryRecursively(keepFraction);
        RecalculatePivot();
        return before - CountLocalTrianglesRecursively();
    }

    private void SimplifyLocalGeometryRecursively(double keepFraction)
    {
        if (LocalTriangles.Count > 3)
        {
            List<Triangle> simplified = MeshSimplifier.Simplify(LocalTriangles, keepFraction);
            LocalTriangles.Clear();
            LocalTriangles.AddRange(simplified);
            logicalFaceTriangleGroups.Clear();
        }

        foreach (SceneObjectGroup child in Children)
            child.SimplifyLocalGeometryRecursively(keepFraction);
    }

    /// <summary>Applies a material transformer to every local triangle in this group and its descendants.</summary>
    public void ApplyMaterialProperties(Func<Material, Material> materialTransform)
    {
        if (materialTransform == null) throw new ArgumentNullException(nameof(materialTransform));

        ApplyMaterialRecursively(tri =>
        {
            Material updated = materialTransform(tri.Material);
            return ReferenceEquals(updated, tri.Material)
                ? tri
                : new Triangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, tri.NormalA, tri.NormalB, tri.NormalC, updated, tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>Returns the first material found in this group or any descendant.</summary>
    public Material? FirstMaterialOrDefault()
    {
        foreach (SceneObjectGroup group in SelfAndDescendants())
        {
            if (group.LocalTriangles.Count > 0)
                return group.LocalTriangles[0].Material;
        }

        return null;
    }

    public Aabb GetWorldBounds(bool includeHidden = false) =>
        TryGetWorldBounds(includeHidden, out Aabb bounds)
            ? bounds
            : new Aabb(Vec3.Zero, Vec3.Zero);

    /// <summary>Computes bounds without materializing world-triangle copies when the subtree has identity transforms.</summary>
    internal bool TryGetWorldBounds(bool includeHidden, out Aabb bounds)
    {
        bool hasPoint = false;
        Vec3 min = Vec3.Zero;
        Vec3 max = Vec3.Zero;

        if (CanReferenceLocalTrianglesAsWorld(includeHidden))
        {
            AccumulateLocalBounds(includeHidden, ref hasPoint, ref min, ref max);
        }
        else
        {
            foreach (Triangle tri in BuildWorldTriangles(includeHidden))
            {
                IncludePoint(tri.A, ref hasPoint, ref min, ref max);
                IncludePoint(tri.B, ref hasPoint, ref min, ref max);
                IncludePoint(tri.C, ref hasPoint, ref min, ref max);
            }
        }

        bounds = hasPoint ? new Aabb(min, max) : new Aabb(Vec3.Zero, Vec3.Zero);
        return hasPoint;
    }

    /// <summary>Returns true when local immutable triangles can be reused directly as world geometry.</summary>
    internal bool CanReferenceLocalTrianglesAsWorld(bool includeHidden = false)
    {
        if (!includeHidden && !Visible)
            return true;
        if (HasPendingTransform() || ColorOverride != null || PreviewColorOverride != null)
            return false;
        return Children.All(child => child.CanReferenceLocalTrianglesAsWorld(includeHidden));
    }

    /// <summary>Appends local triangle references for an identity-transform subtree.</summary>
    internal void AppendLocalTrianglesAsWorld(List<Triangle> destination, bool includeHidden = false)
    {
        if (!includeHidden && !Visible)
            return;
        destination.AddRange(LocalTriangles);
        foreach (SceneObjectGroup child in Children)
            child.AppendLocalTrianglesAsWorld(destination, includeHidden);
    }

    private void AccumulateLocalBounds(bool includeHidden, ref bool hasPoint, ref Vec3 min, ref Vec3 max)
    {
        if (!includeHidden && !Visible)
            return;

        foreach (Triangle tri in LocalTriangles)
        {
            IncludePoint(tri.A, ref hasPoint, ref min, ref max);
            IncludePoint(tri.B, ref hasPoint, ref min, ref max);
            IncludePoint(tri.C, ref hasPoint, ref min, ref max);
        }

        foreach (SceneObjectGroup child in Children)
            child.AccumulateLocalBounds(includeHidden, ref hasPoint, ref min, ref max);
    }

    /// <summary>Builds visible world triangles while preserving each leaf group id for picking.</summary>
    public IEnumerable<Triangle> BuildWorldTriangles() => BuildWorldTriangles(includeHidden: false);

    /// <summary>Builds world triangles, optionally including hidden groups for inspector/detail calculations.</summary>
    public IEnumerable<Triangle> BuildWorldTriangles(bool includeHidden)
    {
        if (!includeHidden && !Visible)
            yield break;

        foreach (Triangle tri in LocalTriangles)
        {
            Material material = PreviewColorOverride ?? ColorOverride ?? tri.Material;
            yield return new Triangle(
                TransformPoint(tri.A), TransformPoint(tri.B), TransformPoint(tri.C),
                tri.UvA, tri.UvB, tri.UvC,
                TransformNormal(tri.NormalA), TransformNormal(tri.NormalB), TransformNormal(tri.NormalC),
                material, Id);
        }

        foreach (SceneObjectGroup child in Children)
        {
            foreach (Triangle childTri in child.BuildWorldTriangles(includeHidden))
            {
                Material material = PreviewColorOverride ?? ColorOverride ?? childTri.Material;
                yield return new Triangle(
                    TransformPoint(childTri.A),
                    TransformPoint(childTri.B),
                    TransformPoint(childTri.C),
                    childTri.UvA,
                    childTri.UvB,
                    childTri.UvC,
                    TransformNormal(childTri.NormalA),
                    TransformNormal(childTri.NormalB),
                    TransformNormal(childTri.NormalC),
                    material,
                    childTri.GroupId >= 0 ? childTri.GroupId : Id);
            }
        }
    }

    public Vec3 TransformPoint(Vec3 p)
    {
        return TransformConverter.ApplySrt(p, Pivot, Position, Rotation, Scale);
    }

    public Vec3 TransformNormal(Vec3 normal)
    {
        return TransformConverter.ApplySrtNormal(normal, Rotation, Scale);
    }

    private bool HasPendingTransform() =>
        Position.Length() > 1e-12 || Rotation.Length() > 1e-12 ||
        Math.Abs(Scale.X - 1.0) > 1e-12 || Math.Abs(Scale.Y - 1.0) > 1e-12 || Math.Abs(Scale.Z - 1.0) > 1e-12;

    private void ApplyPointTransformRecursively(Func<Vec3, Vec3> pointTransform, Func<Vec3, Vec3> normalTransform)
    {
        for (int i = 0; i < LocalTriangles.Count; i++)
        {
            Triangle tri = LocalTriangles[i];
            LocalTriangles[i] = new Triangle(
                pointTransform(tri.A), pointTransform(tri.B), pointTransform(tri.C),
                tri.UvA, tri.UvB, tri.UvC,
                normalTransform(tri.NormalA), normalTransform(tri.NormalB), normalTransform(tri.NormalC),
                tri.Material, tri.GroupId);
        }

        foreach (SceneObjectGroup child in Children)
            child.ApplyPointTransformRecursively(pointTransform, normalTransform);
    }

    private void ClearParametricMetadataRecursively()
    {
        PrimitiveKind = null;
        PrimitiveSourceName = null;
        PrimitiveParameters.Clear();
        foreach (SceneObjectGroup child in Children)
            child.ClearParametricMetadataRecursively();
    }

    private void ApplyMaterialRecursively(Func<Triangle, Triangle> transform)
    {
        for (int i = 0; i < LocalTriangles.Count; i++)
            LocalTriangles[i] = transform(LocalTriangles[i]);

        foreach (SceneObjectGroup child in Children)
            child.ApplyMaterialRecursively(transform);
    }


    private const double TextureRepeatWorldUnits = 0.25;

    private static bool HasDefaultUnitUvs(Triangle tri) =>
        IsUnitUv(tri.UvA) && IsUnitUv(tri.UvB) && IsUnitUv(tri.UvC);

    private static bool IsUnitUv(Vec2 value) =>
        IsZeroOrOne(value.U) && IsZeroOrOne(value.V);

    private static bool IsZeroOrOne(double value) =>
        Math.Abs(value) < 1e-9 || Math.Abs(value - 1.0) < 1e-9;

    private static Vec2 GenerateBoxUv(Vec3 point, Vec3 normal, Aabb bounds, double tileWorldUnits)
    {
        double nx = Math.Abs(normal.X), ny = Math.Abs(normal.Y), nz = Math.Abs(normal.Z);

        // Project onto the dominant plane for the face normal.  Coordinates are
        // converted to scene-space tile units rather than normalized to 0..1, so
        // large faces show repeated texture copies instead of one stretched copy.
        if (ny >= nx && ny >= nz)
            return new Vec2(ToTileCoordinate(point.X, bounds.Min.X, tileWorldUnits), ToTileCoordinate(point.Z, bounds.Min.Z, tileWorldUnits));
        if (nx >= ny && nx >= nz)
            return new Vec2(ToTileCoordinate(point.Z, bounds.Min.Z, tileWorldUnits), ToTileCoordinate(point.Y, bounds.Min.Y, tileWorldUnits));
        return new Vec2(ToTileCoordinate(point.X, bounds.Min.X, tileWorldUnits), ToTileCoordinate(point.Y, bounds.Min.Y, tileWorldUnits));
    }

    private static double ToTileCoordinate(double value, double origin, double tileWorldUnits) =>
        (value - origin) / tileWorldUnits;

    private static double SanitizeTileWorldUnits(double tileWorldUnits) =>
        double.IsFinite(tileWorldUnits) && tileWorldUnits > 1e-6 ? tileWorldUnits : TextureRepeatWorldUnits;

    private void ResetTransformState()
    {
        Position = Vec3.Zero;
        Rotation = Vec3.Zero;
        Scale = new Vec3(1, 1, 1);
    }

    private static bool IsIdentityTransform(Vec3 position, Vec3 rotation, Vec3 scale) =>
        position.Length() <= 1e-12 &&
        rotation.Length() <= 1e-12 &&
        Math.Abs(scale.X - 1.0) <= 1e-12 &&
        Math.Abs(scale.Y - 1.0) <= 1e-12 &&
        Math.Abs(scale.Z - 1.0) <= 1e-12;

    private static Vec3 Rotate(Vec3 p, Vec3 r)
    {
        double cx = Math.Cos(r.X), sx = Math.Sin(r.X);
        double cy = Math.Cos(r.Y), sy = Math.Sin(r.Y);
        double cz = Math.Cos(r.Z), sz = Math.Sin(r.Z);

        Vec3 x = new(p.X, p.Y * cx - p.Z * sx, p.Y * sx + p.Z * cx);
        Vec3 y = new(x.X * cy + x.Z * sy, x.Y, -x.X * sy + x.Z * cy);
        return new Vec3(y.X * cz - y.Y * sz, y.X * sz + y.Y * cz, y.Z);
    }

    private static void AddPoint(Vec3 p, ref Vec3 min, ref Vec3 max)
    {
        min = Min(min, p);
        max = Max(max, p);
    }

    private static void IncludePoint(Vec3 point, ref bool hasPoint, ref Vec3 min, ref Vec3 max)
    {
        if (!hasPoint)
        {
            min = point;
            max = point;
            hasPoint = true;
            return;
        }
        min = Min(min, point);
        max = Max(max, point);
    }

    private static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
}
