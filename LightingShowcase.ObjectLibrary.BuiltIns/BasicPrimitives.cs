/*
 * Object-library definitions generate scene geometry from named, authored parameters. Keeping those parameters
 * attached to the generated object is important: a cube with width/height/depth is still editable as a cube until
 * a topology edit deliberately converts it into ordinary mesh geometry.
 */
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

// PlanePrimitive is the procedural definition for a plane. It knows how to turn authored parameters into triangles
// and, where supported, how to absorb object-scale changes back into those parameters so the object remains
// editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.90), ("originZ", 3.60), ("width", 2.0), ("depth", 2.0));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("width", Math.Max(1e-6, size.X)), ("depth", Math.Max(1e-6, size.Z)));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddGrid(addTriangle, Origin(p, 0, -0.90, 3.60), Size(p, "width", 2.0), Size(p, "depth", 2.0), 1, 1, material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch
    {
        'X' => Multiply(p, "width", factor),
        'Z' => Multiply(p, "depth", factor),
        _ => false
    };
}

// CubePrimitive is the procedural definition for a cube. It knows how to turn authored parameters into triangles
// and, where supported, how to absorb object-scale changes back into those parameters so the object remains
// editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.50), ("originZ", 3.50), ("width", 1.0), ("height", 1.0), ("depth", 1.0));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return BoxParameters(bounds, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        Box(addTriangle, Origin(p, 0, -0.50, 3.50), Size(p, "width", 1.0), Size(p, "height", 1.0), Size(p, "depth", 1.0), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch
    {
        'X' => Multiply(p, "width", factor),
        'Y' => Multiply(p, "height", factor),
        _ => Multiply(p, "depth", factor)
    };
}

// CirclePrimitive is the procedural definition for a circle. It knows how to turn authored parameters into
// triangles and, where supported, how to absorb object-scale changes back into those parameters so the object
// remains editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.90), ("originZ", 3.60), ("vertices", 32), ("radius", 1.0), ("fillType", 1));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("vertices", 32), ("radius", Math.Max(size.X, size.Z) * 0.5), ("fillType", 1));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddCircle(addTriangle, Origin(p, 0, -0.90, 3.60), Size(p, "radius", 1.0), ReadInt(p, "vertices", 32, 3, 512), ReadInt(p, "fillType", 1, 0, 2), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

// SpherePrimitive is the procedural definition for a sphere. It knows how to turn authored parameters into
// triangles and, where supported, how to absorb object-scale changes back into those parameters so the object
// remains editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("radius", 1.0), ("longitudeSegments", 32), ("latitudeSegments", 16));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5), ("longitudeSegments", 32), ("latitudeSegments", 16));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddSphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 1.0), ReadInt(p, "longitudeSegments", 32, 3, 512), ReadInt(p, "latitudeSegments", 16, 2, 256), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Y)), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

// IcospherePrimitive is the procedural definition for a icosphere. It knows how to turn authored parameters into
// triangles and, where supported, how to absorb object-scale changes back into those parameters so the object
// remains editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("subdivisions", 2), ("radius", 1.0));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("subdivisions", 2), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddIcosphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 1.0), ReadInt(p, "subdivisions", 2, 1, 7), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Y)), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

// CylinderPrimitive is the procedural definition for a cylinder. It knows how to turn authored parameters into
// triangles and, where supported, how to absorb object-scale changes back into those parameters so the object
// remains editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.575), ("originZ", 3.55), ("radius", 0.5), ("height", 2.0), ("sides", 32), ("capFill", 1));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds);
        p["sides"] = 32;
        p["capFill"] = 1;
        return p;
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddFrustum(addTriangle, Origin(p, 0, -0.575, 3.55), Size(p, "radius", 0.5), Size(p, "radius", 0.5), Size(p, "height", 2.0), ReadInt(p, "sides", 32, 3, 512), ReadInt(p, "capFill", 1, 0, 1) != 0, material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : Multiply(p, "radius", factor);
}

