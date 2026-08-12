/*
 * This is an extensibility seam. Callers discover capabilities through a registry/interface instead of referencing
 * every concrete format or object-library assembly, allowing plugins to be added while the core scene/editor code
 * remains unchanged.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

// ObjectLibraryRegistry is a discovery table that maps stable names/capabilities to registered implementations,
// removing the need for central switch statements that know every plugin or primitive at compile time.
public static class ObjectLibraryRegistry
{
    private const string TextureProjectionModeKey = "__composerTextureBoxProjection";
    private const string TextureTileMetersKey = "__composerTextureTileMeters";

    public static string[] Names => ScenePrimitiveRegistry.DisplayNames;

    public static void EnsureInitialized() => ScenePrimitiveRegistry.EnsureInitialized();

    public static bool Contains(string objectName) => ScenePrimitiveRegistry.Contains(objectName);

    public static SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (materials == null) throw new ArgumentNullException(nameof(materials));

        EnsureInitialized();
        string name = string.IsNullOrWhiteSpace(objectName) ? Names.FirstOrDefault() ?? "Object" : objectName.Trim();
        ISceneObjectDefinition definition = ScenePrimitiveRegistry.Find(name)
            ?? ScenePrimitiveRegistry.Primitives.FirstOrDefault()
            ?? throw new InvalidOperationException("No object definitions are available. Build and deploy a LightingShowcase.ObjectLibrary.*.dll next to the application. Add a public class implementing ISceneObjectDefinition to add a new object.");

        scene.BeginGroup(definition.DisplayName);
        Dictionary<string, double> parameters = definition.CreateDefaultParameters();
        definition.Build(materials, parameters, materials.Cushion, (a, b, c, uvA, uvB, uvC, material) => scene.AddTriangle(a, b, c, uvA, uvB, uvC, material));
        SceneObjectGroup group = scene.EndGroup();
        group.PrimitiveKind = definition.Kind;
        group.PrimitiveSourceName = definition.DisplayName;
        group.PrimitiveParameters.Clear();
        foreach (KeyValuePair<string, double> parameter in parameters)
            group.PrimitiveParameters[parameter.Key] = parameter.Value;
        return group;
    }

    // ReadyMadeNameForPrimitiveKind reads y made name for primitive kind from the external stream/document,
    // advancing through the format in the order required to resolve references and produce valid internal data.
    // Primitive definitions are resolved through the registry, allowing plugin-provided primitives to follow the
    // same path as built-ins.
    public static string ReadyMadeNameForPrimitiveKind(string? primitiveKind, string? sourceName)
    {
        EnsureInitialized();
        if (ScenePrimitiveRegistry.Find(sourceName) is ISceneObjectDefinition fromSource)
            return fromSource.DisplayName;
        if (ScenePrimitiveRegistry.Find(primitiveKind) is ISceneObjectDefinition fromKind)
            return fromKind.DisplayName;
        return !string.IsNullOrWhiteSpace(sourceName) ? sourceName!.Trim() : primitiveKind?.Trim() ?? Names.FirstOrDefault() ?? "Object";
    }

    public static string PrimitiveKindForReadyMade(string? readyMadeName)
    {
        EnsureInitialized();
        if (ScenePrimitiveRegistry.Find(readyMadeName) is ISceneObjectDefinition definition)
            return definition.Kind;
        return string.IsNullOrWhiteSpace(readyMadeName)
            ? "object"
            : readyMadeName.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    public static void StoreDefaultPrimitiveParametersFromShadow(SceneObjectGroup group)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        group.PrimitiveParameters.Clear();

        if (ScenePrimitiveRegistry.Find(group.PrimitiveKind ?? group.PrimitiveSourceName) is not ISceneObjectDefinition definition)
            return;

        Dictionary<string, double> parameters = definition.CreateParametersFromBounds(group.GetWorldBounds(includeHidden: true));
        foreach (KeyValuePair<string, double> parameter in parameters)
            group.PrimitiveParameters[parameter.Key] = parameter.Value;
    }

    // Parameterized objects are rebuilt through the registered primitive definition rather than hard-coded shape
    // logic. That lets built-ins and plugins share the same edit path while preserving the group’s procedural
    // identity and authored transform parameters.
    public static bool RebuildPrimitiveShadowGeometry(SceneObjectGroup group, SceneMaterials materials)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (materials == null) throw new ArgumentNullException(nameof(materials));
        if (string.IsNullOrWhiteSpace(group.PrimitiveKind) || group.PrimitiveParameters.Count == 0 || group.Children.Count > 0)
            return false;

        if (ScenePrimitiveRegistry.Find(group.PrimitiveKind) is not ISceneObjectDefinition definition)
            return false;

        Material material = group.FirstMaterialOrDefault() ?? materials.WhiteWall;
        group.LocalTriangles.Clear();
        AddTriangleCallback addTriangle = ApplyAuthoredTransform(
            group.PrimitiveParameters,
            (a, b, c, uvA, uvB, uvC, mat) => group.AddTriangle(a, b, c, uvA, uvB, uvC, mat));
        definition.Build(materials, group.PrimitiveParameters, material, addTriangle);

        // Keep an explicitly assigned meter-based texture projection stable when
        // shape parameters regenerate the procedural shadow mesh. The material
        // itself survives because it was captured before LocalTriangles.Clear().
        if (material.HasAnyTexture &&
            group.PrimitiveParameters.TryGetValue(TextureProjectionModeKey, out double boxProjection) &&
            boxProjection >= 0.5)
        {
            double tileMeters = group.PrimitiveParameters.TryGetValue(TextureTileMetersKey, out double storedTile) &&
                                double.IsFinite(storedTile) && storedTile > 1e-6
                ? storedTile
                : 0.25;
            group.RetileTexture(tileMeters);
        }

        group.RecalculatePivot();
        return true;
    }

    /// <summary>
    /// Stores editor-only texture projection metadata alongside procedural shape
    /// parameters. Hidden keys are ignored by the parameter dialog but survive
    /// parameter edits, transforms, undo/redo, and native scene serialization.
    /// </summary>
    public static void StoreParametricTextureProjection(SceneObjectGroup group, double tileWorldUnits, bool forceBoxProjection)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));

        // Texture mapping metadata is useful for imported meshes as well as procedural
        // primitives. Imported objects have no PrimitiveKind, so these hidden keys do
        // not make them parametric and are safely ignored by the parameter editor.
        group.PrimitiveParameters[TextureProjectionModeKey] = forceBoxProjection ? 1.0 : 0.0;
        if (forceBoxProjection)
            group.PrimitiveParameters[TextureTileMetersKey] = double.IsFinite(tileWorldUnits) && tileWorldUnits > 1e-6
                ? tileWorldUnits
                : 0.25;
        else
            group.PrimitiveParameters.Remove(TextureTileMetersKey);
    }

    // ClearParametricTextureProjection removes/resets parametric texture projection to its empty/default state.
    // This is an explicit state transition rather than leaving old values around for later code to accidentally
    // reuse.
    public static void ClearParametricTextureProjection(SceneObjectGroup group)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        group.PrimitiveParameters.Remove(TextureProjectionModeKey);
        group.PrimitiveParameters.Remove(TextureTileMetersKey);
    }

    public static bool TryGetParametricTextureProjection(SceneObjectGroup group, out bool boxProjection, out double tileWorldUnits)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        boxProjection = false;
        tileWorldUnits = 0.25;
        if (!group.PrimitiveParameters.TryGetValue(TextureProjectionModeKey, out double mode))
            return false;

        boxProjection = mode >= 0.5;
        if (group.PrimitiveParameters.TryGetValue(TextureTileMetersKey, out double storedTile) &&
            double.IsFinite(storedTile) && storedTile > 1e-6)
        {
            tileWorldUnits = storedTile;
        }
        return true;
    }


    /// <summary>
    /// Accumulates an object-space gizmo/inspector transform into hidden procedural
    /// metadata. The generated shadow mesh is rebuilt from the original shape
    /// parameters and this affine layer, so Move/Rotate/Scale never destroy the
    /// primitive definition. Topology edits still explicitly convert to mesh.
    /// </summary>
    public static bool AccumulateParametricTransform(
        SceneObjectGroup group,
        Vec3 fixedPivot,
        Vec3 position,
        Vec3 rotation,
        Vec3 scale)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (!group.HasParametricPrimitive || group.Children.Count > 0)
            return false;

        if (IsIdentitySrt(position, rotation, scale))
            return true;

        AuthoredAffine existing = ReadAuthoredAffine(group.PrimitiveParameters);
        AuthoredAffine delta = AuthoredAffine.FromSrt(fixedPivot, position, rotation, scale);
        WriteAuthoredAffine(group.PrimitiveParameters, AuthoredAffine.Compose(delta, existing));
        group.Position = Vec3.Zero;
        group.Rotation = Vec3.Zero;
        group.Scale = new Vec3(1, 1, 1);
        return true;
    }

    private const string AffinePrefix = "__composerAffine";

    // IsIdentitySrt tests whether identity srt is true for the supplied/current value. Keeping the predicate here
    // ensures every caller uses the same definition instead of duplicating a slightly different condition.
    private static bool IsIdentitySrt(Vec3 position, Vec3 rotation, Vec3 scale) =>
        position.Length() <= 1e-12 && rotation.Length() <= 1e-12 &&
        Math.Abs(scale.X - 1.0) <= 1e-12 && Math.Abs(scale.Y - 1.0) <= 1e-12 && Math.Abs(scale.Z - 1.0) <= 1e-12;

    private static AddTriangleCallback ApplyAuthoredTransform(
        IReadOnlyDictionary<string, double> parameters,
        AddTriangleCallback destination)
    {
        AuthoredAffine affine = ReadAuthoredAffine(parameters);
        if (affine.IsIdentity)
            return destination;

        return (a, b, c, uvA, uvB, uvC, material) =>
            destination(
                affine.TransformPoint(a),
                affine.TransformPoint(b),
                affine.TransformPoint(c),
                uvA, uvB, uvC, material);
    }

    // ReadAuthoredAffine reads authored affine from the external stream/document, advancing through the format in
    // the order required to resolve references and produce valid internal data.
    private static AuthoredAffine ReadAuthoredAffine(IReadOnlyDictionary<string, double> parameters)
    {
        double Read(string suffix, double fallback) =>
            parameters.TryGetValue(AffinePrefix + suffix, out double value) && double.IsFinite(value)
                ? value
                : fallback;

        return new AuthoredAffine(
            Read("M11", 1.0), Read("M12", 0.0), Read("M13", 0.0), Read("TX", 0.0),
            Read("M21", 0.0), Read("M22", 1.0), Read("M23", 0.0), Read("TY", 0.0),
            Read("M31", 0.0), Read("M32", 0.0), Read("M33", 1.0), Read("TZ", 0.0));
    }

    // WriteAuthoredAffine writes authored affine to the external stream/document in the format’s required order,
    // using stable indices/references so another reader can reconstruct the same relationships.
    private static void WriteAuthoredAffine(IDictionary<string, double> parameters, AuthoredAffine value)
    {
        parameters[AffinePrefix + "M11"] = value.M11;
        parameters[AffinePrefix + "M12"] = value.M12;
        parameters[AffinePrefix + "M13"] = value.M13;
        parameters[AffinePrefix + "TX"] = value.TX;
        parameters[AffinePrefix + "M21"] = value.M21;
        parameters[AffinePrefix + "M22"] = value.M22;
        parameters[AffinePrefix + "M23"] = value.M23;
        parameters[AffinePrefix + "TY"] = value.TY;
        parameters[AffinePrefix + "M31"] = value.M31;
        parameters[AffinePrefix + "M32"] = value.M32;
        parameters[AffinePrefix + "M33"] = value.M33;
        parameters[AffinePrefix + "TZ"] = value.TZ;
    }

    private readonly record struct AuthoredAffine(
        double M11, double M12, double M13, double TX,
        double M21, double M22, double M23, double TY,
        double M31, double M32, double M33, double TZ)
    {
        // IsIdentity is a read-only predicate over the object’s existing state; it exists so callers share one
        // exact condition when enabling commands or deciding whether an operation is applicable.
        public bool IsIdentity =>
            Math.Abs(M11 - 1.0) <= 1e-12 && Math.Abs(M22 - 1.0) <= 1e-12 && Math.Abs(M33 - 1.0) <= 1e-12 &&
            Math.Abs(M12) <= 1e-12 && Math.Abs(M13) <= 1e-12 &&
            Math.Abs(M21) <= 1e-12 && Math.Abs(M23) <= 1e-12 &&
            Math.Abs(M31) <= 1e-12 && Math.Abs(M32) <= 1e-12 &&
            Math.Abs(TX) <= 1e-12 && Math.Abs(TY) <= 1e-12 && Math.Abs(TZ) <= 1e-12;

        // TransformPoint applies the relevant coordinate transform to point, making explicit whether data is being
        // moved between local, world, view, or preview space.
        public Vec3 TransformPoint(Vec3 point) => new(
            M11 * point.X + M12 * point.Y + M13 * point.Z + TX,
            M21 * point.X + M22 * point.Y + M23 * point.Z + TY,
            M31 * point.X + M32 * point.Y + M33 * point.Z + TZ);

        public static AuthoredAffine FromSrt(Vec3 pivot, Vec3 position, Vec3 rotation, Vec3 scale)
        {
            Vec3 safeScale = TransformConverter.SanitizeScale(scale);
            Vec3 x = TransformConverter.RotateEuler(new Vec3(safeScale.X, 0.0, 0.0), rotation);
            Vec3 y = TransformConverter.RotateEuler(new Vec3(0.0, safeScale.Y, 0.0), rotation);
            Vec3 z = TransformConverter.RotateEuler(new Vec3(0.0, 0.0, safeScale.Z), rotation);

            double m11 = x.X, m12 = y.X, m13 = z.X;
            double m21 = x.Y, m22 = y.Y, m23 = z.Y;
            double m31 = x.Z, m32 = y.Z, m33 = z.Z;
            double tx = pivot.X + position.X - (m11 * pivot.X + m12 * pivot.Y + m13 * pivot.Z);
            double ty = pivot.Y + position.Y - (m21 * pivot.X + m22 * pivot.Y + m23 * pivot.Z);
            double tz = pivot.Z + position.Z - (m31 * pivot.X + m32 * pivot.Y + m33 * pivot.Z);
            return new AuthoredAffine(m11, m12, m13, tx, m21, m22, m23, ty, m31, m32, m33, tz);
        }

        /// <summary>Returns outer(inner(point)).</summary>
        public static AuthoredAffine Compose(AuthoredAffine outer, AuthoredAffine inner)
        {
            return new AuthoredAffine(
                outer.M11 * inner.M11 + outer.M12 * inner.M21 + outer.M13 * inner.M31,
                outer.M11 * inner.M12 + outer.M12 * inner.M22 + outer.M13 * inner.M32,
                outer.M11 * inner.M13 + outer.M12 * inner.M23 + outer.M13 * inner.M33,
                outer.M11 * inner.TX + outer.M12 * inner.TY + outer.M13 * inner.TZ + outer.TX,

                outer.M21 * inner.M11 + outer.M22 * inner.M21 + outer.M23 * inner.M31,
                outer.M21 * inner.M12 + outer.M22 * inner.M22 + outer.M23 * inner.M32,
                outer.M21 * inner.M13 + outer.M22 * inner.M23 + outer.M23 * inner.M33,
                outer.M21 * inner.TX + outer.M22 * inner.TY + outer.M23 * inner.TZ + outer.TY,

                outer.M31 * inner.M11 + outer.M32 * inner.M21 + outer.M33 * inner.M31,
                outer.M31 * inner.M12 + outer.M32 * inner.M22 + outer.M33 * inner.M32,
                outer.M31 * inner.M13 + outer.M32 * inner.M23 + outer.M33 * inner.M33,
                outer.M31 * inner.TX + outer.M32 * inner.TY + outer.M33 * inner.TZ + outer.TZ);
        }
    }

}
