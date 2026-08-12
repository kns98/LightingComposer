/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

// ComposerGizmoMode makes a closed set of choices compiler-visible instead of passing loosely related integers or
// strings. Code that switches over Translate, Rotate, Scale is where the behavioral meaning of each choice is
// implemented.
/// <summary>
/// Transform tool displayed in the editor viewport. The keyboard shortcuts match
/// 3D viewport's primary transform commands: G=move, R=rotate, S=scale.
/// </summary>
internal enum ComposerGizmoMode
{
    Translate,
    Rotate,
    Scale
}

// ComposerGizmoAxis makes a closed set of choices compiler-visible instead of passing loosely related integers or
// strings. Code that switches over None, X, Y, Z, Uniform is where the behavioral meaning of each choice is
// implemented.
internal enum ComposerGizmoAxis
{
    None,
    X,
    Y,
    Z,
    Uniform
}

internal readonly record struct ComposerGizmoHit(
    ComposerGizmoAxis Axis,
    double ScreenDirectionX,
    double ScreenDirectionY,
    double WorldUnitsPerPixel,
    double CenterX,
    double CenterY,
    double GestureSign,
    Vec3 WorldCenter,
    Vec3 RotationStartVector);

// ComposerOverlayRenderer turns camera/scene state into an image using one rendering backend. Its caches/resources
// are implementation details of that backend; callers should depend on the common rendered result rather than those
// internals.
/// <summary>
/// Draws editor-only selection bounds and transform controls directly over any
/// renderer output. Keeping this as a post-process makes the overlay identical
/// for software, Vulkan raster, Vulkan compute, and CPU previews.
/// </summary>
internal static class ComposerOverlayRenderer
{
    private const uint BoundsColor = 0xff40c8ffu;
    private const uint SelectionWireColor = 0xff2080ffu;
    private const uint ComponentPointColor = 0xff45e8ffu;
    private const uint ComponentEdgeColor = 0xff55d8ffu;
    private const uint HoverPointColor = 0xff56ff98u;
    private const uint HoverEdgeColor = 0xff48ffb8u;
    private const int MaximumSelectionWireTriangles = 2500;
    private const uint OriginColor = 0xffffffffu;
    private const uint XAxisColor = 0xff4545f4u;
    private const uint YAxisColor = 0xff55c96bu;
    private const uint ZAxisColor = 0xffff8d3du;
    private const int RotationRingSegments = 72;

    private readonly record struct ProjectedPoint(double X, double Y, double Depth);

    private readonly record struct AxisGeometry(
        Vec3 WorldCenter,
        double AxisWorldLength,
        ProjectedPoint Center,
        ProjectedPoint XEnd,
        ProjectedPoint YEnd,
        ProjectedPoint ZEnd);

    public static void DrawSelection(
        RenderImage image,
        CameraDefinition camera,
        Aabb bounds,
        IEnumerable<Triangle>? selectedTriangles = null,
        ComposerGizmoMode gizmoMode = ComposerGizmoMode.Translate,
        ComposerMeshSelectionVisual? meshSelection = null,
        bool drawBounds = true,
        ComposerGizmoAxis axisConstraint = ComposerGizmoAxis.None)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (camera == null) throw new ArgumentNullException(nameof(camera));

        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        Vec3 extent = bounds.Max - bounds.Min;
        if (!IsFinite(center) || !IsFinite(extent))
            return;

        if (selectedTriangles != null)
            DrawSelectedGeometryWireframe(image, camera, selectedTriangles);
        if (meshSelection != null)
            DrawMeshSelection(image, camera, meshSelection);

        if (drawBounds)
            DrawBounds(image, camera, bounds);
        if (!TryCreateAxisGeometry(camera, bounds, image.Width, image.Height, out AxisGeometry geometry))
            return;

