using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>
/// Draws editor-only selection bounds and transform axes directly over any
/// renderer output. Keeping this as a post-process makes the overlay identical
/// for software, Vulkan raster, Vulkan compute, and CPU previews.
/// </summary>
internal enum ComposerGizmoAxis
{
    None,
    X,
    Y,
    Z
}

internal readonly record struct ComposerGizmoHit(
    ComposerGizmoAxis Axis,
    double ScreenDirectionX,
    double ScreenDirectionY,
    double WorldUnitsPerPixel);

internal static class ComposerOverlayRenderer
{
    private const uint BoundsColor = 0xff40c8ffu;
    private const uint SelectionWireColor = 0xff2080ffu;
    private const int MaximumSelectionWireTriangles = 2500;
    private const uint OriginColor = 0xffffffffu;
    private const uint XAxisColor = 0xff4545f4u;
    private const uint YAxisColor = 0xff55c96bu;
    private const uint ZAxisColor = 0xffff8d3du;

    private readonly record struct ProjectedPoint(double X, double Y, double Depth);

    public static void DrawSelection(
        RenderImage image,
        CameraDefinition camera,
        Aabb bounds,
        IEnumerable<Triangle>? selectedTriangles = null)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (camera == null) throw new ArgumentNullException(nameof(camera));

        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        Vec3 extent = bounds.Max - bounds.Min;
        if (!IsFinite(center) || !IsFinite(extent))
            return;

        if (selectedTriangles != null)
            DrawSelectedGeometryWireframe(image, camera, selectedTriangles);

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

        double maximumExtent = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        double distance = (center - camera.Position).Length();
        double axisLength = Math.Max(maximumExtent * 0.45, distance * 0.075);
        axisLength = Math.Max(axisLength, 0.05);

        DrawAxis(image, camera, center, center + new Vec3(axisLength, 0, 0), XAxisColor);
        DrawAxis(image, camera, center, center + new Vec3(0, axisLength, 0), YAxisColor);
        DrawAxis(image, camera, center, center + new Vec3(0, 0, axisLength), ZAxisColor);

        if (TryProject(center, camera, image.Width, image.Height, out ProjectedPoint projectedCenter))
            DrawHandle(image, projectedCenter.X, projectedCenter.Y, OriginColor, radius: 4);
    }


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


    public static bool TryHitTranslationAxis(
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

        const double hitRadius = 12.0;
        ComposerGizmoHit best = default;
        double bestDistance = double.PositiveInfinity;

        TestAxis(ComposerGizmoAxis.X, geometry.Center, geometry.XEnd);
        TestAxis(ComposerGizmoAxis.Y, geometry.Center, geometry.YEnd);
        TestAxis(ComposerGizmoAxis.Z, geometry.Center, geometry.ZEnd);

        if (bestDistance > hitRadius)
            return false;

        hit = best;
        return true;

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
                geometry.AxisWorldLength / length);
        }
    }

    private readonly record struct AxisGeometry(
        ProjectedPoint Center,
        ProjectedPoint XEnd,
        ProjectedPoint YEnd,
        ProjectedPoint ZEnd,
        double AxisWorldLength);

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

        geometry = new AxisGeometry(projectedCenter, xEnd, yEnd, zEnd, axisLength);
        return true;
    }

    private static double DistanceToSegment(
        double px, double py,
        double x0, double y0,
        double x1, double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double denominator = dx * dx + dy * dy;
        if (denominator < 1e-12)
            return Math.Sqrt((px - x0) * (px - x0) + (py - y0) * (py - y0));

        double t = ((px - x0) * dx + (py - y0) * dy) / denominator;
        t = Math.Clamp(t, 0.0, 1.0);
        double nearestX = x0 + t * dx;
        double nearestY = y0 + t * dy;
        double offsetX = px - nearestX;
        double offsetY = py - nearestY;
        return Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
    }

    private static void DrawAxis(
        RenderImage image,
        CameraDefinition camera,
        Vec3 origin,
        Vec3 endpoint,
        uint color)
    {
        if (!TryProject(origin, camera, image.Width, image.Height, out ProjectedPoint start) ||
            !TryProject(endpoint, camera, image.Width, image.Height, out ProjectedPoint end))
        {
            return;
        }

        DrawLine(image, start.X, start.Y, end.X, end.Y, color, thickness: 4);
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

    private static void DrawHandle(RenderImage image, double x, double y, uint color, int radius)
    {
        int centerX = (int)Math.Round(x);
        int centerY = (int)Math.Round(y);
        DrawDisc(image, centerX, centerY, radius, color);
        DrawDisc(image, centerX, centerY, Math.Max(1, radius - 3), 0xffffffffu);
    }

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

    private static bool IsFinite(Vec3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}
