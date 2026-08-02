using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

internal static class CpuComposerRenderer
{
    public static RenderImage Render(
        Scene scene,
        CameraDefinition camera,
        int width,
        int height,
        CancellationToken cancellationToken,
        out string details)
    {
        RayTracer tracer = new(scene, new LightingState());
        CameraBasis basis = camera.ToBasis();
        uint[] pixels = new uint[checked(width * height)];
        ParallelOptions options = new() { CancellationToken = cancellationToken };

        Parallel.For(0, height, options, y =>
        {
            for (int x = 0; x < width; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Vec3 direction = RayTracer.RayDirection(
                    x + 0.5,
                    y + 0.5,
                    width,
                    height,
                    basis,
                    camera.FieldOfViewDegrees);
                Vec3 linear = tracer.TracePath(new Ray(camera.Position, direction), 1, x, y, 0);
                pixels[y * width + x] = PackDisplayColor(linear);
            }
        });

        details = $"CPU preview - {width}x{height}, samples=1, bounces=1";
        return new RenderImage(width, height, pixels);
    }

    private static uint PackDisplayColor(Vec3 linear)
    {
        Vec3 display = RayTracer.ToDisplayColor(linear);
        byte red = ToByte(display.X);
        byte green = ToByte(display.Y);
        byte blue = ToByte(display.Z);
        return red | ((uint)green << 8) | ((uint)blue << 16) | 0xff000000u;
    }

    private static byte ToByte(double value)
    {
        if (!double.IsFinite(value)) return 0;
        return (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0), 0, 255);
    }
}
