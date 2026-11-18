// -----------------------------------------------------------------------------
// File: LightingShowcase.ObjectLibrary.BuiltIns/BasicPrimitives.cs
// Purpose: Basic procedural primitives.
//
// Plane, cube, circle, sphere, icosphere, cylinder, cone, grid, and hemisphere primitives with editable parameters.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

public sealed class PlanePrimitive : PrimitiveBase
{
    public override string Kind => "plane";
    public override string DisplayName => "Plane";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Plane", true, "X/Z scale updates width/depth", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        LengthParameter("width", "Width"),
        LengthParameter("depth", "Depth")
    ];
    /// <summary>Creates default origin, width, and depth parameters for a plane.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.90), ("originZ", 3.60), ("width", 2.0), ("depth", 2.0));
    /// <summary>Creates plane parameters centered on the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("width", Math.Max(1e-6, size.X)), ("depth", Math.Max(1e-6, size.Z)));
    }
    /// <summary>Builds a plane mesh from the width and depth parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddGrid(addTriangle, Origin(p, 0, -0.90, 3.60), Size(p, "width", 2.0), Size(p, "depth", 2.0), 1, 1, material);
    /// <summary>Applies an X or Z gizmo scale delta to the plane width or depth.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch
    {
        'X' => Multiply(p, "width", factor),
        'Z' => Multiply(p, "depth", factor),
        _ => false
    };
}

public sealed class CubePrimitive : PrimitiveBase
{
    public override string Kind => "cube";
    public override string DisplayName => "Cube";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Cuboid", true, "X/Y/Z scale updates width/height/depth", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        LengthParameter("width", "Width"),
        LengthParameter("height", "Height"),
        LengthParameter("depth", "Depth")
    ];
    /// <summary>Creates default origin and size parameters for a cube.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.50), ("originZ", 3.50), ("width", 1.0), ("height", 1.0), ("depth", 1.0));
    /// <summary>Creates cube parameters sized to the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return BoxParameters(bounds, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }
    /// <summary>Builds a cuboid mesh from the width, height, and depth parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        Box(addTriangle, Origin(p, 0, -0.50, 3.50), Size(p, "width", 1.0), Size(p, "height", 1.0), Size(p, "depth", 1.0), material);
    /// <summary>Applies an X, Y, or Z gizmo scale delta to the matching cube dimension.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch
    {
        'X' => Multiply(p, "width", factor),
        'Y' => Multiply(p, "height", factor),
        _ => Multiply(p, "depth", factor)
    };
}

public sealed class CirclePrimitive : PrimitiveBase
{
    public override string Kind => "circle";
    public override string DisplayName => "Circle";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Circle", true, "uniform scale updates radius", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        IntegerParameter("vertices", "Vertices", 3, 512),
        LengthParameter("radius", "Radius"),
        ChoiceParameter("fillType", "Fill Type", "Nothing", "N-gon", "Triangle Fan")
    ];
    /// <summary>Creates default origin, vertex count, radius, and fill parameters for a circle.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.90), ("originZ", 3.60), ("vertices", 32), ("radius", 1.0), ("fillType", 1));
    /// <summary>Creates circle parameters centered on the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("vertices", 32), ("radius", Math.Max(size.X, size.Z) * 0.5), ("fillType", 1));
    }
    /// <summary>Builds a circle mesh from the radius, vertex, and fill parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddCircle(addTriangle, Origin(p, 0, -0.90, 3.60), Size(p, "radius", 1.0), ReadInt(p, "vertices", 32, 3, 512), ReadInt(p, "fillType", 1, 0, 2), material);
    /// <summary>Applies any gizmo scale delta uniformly to the circle radius.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    /// <summary>Applies a pending move plus uniform X/Z scale to the circle parameters.</summary>
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

public sealed class SpherePrimitive : PrimitiveBase
{
    // Keep the historical kind "sphere" so older .lscene files remain editable.
    public override string Kind => "sphere";
    public override string DisplayName => "UV Sphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("UV Sphere", true, "uniform gizmo scale updates radius", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        IntegerParameter("longitudeSegments", "Segments", 3, 512),
        IntegerParameter("latitudeSegments", "Rings", 2, 256),
        LengthParameter("radius", "Radius")
    ];
    /// <summary>Creates default origin, radius, and segment parameters for a UV sphere.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("radius", 1.0), ("longitudeSegments", 32), ("latitudeSegments", 16));
    /// <summary>Creates sphere parameters centered on the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5), ("longitudeSegments", 32), ("latitudeSegments", 16));
    }
    /// <summary>Builds a UV sphere mesh from the radius and segment parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddSphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 1.0), ReadInt(p, "longitudeSegments", 32, 3, 512), ReadInt(p, "latitudeSegments", 16, 2, 256), material);
    /// <summary>Applies any gizmo scale delta uniformly to the sphere radius.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    /// <summary>Applies a pending move plus uniform scale to the sphere parameters.</summary>
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Y)), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