        switch (gizmoMode)
        {
            case ComposerGizmoMode.Rotate:
                DrawRotationGizmo(image, camera, geometry);
                break;
            case ComposerGizmoMode.Scale:
                DrawScaleGizmo(image, geometry);
                break;
            default:
                DrawTranslationGizmo(image, geometry, axisConstraint);
                break;
        }
    }


    public static void DrawMeshHover(
        RenderImage image,
        CameraDefinition camera,
        ComposerMeshSelectionVisual selection)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        if (selection == null) throw new ArgumentNullException(nameof(selection));

        DrawMeshVisual(
            image,
            camera,
            selection,
            HoverPointColor,
            HoverEdgeColor,
            selection.Mode == ComposerSelectionMode.Vertex ? 10 : 6,
            edgeThickness: 7,
            drawWhiteCenter: false);
    }

    private static void DrawMeshSelection(
        RenderImage image,
        CameraDefinition camera,
        ComposerMeshSelectionVisual selection) =>
        DrawMeshVisual(
            image,
            camera,
            selection,
            ComponentPointColor,
            ComponentEdgeColor,
            selection.Mode == ComposerSelectionMode.Vertex ? 8 : 5,
            edgeThickness: 5,
            drawWhiteCenter: true);

    private static void DrawMeshVisual(
        RenderImage image,
        CameraDefinition camera,
        ComposerMeshSelectionVisual selection,
        uint pointColor,
        uint edgeColor,
        int radius,
        int edgeThickness,
        bool drawWhiteCenter)
    {
        foreach (ComposerWorldEdge edge in selection.Edges)
        {
            if (TryProject(edge.A, camera, image.Width, image.Height, out ProjectedPoint start) &&
                TryProject(edge.B, camera, image.Width, image.Height, out ProjectedPoint end))
            {
                DrawLine(image, start.X, start.Y, end.X, end.Y, edgeColor, thickness: edgeThickness);
            }
        }

        foreach (Vec3 point in selection.Points)
        {
            if (!TryProject(point, camera, image.Width, image.Height, out ProjectedPoint projected))
                continue;
            DrawDisc(image, (int)Math.Round(projected.X), (int)Math.Round(projected.Y), radius, pointColor);
            if (drawWhiteCenter)
                DrawDisc(image, (int)Math.Round(projected.X), (int)Math.Round(projected.Y), Math.Max(2, radius - 4), 0xffffffffu);
        }
    }

    public static bool TryHitGizmo(
        ComposerGizmoMode mode,
        CameraDefinition camera,
        Aabb bounds,
        int width,
        int height,
        double imageX,
        double imageY,
        out ComposerGizmoHit hit) =>
        TryHitGizmo(mode, camera, bounds, width, height, imageX, imageY, ComposerGizmoAxis.None, out hit);

    public static bool TryHitGizmo(
        ComposerGizmoMode mode,
        CameraDefinition camera,
        Aabb bounds,
        int width,
        int height,
        double imageX,
        double imageY,
        ComposerGizmoAxis axisConstraint,
        out ComposerGizmoHit hit)
    {
        return mode switch
        {
            ComposerGizmoMode.Rotate => TryHitRotationRing(camera, bounds, width, height, imageX, imageY, out hit),
            ComposerGizmoMode.Scale => TryHitScaleHandle(camera, bounds, width, height, imageX, imageY, out hit),
            _ => TryHitTranslationAxis(camera, bounds, width, height, imageX, imageY, axisConstraint, out hit)
        };
    }

    public static bool TryHitTranslationAxis(
        CameraDefinition camera,
        Aabb bounds,
        int width,
        int height,
        double imageX,
        double imageY,
        out ComposerGizmoHit hit) =>
        TryHitTranslationAxis(camera, bounds, width, height, imageX, imageY, ComposerGizmoAxis.None, out hit);

    public static bool TryHitTranslationAxis(
        CameraDefinition camera,
        Aabb bounds,
        int width,
        int height,
        double imageX,
        double imageY,
        ComposerGizmoAxis axisConstraint,
        out ComposerGizmoHit hit)
    {
        hit = default;
        if (!TryCreateAxisGeometry(camera, bounds, width, height, out AxisGeometry geometry))
            return false;

        const double hitRadius = 12.0;
        ComposerGizmoHit best = default;
        double bestDistance = double.PositiveInfinity;

        if (axisConstraint is ComposerGizmoAxis.None or ComposerGizmoAxis.X)
            TestAxis(ComposerGizmoAxis.X, geometry.Center, geometry.XEnd);
        if (axisConstraint is ComposerGizmoAxis.None or ComposerGizmoAxis.Y)
            TestAxis(ComposerGizmoAxis.Y, geometry.Center, geometry.YEnd);
        if (axisConstraint is ComposerGizmoAxis.None or ComposerGizmoAxis.Z)
            TestAxis(ComposerGizmoAxis.Z, geometry.Center, geometry.ZEnd);

        if (bestDistance > hitRadius)
            return false;

        hit = best;
        return true;

        // TestAxis evaluates axis against the current hit-test conditions and updates the best candidate only when
        // this candidate is within tolerance/closer than the previous one.
        // TestAxis evaluates axis against the current hit-test conditions and updates the best candidate only when
        // this candidate is within tolerance/closer than the previous one.
        void TestAxis(ComposerGizmoAxis axis, ProjectedPoint start, ProjectedPoint end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length) || length < 8.0)
                return;

            double distance = DistanceToSegment(imageX, imageY, start.X, start.Y, end.X, end.Y);
            if (distance >= bestDistance)
                return;

            bestDistance = distance;
            best = new ComposerGizmoHit(
                axis,
                dx / length,
                dy / length,
                geometry.AxisWorldLength / length,
                geometry.Center.X,
                geometry.Center.Y,
                1.0,
                geometry.WorldCenter,
                Vec3.Zero);
        }
    }

    private static bool TryHitScaleHandle(
        CameraDefinition camera,
        Aabb bounds,
        int width,
        int height,
        double imageX,
        double imageY,
        out ComposerGizmoHit hit)
    {
        hit = default;
        if (!TryCreateAxisGeometry(camera, bounds, width, height, out AxisGeometry geometry))
            return false;

        double centerDistance = Distance(imageX, imageY, geometry.Center.X, geometry.Center.Y);
        if (centerDistance <= 11.0)
        {
            hit = new ComposerGizmoHit(
                ComposerGizmoAxis.Uniform,
                0,
                -1,
                0,
                geometry.Center.X,
                geometry.Center.Y,
                1.0,
                geometry.WorldCenter,
                Vec3.Zero);
            return true;
        }

        ComposerGizmoHit best = default;
        double bestDistance = double.PositiveInfinity;
        TestAxis(ComposerGizmoAxis.X, geometry.XEnd);
        TestAxis(ComposerGizmoAxis.Y, geometry.YEnd);
        TestAxis(ComposerGizmoAxis.Z, geometry.ZEnd);
        if (bestDistance > 14.0)
            return false;

        hit = best;
        return true;

        void TestAxis(ComposerGizmoAxis axis, ProjectedPoint end)
        {
            double dx = end.X - geometry.Center.X;
            double dy = end.Y - geometry.Center.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length) || length < 8.0)
                return;

            double endpointDistance = Distance(imageX, imageY, end.X, end.Y);
            double lineDistance = DistanceToSegment(
                imageX, imageY,
                geometry.Center.X, geometry.Center.Y,
                end.X, end.Y);
            double distance = Math.Min(endpointDistance, lineDistance + 3.0);
            if (distance >= bestDistance)
                return;

            bestDistance = distance;
            best = new ComposerGizmoHit(
                axis,
                dx / length,
                dy / length,
                geometry.AxisWorldLength / length,
                geometry.Center.X,
                geometry.Center.Y,
                1.0,
                geometry.WorldCenter,
                Vec3.Zero);
        }
    }

    private static bool TryHitRotationRing(
        CameraDefinition camera,
        Aabb bounds,
        int width,
        int height,
        double imageX,
        double imageY,
        out ComposerGizmoHit hit)
    {
        hit = default;
        if (!TryCreateAxisGeometry(camera, bounds, width, height, out AxisGeometry geometry))
            return false;

        const double hitRadius = 11.0;
        double bestDistance = double.PositiveInfinity;
        double bestAngle = 0.0;
        ComposerGizmoAxis bestAxis = ComposerGizmoAxis.None;

        TestRing(ComposerGizmoAxis.X);
        TestRing(ComposerGizmoAxis.Y);
        TestRing(ComposerGizmoAxis.Z);

        if (bestAxis == ComposerGizmoAxis.None || bestDistance > hitRadius)
            return false;

        Vec3 axis = AxisVector(bestAxis);
        double facing = axis.Dot(camera.ToBasis().Forward);
        double gestureSign = Math.Abs(facing) < 0.12 ? 1.0 : -Math.Sign(facing);
        Vec3 startVector = TryGetRotationPlaneVector(
            camera, width, height, imageX, imageY, geometry.WorldCenter, bestAxis, out Vec3 planeVector)
            ? planeVector
            : RingDirection(bestAxis, bestAngle);
        hit = new ComposerGizmoHit(
            bestAxis,
            0,
            0,
            0,
            geometry.Center.X,
            geometry.Center.Y,
            gestureSign,
            geometry.WorldCenter,
            startVector);
        return true;

        // TestRing evaluates ring against the current hit-test conditions and updates the best candidate only when
        // this candidate is within tolerance/closer than the previous one.
        void TestRing(ComposerGizmoAxis axis)
        {
            ProjectedPoint? previous = null;
            double previousAngle = 0.0;
            for (int segment = 0; segment <= RotationRingSegments; segment++)
            {
                double angle = Math.PI * 2.0 * segment / RotationRingSegments;
                Vec3 point = RingPoint(geometry.WorldCenter, geometry.AxisWorldLength * 0.92, axis, angle);
                if (!TryProject(point, camera, width, height, out ProjectedPoint projected))
                {
                    previous = null;
                    previousAngle = angle;
                    continue;
                }

                if (previous is ProjectedPoint start)
                {
                    double parameter = ClosestPointParameter(
                        imageX, imageY, start.X, start.Y, projected.X, projected.Y);
                    double closestX = start.X + (projected.X - start.X) * parameter;
                    double closestY = start.Y + (projected.Y - start.Y) * parameter;
                    double distance = Distance(imageX, imageY, closestX, closestY);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestAxis = axis;
                        bestAngle = previousAngle + (angle - previousAngle) * parameter;
                    }
                }
                previous = projected;
                previousAngle = angle;
            }
        }
    }

    /// <summary>
    /// Maps a pointer to a unit direction in the selected world-space rotation
    /// plane. This is the primary 3D viewport-like drag path because its angular
    /// response remains stable when a projected ring is elliptical.
    /// </summary>
    public static bool TryGetRotationPlaneVector(
        CameraDefinition camera,
        int width,
        int height,
        double imageX,
        double imageY,
        Vec3 worldCenter,
        ComposerGizmoAxis axis,
        out Vec3 vector)
    {
        Vec3 axisVector = AxisVector(axis);
        if (axisVector.Length() < 0.5 || width <= 0 || height <= 0)
        {
            vector = Vec3.Zero;
            return false;
        }

        Vec3 rayDirection = RayTracer.RayDirection(
            imageX, imageY, width, height, camera.ToBasis(), camera.FieldOfViewDegrees);
        double denominator = rayDirection.Dot(axisVector);
        if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-5)
        {
            vector = Vec3.Zero;
            return false;
        }

        double distance = (worldCenter - camera.Position).Dot(axisVector) / denominator;
        if (!double.IsFinite(distance) || distance <= 1e-6)
        {
            vector = Vec3.Zero;
            return false;
        }

        Vec3 offset = camera.Position + rayDirection * distance - worldCenter;
        offset -= axisVector * offset.Dot(axisVector);
        double length = offset.Length();
        if (!double.IsFinite(length) || length <= 1e-8)
        {
            vector = Vec3.Zero;
            return false;
        }

        vector = offset / length;
        return true;
    }

    // DrawBounds rasterizes bounds into the target image/overlay using projected geometry. Drawing is kept separate
    // from scene mutation so gizmos/highlights remain presentation-only.
    private static void DrawBounds(RenderImage image, CameraDefinition camera, Aabb bounds)
    {
        Vec3[] corners =
        [
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z)
        ];

        int[,] edges =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        for (int i = 0; i < edges.GetLength(0); i++)
        {
            if (TryProject(corners[edges[i, 0]], camera, image.Width, image.Height, out ProjectedPoint start) &&
                TryProject(corners[edges[i, 1]], camera, image.Width, image.Height, out ProjectedPoint end))
            {
                DrawLine(image, start.X, start.Y, end.X, end.Y, BoundsColor, thickness: 2);
            }
        }
    }

    private static void DrawTranslationGizmo(
        RenderImage image,
        AxisGeometry geometry,
        ComposerGizmoAxis axisConstraint)
    {
        if (axisConstraint is ComposerGizmoAxis.None or ComposerGizmoAxis.X)
            DrawAxis(image, geometry.Center, geometry.XEnd, XAxisColor, squareHandle: false);
        if (axisConstraint is ComposerGizmoAxis.None or ComposerGizmoAxis.Y)
            DrawAxis(image, geometry.Center, geometry.YEnd, YAxisColor, squareHandle: false);
        if (axisConstraint is ComposerGizmoAxis.None or ComposerGizmoAxis.Z)
            DrawAxis(image, geometry.Center, geometry.ZEnd, ZAxisColor, squareHandle: false);
        DrawHandle(image, geometry.Center.X, geometry.Center.Y, OriginColor, radius: 4);
    }

    // DrawScaleGizmo rasterizes scale gizmo into the target image/overlay using projected geometry. Drawing is kept
    // separate from scene mutation so gizmos/highlights remain presentation-only.
    private static void DrawScaleGizmo(RenderImage image, AxisGeometry geometry)
    {
        DrawAxis(image, geometry.Center, geometry.XEnd, XAxisColor, squareHandle: true);
        DrawAxis(image, geometry.Center, geometry.YEnd, YAxisColor, squareHandle: true);
        DrawAxis(image, geometry.Center, geometry.ZEnd, ZAxisColor, squareHandle: true);
        DrawSquareHandle(image, geometry.Center.X, geometry.Center.Y, OriginColor, halfSize: 6);
    }

    // DrawRotationGizmo rasterizes rotation gizmo into the target image/overlay using projected geometry. Drawing
    // is kept separate from scene mutation so gizmos/highlights remain presentation-only.
    private static void DrawRotationGizmo(RenderImage image, CameraDefinition camera, AxisGeometry geometry)
    {
        DrawRing(ComposerGizmoAxis.X, XAxisColor);
        DrawRing(ComposerGizmoAxis.Y, YAxisColor);
        DrawRing(ComposerGizmoAxis.Z, ZAxisColor);
        DrawHandle(image, geometry.Center.X, geometry.Center.Y, OriginColor, radius: 3);

        // DrawRing rasterizes ring into the target image/overlay using projected geometry. Drawing is kept separate
        // from scene mutation so gizmos/highlights remain presentation-only.
        void DrawRing(ComposerGizmoAxis axis, uint color)
        {
            ProjectedPoint? previous = null;
            for (int segment = 0; segment <= RotationRingSegments; segment++)
            {
                double angle = Math.PI * 2.0 * segment / RotationRingSegments;
                Vec3 point = RingPoint(geometry.WorldCenter, geometry.AxisWorldLength * 0.92, axis, angle);
                if (!TryProject(point, camera, image.Width, image.Height, out ProjectedPoint projected))
                {
                    previous = null;
                    continue;
                }

                if (previous is ProjectedPoint start)
                    DrawLine(image, start.X, start.Y, projected.X, projected.Y, color, thickness: 3);
                previous = projected;
            }
        }
    }

    private static Vec3 RingPoint(Vec3 center, double radius, ComposerGizmoAxis axis, double angle)
    {
        double a = Math.Cos(angle) * radius;
        double b = Math.Sin(angle) * radius;
        return axis switch
        {
            ComposerGizmoAxis.X => center + new Vec3(0, a, b),
            ComposerGizmoAxis.Y => center + new Vec3(a, 0, b),
            _ => center + new Vec3(a, b, 0)
        };
    }

    private static Vec3 RingDirection(ComposerGizmoAxis axis, double angle)
    {
        double a = Math.Cos(angle);
        double b = Math.Sin(angle);
        return axis switch
        {
            ComposerGizmoAxis.X => new Vec3(0, a, b),
            ComposerGizmoAxis.Y => new Vec3(a, 0, b),
            _ => new Vec3(a, b, 0)
        };
    }

    private static Vec3 AxisVector(ComposerGizmoAxis axis) => axis switch
    {
        ComposerGizmoAxis.X => new Vec3(1, 0, 0),
        ComposerGizmoAxis.Y => new Vec3(0, 1, 0),
        ComposerGizmoAxis.Z => new Vec3(0, 0, 1),
        _ => Vec3.Zero
    };

    private static void DrawSelectedGeometryWireframe(
        RenderImage image,
        CameraDefinition camera,
        IEnumerable<Triangle> triangles)
    {
        int drawn = 0;
        foreach (Triangle triangle in triangles)
        {
            if (drawn++ >= MaximumSelectionWireTriangles)
                break;

            if (!TryProject(triangle.A, camera, image.Width, image.Height, out ProjectedPoint a) ||
                !TryProject(triangle.B, camera, image.Width, image.Height, out ProjectedPoint b) ||
                !TryProject(triangle.C, camera, image.Width, image.Height, out ProjectedPoint c))
            {
                continue;
            }

            DrawLine(image, a.X, a.Y, b.X, b.Y, SelectionWireColor, thickness: 1);
            DrawLine(image, b.X, b.Y, c.X, c.Y, SelectionWireColor, thickness: 1);
            DrawLine(image, c.X, c.Y, a.X, a.Y, SelectionWireColor, thickness: 1);
        }
    }

    private static bool TryCreateAxisGeometry(
        CameraDefinition camera,
        Aabb bounds,
        int width,
        int height,
        out AxisGeometry geometry)
    {
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        Vec3 extent = bounds.Max - bounds.Min;
        if (!IsFinite(center) || !IsFinite(extent))
        {
            geometry = default;
            return false;
        }

        double maximumExtent = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        double distance = (center - camera.Position).Length();
        double axisLength = Math.Max(maximumExtent * 0.45, distance * 0.075);
        axisLength = Math.Max(axisLength, 0.05);

        if (!TryProject(center, camera, width, height, out ProjectedPoint projectedCenter) ||
            !TryProject(center + new Vec3(axisLength, 0, 0), camera, width, height, out ProjectedPoint xEnd) ||
            !TryProject(center + new Vec3(0, axisLength, 0), camera, width, height, out ProjectedPoint yEnd) ||
            !TryProject(center + new Vec3(0, 0, axisLength), camera, width, height, out ProjectedPoint zEnd))
        {
            geometry = default;
            return false;
        }

        geometry = new AxisGeometry(center, axisLength, projectedCenter, xEnd, yEnd, zEnd);
        return true;
    }

    private static double ClosestPointParameter(
        double px, double py,
        double x0, double y0,
        double x1, double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double denominator = dx * dx + dy * dy;
        if (denominator < 1e-12)
            return 0.0;
        return Math.Clamp(((px - x0) * dx + (py - y0) * dy) / denominator, 0.0, 1.0);
    }

    private static double DistanceToSegment(
        double px, double py,
        double x0, double y0,
        double x1, double y1)
    {
        double t = ClosestPointParameter(px, py, x0, y0, x1, y1);
        return Distance(px, py, x0 + t * (x1 - x0), y0 + t * (y1 - y0));
    }

    // Distance computes Euclidean distance between the supplied points/components. In hit-testing code this
    // converts geometric proximity into a scalar that can be compared with a pixel/selection tolerance.
    private static double Distance(double x0, double y0, double x1, double y1)
    {
        double dx = x0 - x1;
        double dy = y0 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void DrawAxis(
        RenderImage image,
        ProjectedPoint start,
        ProjectedPoint end,
        uint color,
        bool squareHandle)
    {
        DrawLine(image, start.X, start.Y, end.X, end.Y, color, thickness: 4);
        if (squareHandle)
            DrawSquareHandle(image, end.X, end.Y, color, halfSize: 6);
        else
            DrawHandle(image, end.X, end.Y, color, radius: 6);
    }

    private static bool TryProject(
        Vec3 point,
        CameraDefinition camera,
        int width,
        int height,
        out ProjectedPoint projected)
    {
        CameraBasis basis = camera.ToBasis();
        Vec3 relative = point - camera.Position;
        double depth = relative.Dot(basis.Forward);
        if (!double.IsFinite(depth) || depth <= Math.Max(1e-5, camera.NearPlane * 0.25))
        {
            projected = default;
            return false;
        }

        double safeFov = Math.Clamp(camera.FieldOfViewDegrees, 1.0, 179.0);
        double tangent = Math.Tan((safeFov * Math.PI / 180.0) * 0.5);
        double aspect = width / (double)Math.Max(1, height);
        double horizontal = relative.Dot(basis.Right) / depth;
        double vertical = relative.Dot(basis.Up) / depth;

        double x = width * 0.5 * (1.0 - horizontal / (aspect * tangent));
        double y = height * 0.5 * (1.0 - vertical / tangent);
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            projected = default;
            return false;
        }

        projected = new ProjectedPoint(x, y, depth);
        return true;
    }

    private static void DrawLine(
        RenderImage image,
        double x0,
        double y0,
        double x1,
        double y1,
        uint color,
        int thickness)
    {
        if (!ClipLine(image.Width, image.Height, ref x0, ref y0, ref x1, ref y1))
            return;

        int startX = (int)Math.Round(x0);
        int startY = (int)Math.Round(y0);
        int endX = (int)Math.Round(x1);
        int endY = (int)Math.Round(y1);
        int dx = Math.Abs(endX - startX);
        int sx = startX < endX ? 1 : -1;
        int dy = -Math.Abs(endY - startY);
        int sy = startY < endY ? 1 : -1;
        int error = dx + dy;

        while (true)
        {
            DrawDisc(image, startX, startY, Math.Max(0, thickness / 2), color);
            if (startX == endX && startY == endY)
                break;

            int twiceError = error * 2;
            if (twiceError >= dy)
            {
                error += dy;
                startX += sx;
            }
            if (twiceError <= dx)
            {
                error += dx;
                startY += sy;
            }
        }
    }

    // DrawHandle rasterizes handle into the target image/overlay using projected geometry. Drawing is kept separate
    // from scene mutation so gizmos/highlights remain presentation-only.
    private static void DrawHandle(RenderImage image, double x, double y, uint color, int radius)
    {
        int centerX = (int)Math.Round(x);
        int centerY = (int)Math.Round(y);
        DrawDisc(image, centerX, centerY, radius, color);
        DrawDisc(image, centerX, centerY, Math.Max(1, radius - 3), 0xffffffffu);
    }

    // DrawSquareHandle rasterizes square handle into the target image/overlay using projected geometry. Drawing is
    // kept separate from scene mutation so gizmos/highlights remain presentation-only.
    private static void DrawSquareHandle(RenderImage image, double x, double y, uint color, int halfSize)
    {
        int centerX = (int)Math.Round(x);
        int centerY = (int)Math.Round(y);
        for (int offsetY = -halfSize; offsetY <= halfSize; offsetY++)
        {
            for (int offsetX = -halfSize; offsetX <= halfSize; offsetX++)
            {
                bool border = Math.Abs(offsetX) >= halfSize - 2 || Math.Abs(offsetY) >= halfSize - 2;
                BlendPixel(image, centerX + offsetX, centerY + offsetY, border ? color : 0xffffffffu);
            }
        }
    }

    // DrawDisc rasterizes disc into the target image/overlay using projected geometry. Drawing is kept separate
    // from scene mutation so gizmos/highlights remain presentation-only.
    private static void DrawDisc(RenderImage image, int centerX, int centerY, int radius, uint color)
    {
        int squaredRadius = radius * radius;
        for (int offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if (offsetX * offsetX + offsetY * offsetY > squaredRadius)
                    continue;
                BlendPixel(image, centerX + offsetX, centerY + offsetY, color);
            }
        }
    }

    // BlendPixel combines pixel with the destination pixel using alpha/color composition rather than overwriting it
    // outright, allowing translucent overlays to remain readable over the rendered scene.
    private static void BlendPixel(RenderImage image, int x, int y, uint source)
    {
        if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
            return;

        int index = y * image.Width + x;
        uint destination = image.PackedRgba32[index];
        const int sourceWeight = 220;
        const int destinationWeight = 255 - sourceWeight;

        int red = (((int)source & 0xff) * sourceWeight + ((int)destination & 0xff) * destinationWeight) / 255;
        int green = (((int)(source >> 8) & 0xff) * sourceWeight + ((int)(destination >> 8) & 0xff) * destinationWeight) / 255;
        int blue = (((int)(source >> 16) & 0xff) * sourceWeight + ((int)(destination >> 16) & 0xff) * destinationWeight) / 255;
        image.PackedRgba32[index] = (uint)(red | (green << 8) | (blue << 16)) | 0xff000000u;
    }

    private static bool ClipLine(
        int width,
        int height,
        ref double x0,
        ref double y0,
        ref double x1,
        ref double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double t0 = 0.0;
        double t1 = 1.0;

        if (!Clip(-dx, x0, ref t0, ref t1) ||
            !Clip(dx, width - 1.0 - x0, ref t0, ref t1) ||
            !Clip(-dy, y0, ref t0, ref t1) ||
            !Clip(dy, height - 1.0 - y0, ref t0, ref t1))
        {
            return false;
        }

        double originalX = x0;
        double originalY = y0;
        x0 = originalX + t0 * dx;
        y0 = originalY + t0 * dy;
        x1 = originalX + t1 * dx;
        y1 = originalY + t1 * dy;
        return true;
    }

    private static bool Clip(double denominator, double numerator, ref double t0, ref double t1)
    {
        if (Math.Abs(denominator) < 1e-12)
            return numerator >= 0.0;

        double ratio = numerator / denominator;
        if (denominator < 0.0)
        {
            if (ratio > t1) return false;
            if (ratio > t0) t0 = ratio;
        }
        else
        {
            if (ratio < t0) return false;
            if (ratio < t1) t1 = ratio;
        }
        return true;
    }

    // IsFinite rejects NaN and infinity so geometry/projection code does not feed non-finite coordinates into
    // clipping, rasterization, bounds, or scene transforms.
    private static bool IsFinite(Vec3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}
