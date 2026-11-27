// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerCamera.cs
// Purpose: Orbit camera model for the composer viewport.
//
// Keeps target, radius, yaw, pitch, and field of view, and snapshots them into a CameraDefinition with derived near and far planes.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal sealed class ComposerCamera
{
    private Vec3 target;
    private double radius = 5.0;
    private double yaw = 0.32;
    private double pitch = 0.18;

    public double FieldOfViewDegrees { get; set; } = 72.0;

    /// <summary>Builds a camera definition from the current orbit target, radius, yaw, pitch, and field of view.</summary>
    public CameraDefinition Snapshot()
    {
        double horizontal = radius * Math.Cos(pitch);
        Vec3 position = target + new Vec3(
            horizontal * Math.Sin(yaw),
            radius * Math.Sin(pitch),
            -horizontal * Math.Cos(yaw));

        return new CameraDefinition
        {
            Position = position,
            Target = target,
            Up = new Vec3(0, 1, 0),
            FieldOfViewDegrees = FieldOfViewDegrees,
            NearPlane = Math.Max(0.01, radius / 500.0),
            FarPlane = Math.Max(5000.0, radius * 40.0)
        };
    }

    /// <summary>Moves the orbit camera to match the given view definition.</summary>
    public void ApplyView(CameraDefinition view)
    {
        ArgumentNullException.ThrowIfNull(view);
        target = view.Target;
        Vec3 offset = view.Position - view.Target;
        radius = Math.Max(0.05, offset.Length());
        double horizontal = Math.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        yaw = Math.Atan2(offset.X, -offset.Z);
        pitch = Math.Clamp(Math.Atan2(offset.Y, horizontal), -1.45, 1.45);
        FieldOfViewDegrees = view.FieldOfViewDegrees;
    }

    /// <summary>Resets the orbit camera to the default view or frames the scene triangles.</summary>
    public void Reset(Scene scene)
    {
        if (scene.Triangles.Count == 0)
        {
            target = new Vec3(0, 0.5, 0);
            radius = 5.0;
            yaw = 0.32;
            pitch = 0.18;
            return;
        }

        Frame(ComputeBounds(scene.Triangles));
    }

    /// <summary>Frames the given bounds by recentering the target and fitting the radius to the extent.</summary>
    public void Frame(Aabb bounds)
    {
        target = (bounds.Min + bounds.Max) * 0.5;
        Vec3 extent = bounds.Max - bounds.Min;
        radius = Math.Max(0.5, extent.Length() * 1.25);
        yaw = 0.32;
        pitch = 0.18;
    }

    /// <summary>Rotates the camera yaw and clamped pitch by the given pointer deltas.</summary>
    public void Orbit(double deltaX, double deltaY)
    {
        yaw -= deltaX * 0.008;
        pitch = Math.Clamp(pitch + deltaY * 0.008, -1.45, 1.45);
    }

    /// <summary>Pans the camera target from screen deltas using world units per pixel.</summary>
    public void Pan(double deltaX, double deltaY, double viewportHeight)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
            return;

        CameraDefinition camera = Snapshot();
        CameraBasis basis = camera.ToBasis();
        double safeHeight = Math.Max(1.0, viewportHeight);
        double verticalWorldSize = 2.0 * radius * Math.Tan(
            Math.Clamp(FieldOfViewDegrees, 1.0, 179.0) * Math.PI / 360.0);
        double worldUnitsPerPixel = verticalWorldSize / safeHeight;

        // Dragging the view to the right moves the scene to the right, so the
        // camera target moves opposite its local right vector. Vertical drag is
        // mapped directly to the corrected camera up vector.
        target += basis.Right * (-deltaX * worldUnitsPerPixel);
        target += basis.Up * (deltaY * worldUnitsPerPixel);
    }

    /// <summary>Zooms the camera by scaling the orbit radius from the wheel delta.</summary>
    public void Zoom(double wheelDelta)
    {
        radius *= Math.Exp(-wheelDelta * 0.12);
        radius = Math.Clamp(radius, 0.05, 100000.0);
    }

    /// <summary>
    /// Turntable-style rotation around the scene target.
    ///
    /// Unlike camera roll, this keeps world-up fixed. A circular two-finger
    /// gesture therefore makes the scene turn left/right on screen instead of
    /// tilting or flipping the image plane.
    /// </summary>
    public void Turn(double deltaRadians)
    {
        if (!double.IsFinite(deltaRadians))
            return;

        yaw += deltaRadians;

        while (yaw > Math.PI)
            yaw -= Math.PI * 2.0;
        while (yaw < -Math.PI)
            yaw += Math.PI * 2.0;
    }

    /// <summary>Computes the axis-aligned bounds of the given triangles.</summary>
    private static Aabb ComputeBounds(IReadOnlyList<Triangle> triangles)
    {
        Vec3 first = triangles[0].A;
        double minX = first.X, minY = first.Y, minZ = first.Z;
        double maxX = first.X, maxY = first.Y, maxZ = first.Z;

        foreach (Triangle triangle in triangles)
        {
            Expand(triangle.A);
            Expand(triangle.B);
            Expand(triangle.C);
        }

        return new Aabb(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));

        /// <summary>Expands the running bounds to include the given vertex.</summary>
        void Expand(Vec3 value)
        {
            minX = Math.Min(minX, value.X);
            minY = Math.Min(minY, value.Y);
            minZ = Math.Min(minZ, value.Z);
            maxX = Math.Max(maxX, value.X);
            maxY = Math.Max(maxY, value.Y);
            maxZ = Math.Max(maxZ, value.Z);
        }
    }
}