// ConePrimitive is the procedural definition for a cone. It knows how to turn authored parameters into triangles
// and, where supported, how to absorb object-scale changes back into those parameters so the object remains
// editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.525), ("originZ", 3.55), ("radius1", 0.5), ("radius2", 0.0), ("height", 2.0), ("sides", 32), ("capFill", 1));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds, "radius1", "height");
        p["radius2"] = 0.0;
        p["sides"] = 32;
        p["capFill"] = 1;
        return p;
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle)
    {
        // Backward compatibility: old scenes used "radius" for the cone base.
        double r1 = p.ContainsKey("radius1") ? Math.Max(0.0, Read(p, "radius1", 0.5)) : Size(p, "radius", 0.5);
        double r2 = Math.Max(0.0, Read(p, "radius2", 0.0));
        AddFrustum(addTriangle, Origin(p, 0, -0.525, 3.55), r1, r2, Size(p, "height", 2.0), ReadInt(p, "sides", 32, 3, 512), ReadInt(p, "capFill", 1, 0, 1) != 0, material);
    }
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y'
        ? Multiply(p, "height", factor)
        : MultiplyAny(p, factor, p.ContainsKey("radius1") ? "radius1" : "radius", "radius2");
}

// GridPrimitive is the procedural definition for a grid. It knows how to turn authored parameters into triangles
// and, where supported, how to absorb object-scale changes back into those parameters so the object remains
// editable as a named primitive rather than becoming anonymous mesh data.
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
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.90), ("originZ", 3.60), ("xSubdivisions", 10), ("ySubdivisions", 10), ("width", 2.0), ("depth", 2.0));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("xSubdivisions", 10), ("ySubdivisions", 10), ("width", Math.Max(1e-6, size.X)), ("depth", Math.Max(1e-6, size.Z)));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) =>
        AddGrid(addTriangle, Origin(p, 0, -0.90, 3.60), Size(p, "width", 2.0), Size(p, "depth", 2.0), ReadInt(p, "xSubdivisions", 10, 1, 512), ReadInt(p, "ySubdivisions", 10, 1, 512), material);
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
// LowPolySpherePrimitive is the procedural definition for a low poly sphere. It knows how to turn authored
// parameters into triangles and, where supported, how to absorb object-scale changes back into those parameters so
// the object remains editable as a named primitive rather than becoming anonymous mesh data.
public sealed class LowPolySpherePrimitive : PrimitiveBase
{
    public override string Kind => "lowPolySphere";
    public override string DisplayName => "Low-poly Sphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Low-poly Sphere", true, "uniform gizmo scale updates radius", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("radius", 0.52), ("longitudeSegments", 12), ("latitudeSegments", 8));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5), ("longitudeSegments", 12), ("latitudeSegments", 8));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddSphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 0.52), ReadInt(p, "longitudeSegments", 12, 3, 256), ReadInt(p, "latitudeSegments", 8, 2, 128), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
}

// HemispherePrimitive is the procedural definition for a hemisphere. It knows how to turn authored parameters into
// triangles and, where supported, how to absorb object-scale changes back into those parameters so the object
// remains editable as a named primitive rather than becoming anonymous mesh data.
public sealed class HemispherePrimitive : PrimitiveBase
{
    public override string Kind => "hemisphere";
    public override string DisplayName => "Hemisphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Hemisphere", true, "X/Z scale updates radius; Y scale updates height", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.61), ("originZ", 3.55), ("radius", 0.62), ("height", 0.62), ("longitudeSegments", 32), ("latitudeSegments", 8));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds);
        p["longitudeSegments"] = 32;
        p["latitudeSegments"] = 8;
        return p;
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddHemisphere(addTriangle, Origin(p, 0, -0.61, 3.55), Size(p, "radius", 0.62), Size(p, "height", 0.62), ReadInt(p, "longitudeSegments", 32, 3, 256), ReadInt(p, "latitudeSegments", 8, 1, 128), upper: true, material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : Multiply(p, "radius", factor);
}
