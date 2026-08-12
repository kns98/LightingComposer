/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>
/// Callback supplied by the core scene layer to external object DLLs. Object
/// definitions emit triangle geometry through this callback instead of receiving
/// Scene or SceneObjectGroup references.
/// </summary>
public delegate void AddTriangleCallback(Vec3 a, Vec3 b, Vec3 c, Vec2 uvA, Vec2 uvB, Vec2 uvC, Material material);


// PrimitiveParameterKind makes a closed set of choices compiler-visible instead of passing loosely related integers
// or strings. Code that switches over Length, Integer, Number, Toggle, Choice is where the behavioral meaning of
// each choice is implemented.
/// <summary>Editor control type for one authored primitive parameter.</summary>
public enum PrimitiveParameterKind
{
    Length,
    Integer,
    Number,
    Toggle,
    Choice
}

/// <summary>
/// Describes one editable procedural parameter. Length values are always stored
/// in scene metres; the editor may display the UnitLabel but never rescales the
/// authored value behind the user's back.
/// </summary>
public sealed record PrimitiveParameterDescriptor(
    string Key,
    string Label,
    PrimitiveParameterKind Kind,
    double Minimum,
    double Maximum,
    double Step,
    string UnitLabel = "",
    IReadOnlyList<string>? Choices = null)
{
    // Normalize returns a unit-length direction while guarding the degenerate near-zero case so division does not
    // create infinities or unstable directions.
    public double Normalize(double value)
    {
        if (!double.IsFinite(value))
            value = Minimum;
        value = Math.Clamp(value, Minimum, Maximum);
        return Kind is PrimitiveParameterKind.Integer or PrimitiveParameterKind.Choice or PrimitiveParameterKind.Toggle
            ? Math.Round(value)
            : value;
    }
}

// IEditablePrimitiveDefinition defines a capability boundary: callers depend on the contract rather than the
// concrete plugin/backend implementing it. New implementations can therefore participate without changing the core
// caller.
/// <summary>Optional editor metadata for procedural definitions with user-editable parameters.</summary>
public interface IEditablePrimitiveDefinition
{
    IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; }
}

// ISceneObjectDefinition defines a capability boundary: callers depend on the contract rather than the concrete
// plugin/backend implementing it. New implementations can therefore participate without changing the core caller.
/// <summary>
/// Contract for insertable objects that can emit their own triangle shadow mesh and own
/// the gizmo-to-parameter rules used by the editor. Implement this in an external
/// LightingShowcase.ObjectLibrary.* DLL; the registry discovers it automatically.
/// </summary>
public interface ISceneObjectDefinition
{
    /// <summary>Stable serializer/editor kind, for example "sphere" or "diningTable".</summary>
    string Kind { get; }

    /// <summary>User-facing insert menu name, for example "Sphere".</summary>
    string DisplayName { get; }

    /// <summary>Metadata shown by the editor and used to describe gizmo behavior.</summary>
    PrimitiveGizmoEditMetadata GizmoMetadata { get; }

    /// <summary>Creates the default authored parameters for a newly inserted object.</summary>
    Dictionary<string, double> CreateDefaultParameters();

    /// <summary>Creates authored parameters that fit this object definition into an existing mesh/shadow bounding box.</summary>
    Dictionary<string, double> CreateParametersFromBounds(Aabb bounds);

    /// <summary>Emits the render/pick shadow mesh from authored parameters through the supplied triangle callback.</summary>
    void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> parameters, Material material, AddTriangleCallback addTriangle);

    /// <summary>Applies a live gizmo move to authored parameters.</summary>
    bool ApplyMoveDelta(IDictionary<string, double> parameters, Vec3 delta);

    /// <summary>Applies a live gizmo scale to authored parameters.</summary>
    bool ApplyScaleDelta(IDictionary<string, double> parameters, char axis, double factor);

    /// <summary>Commits accumulated object transform preview values back into authored parameters.</summary>
    bool ApplyPendingTransform(IDictionary<string, double> parameters, Vec3 position, Vec3 scale);
}


// IScenePrimitive defines a capability boundary: callers depend on the contract rather than the concrete
// plugin/backend implementing it. New implementations can therefore participate without changing the core caller.
/// <summary>Backward-compatible alias for older primitive plugin classes.</summary>
public interface IScenePrimitive : ISceneObjectDefinition
{
}