public sealed class IcospherePrimitive : PrimitiveBase
{
    public override string Kind => "icosphere";
    public override string DisplayName => "Icosphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Icosphere", true, "uniform gizmo scale updates radius", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        IntegerParameter("subdivisions", "Subdivisions", 1, 7),
        LengthParameter("radius", "Radius")
    ];
    /// <summary>Creates default origin, subdivision, and radius parameters for an icosphere.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("subdivisions", 2), ("radius", 1.0));
    /// <summary>Creates icosphere parameters centered on the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("subdivisions", 2), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5));
    }
    /// <summary>Builds an icosphere mesh from the radius and subdivision parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddIcosphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 1.0), ReadInt(p, "subdivisions", 2, 1, 7), material);
    /// <summary>Applies any gizmo scale delta uniformly to the icosphere radius.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    /// <summary>Applies a pending move plus uniform scale to the icosphere parameters.</summary>
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Y)), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

public sealed class CylinderPrimitive : PrimitiveBase
{
    public override string Kind => "cylinder";
    public override string DisplayName => "Cylinder";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Cylinder", true, "X/Z scale updates radius; Y scale updates depth", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        IntegerParameter("sides", "Vertices", 3, 512),
        LengthParameter("radius", "Radius"),
        LengthParameter("height", "Depth"),
        ToggleParameter("capFill", "Fill Caps")
    ];
    /// <summary>Creates default origin, radius, height, side, and cap parameters for a cylinder.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.575), ("originZ", 3.55), ("radius", 0.5), ("height", 2.0), ("sides", 32), ("capFill", 1));
    /// <summary>Creates cylinder parameters sized to the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds);
        p["sides"] = 32;
        p["capFill"] = 1;
        return p;
    }
    /// <summary>Builds a cylinder mesh from the radius, height, side, and cap parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddFrustum(addTriangle, Origin(p, 0, -0.575, 3.55), Size(p, "radius", 0.5), Size(p, "radius", 0.5), Size(p, "height", 2.0), ReadInt(p, "sides", 32, 3, 512), ReadInt(p, "capFill", 1, 0, 1) != 0, material);
    /// <summary>Applies a Y gizmo scale delta to height and other axes to radius.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : Multiply(p, "radius", factor);
}

public sealed class ConePrimitive : PrimitiveBase
{
    public override string Kind => "cone";
    public override string DisplayName => "Cone";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Cone", true, "X/Z scale updates both radii; Y scale updates depth", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        IntegerParameter("sides", "Vertices", 3, 512),
        LengthParameter("radius1", "Radius 1", 0.0),
        LengthParameter("radius2", "Radius 2", 0.0),
        LengthParameter("height", "Depth"),
        ToggleParameter("capFill", "Fill Caps")
    ];
    /// <summary>Creates default origin, radii, height, side, and cap parameters for a cone.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.525), ("originZ", 3.55), ("radius1", 0.5), ("radius2", 0.0), ("height", 2.0), ("sides", 32), ("capFill", 1));
    /// <summary>Creates cone parameters sized to the given bounds with a pointed top.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds, "radius1", "height");
        p["radius2"] = 0.0;
        p["sides"] = 32;
        p["capFill"] = 1;
        return p;
    }
    /// <summary>Builds a cone or frustum mesh from the radii, height, side, and cap parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle)
    {
        // Backward compatibility: old scenes used "radius" for the cone base.
        double r1 = p.ContainsKey("radius1") ? Math.Max(0.0, Read(p, "radius1", 0.5)) : Size(p, "radius", 0.5);
        double r2 = Math.Max(0.0, Read(p, "radius2", 0.0));
        AddFrustum(addTriangle, Origin(p, 0, -0.525, 3.55), r1, r2, Size(p, "height", 2.0), ReadInt(p, "sides", 32, 3, 512), ReadInt(p, "capFill", 1, 0, 1) != 0, material);
    }
    /// <summary>Applies a Y gizmo scale delta to height and other axes to the cone radii.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y'
        ? Multiply(p, "height", factor)
        : MultiplyAny(p, factor, p.ContainsKey("radius1") ? "radius1" : "radius", "radius2");
}

