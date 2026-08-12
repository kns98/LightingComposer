/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

// CpuComposerRenderer turns camera/scene state into an image using one rendering backend. Its caches/resources are
// implementation details of that backend; callers should depend on the common rendered result rather than those
// internals.
internal static class CpuComposerRenderer
{
    public static RenderImage Render(
        Scene scene,
        CameraDefinition camera,
        int width,
        int height,
        ComposerRenderOptions options,
        CancellationToken cancellationToken,
        out string details)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        RayTracer tracer = new(scene, new LightingState());
        CameraBasis basis = camera.ToBasis();
        uint[] pixels = new uint[checked(width * height)];
        int sampleCount = options.Samples;
        double sampleWeight = 1.0 / sampleCount;
        ParallelOptions parallelOptions = new() { CancellationToken = cancellationToken };

        Parallel.For(0, height, parallelOptions, y =>
        {
            for (int x = 0; x < width; x++)
            {
                Vec3 accumulated = Vec3.Zero;
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    double jitterX = sampleCount == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 0);
                    double jitterY = sampleCount == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 1);
                    Vec3 direction = RayTracer.RayDirection(
                        x + jitterX,
                        y + jitterY,
                        width,
                        height,
                        basis,
                        options.FieldOfViewDegrees);

                    Ray ray = new(camera.Position, direction);
                    Vec3 sample = options.Bounces <= 0
                        ? tracer.Trace(ray, RenderMode.Lit)
                        : tracer.TracePath(ray, options.Bounces, x, y, sampleIndex);
                    accumulated += sample;
                }

                Vec3 linear = accumulated * sampleWeight;
                Vec3 display = RayTracer.ToDisplayColor(linear, options.Exposure);
                pixels[y * width + x] = PackDisplayColor(display);
            }
        });

        details =
            $"CPU ray/path tracer - {width}x{height}, samples={options.Samples}, " +
            $"bounces={options.Bounces}, fov={options.FieldOfViewDegrees:0.##}°, exposure={options.Exposure:0.###}";
        return new RenderImage(width, height, pixels);
    }

    // PackDisplayColor packs display color into the compact binary/pixel representation expected by the downstream
    // renderer or file format, including clamping/bit placement rather than exposing raw doubles.
    private static uint PackDisplayColor(Vec3 display)
    {
        byte red = ToByte(display.X);
        byte green = ToByte(display.Y);
        byte blue = ToByte(display.Z);
        return red | ((uint)green << 8) | ((uint)blue << 16) | 0xff000000u;
    }

    // ToByte converts a normalized numeric channel to an 8-bit channel after clamping/rounding, preventing negative
    // or over-range values from wrapping when packed into a pixel/color.
    private static byte ToByte(double value)
    {
        if (!double.IsFinite(value)) return 0;
        return (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0), 0, 255);
    }

    private static double Jitter01(int x, int y, int sampleIndex, int axis)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)x) * 16777619u;
            hash = (hash ^ (uint)y) * 16777619u;
            hash = (hash ^ (uint)sampleIndex) * 16777619u;
            hash = (hash ^ (uint)(axis * 374761393)) * 16777619u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffu) / 16777216.0;
        }
    }
}
