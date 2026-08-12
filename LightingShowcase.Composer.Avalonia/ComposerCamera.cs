/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

// ComposerCamera provides the mutable/editor-facing camera operations while exposing renderer snapshots so a frame
// sees a consistent camera even if interaction continues.
internal sealed class ComposerCamera
{
    private Vec3 target;
    private double radius = 5.0;
    private double yaw = 0.32;
    private double pitch = 0.18;

    public double FieldOfViewDegrees { get; set; } = 72.0;

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

    public void Frame(Aabb bounds)
    {
        target = (bounds.Min + bounds.Max) * 0.5;
        Vec3 extent = bounds.Max - bounds.Min;
        radius = Math.Max(0.5, extent.Length() * 1.25);
        yaw = 0.32;
        pitch = 0.18;
    }

    public void Orbit(double deltaX, double deltaY)
    {
        yaw -= deltaX * 0.008;
        pitch = Math.Clamp(pitch + deltaY * 0.008, -1.45, 1.45);
    }

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

    // ComputeBounds calculates bounds deterministically from its inputs; callers can use the result as derived
    // data/cache evidence without mutating the underlying scene.
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