public sealed class GridPrimitive : PrimitiveBase
{
    public override string Kind => "grid";
    public override string DisplayName => "Grid";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Grid", true, "X/Z scale updates width/depth", "stored as object rotation");
    public override IReadOnlyList<PrimitiveParameterDescriptor> EditableParameters { get; } =
    [
        IntegerParameter("xSubdivisions", "X Subdivisions", 1, 512),
        IntegerParameter("ySubdivisions", "Y Subdivisions", 1, 512),
        LengthParameter("width", "Width"),
        LengthParameter("depth", "Depth")
    ];
    /// <summary>Creates default origin, subdivision, width, and depth parameters for a grid.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.90), ("originZ", 3.60), ("xSubdivisions", 10), ("ySubdivisions", 10), ("width", 2.0), ("depth", 2.0));
    /// <summary>Creates grid parameters centered on the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("xSubdivisions", 10), ("ySubdivisions", 10), ("width", Math.Max(1e-6, size.X)), ("depth", Math.Max(1e-6, size.Z)));
    }
    /// <summary>Builds a subdivided grid mesh from the width, depth, and subdivision parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddGrid(addTriangle, Origin(p, 0, -0.90, 3.60), Size(p, "width", 2.0), Size(p, "depth", 2.0), ReadInt(p, "xSubdivisions", 10, 1, 512), ReadInt(p, "ySubdivisions", 10, 1, 512), material);
    /// <summary>Applies an X or Z gizmo scale delta to the grid width or depth.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch
    {
        'X' => Multiply(p, "width", factor),
        'Z' => Multiply(p, "depth", factor),
        _ => false
    };
}

// Existing non-3D viewport helper primitives remain registered for backward compatibility
// with older .lscene documents and plug-in users, but the Composer Add Primitive menu
// filters to the standard two-finger set above plus Torus.
public sealed class LowPolySpherePrimitive : PrimitiveBase
{
    public override string Kind => "lowPolySphere";
    public override string DisplayName => "Low-poly Sphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Low-poly Sphere", true, "uniform gizmo scale updates radius", "stored as object rotation");
    /// <summary>Creates default origin, radius, and segment parameters for a low-poly sphere.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("radius", 0.52), ("longitudeSegments", 12), ("latitudeSegments", 8));
    /// <summary>Creates low-poly sphere parameters centered on the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5), ("longitudeSegments", 12), ("latitudeSegments", 8));
    }
    /// <summary>Builds a low-poly sphere mesh from the radius and segment parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddSphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 0.52), ReadInt(p, "longitudeSegments", 12, 3, 256), ReadInt(p, "latitudeSegments", 8, 2, 128), material);
    /// <summary>Applies any gizmo scale delta uniformly to the low-poly sphere radius.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
}

public sealed class HemispherePrimitive : PrimitiveBase
{
    public override string Kind => "hemisphere";
    public override string DisplayName => "Hemisphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Hemisphere", true, "X/Z scale updates radius; Y scale updates height", "stored as object rotation");
    /// <summary>Creates default origin, radius, height, and segment parameters for a hemisphere.</summary>
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.61), ("originZ", 3.55), ("radius", 0.62), ("height", 0.62), ("longitudeSegments", 32), ("latitudeSegments", 8));
    /// <summary>Creates hemisphere parameters sized to the given bounds.</summary>
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds);
        p["longitudeSegments"] = 32;
        p["latitudeSegments"] = 8;
        return p;
    }
    /// <summary>Builds an upper hemisphere mesh from the radius, height, and segment parameters.</summary>
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddHemisphere(addTriangle, Origin(p, 0, -0.61, 3.55), Size(p, "radius", 0.62), Size(p, "height", 0.62), ReadInt(p, "longitudeSegments", 32, 3, 256), ReadInt(p, "latitudeSegments", 8, 1, 128), upper: true, material);
    /// <summary>Applies a Y gizmo scale delta to height and other axes to radius.</summary>
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : Multiply(p, "radius", factor);
}
