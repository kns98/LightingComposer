// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CpuComposerRenderer.cs
// Purpose: CPU ray-traced preview rendering.
//
// Renders the session scene with the parallel CPU ray tracer while honoring samples, cancellation, and render options.
// -----------------------------------------------------------------------------

// Import camera definitions and basis helpers
using LightingShowcase.CameraSystem;
// Import lighting state types
using LightingShowcase.Lighting;
// Import vector and ray math types
using LightingShowcase.Math3D;
// Import ray tracer and render types
using LightingShowcase.Rendering;
// Import scene graph types
using LightingShowcase.SceneGraph;

// Declare composer preview renderer namespace
namespace LightingShowcase.Composer;

// CPU parallel ray-traced preview renderer
internal static class CpuComposerRenderer
// Begin CpuComposerRenderer class
{
    /// <summary>Renders the scene on the CPU with the parallel ray tracer.</summary>
    // Render scene on CPU ray tracer
    public static RenderImage Render(
        // Scene to render parameter
        Scene scene,
        // Viewpoint camera definition parameter
        CameraDefinition camera,
        // Output image width parameter
        int width,
        // Output image height parameter
        int height,
        // Samples bounces exposure options parameter
        ComposerRenderOptions options,
        // Cancellation token parameter
        CancellationToken cancellationToken,
        // Render description output parameter
        out string details)
    // Begin Render method
    {
        // Require non-null scene
        ArgumentNullException.ThrowIfNull(scene);
        // Require non-null camera
        ArgumentNullException.ThrowIfNull(camera);
        // Require non-null render options
        ArgumentNullException.ThrowIfNull(options);
        // Validate render option ranges
        options.Validate();

        // Create ray tracer with fresh lighting
        RayTracer tracer = new(scene, new LightingState());
        // Apply configured background gradient
        tracer.BackgroundTop = options.BackgroundTop;
        tracer.BackgroundBottom = options.BackgroundBottom;
        // Build camera basis vectors
        CameraBasis basis = camera.ToBasis();
        // Precompute world units per pixel for wireframe width
        tracer.WireframePixelWorld = 2.0 * Math.Tan(options.FieldOfViewDegrees * Math.PI / 360.0) / Math.Max(1, height);
        // Allocate output pixel buffer
        uint[] pixels = new uint[checked(width * height)];
        // Read samples per pixel count
        int sampleCount = options.Samples;
        // Detect wireframe single-sample view
        bool wireframe = options.Wireframe;
        // Collapse samples for wireframe view
        int effectiveSamples = wireframe ? 1 : sampleCount;
        // Compute per-sample averaging weight
        double sampleWeight = 1.0 / effectiveSamples;
        // Configure parallel loop cancellation
        ParallelOptions parallelOptions = new() { CancellationToken = cancellationToken };

        // Render each scanline in parallel
        Parallel.For(0, height, parallelOptions, y =>
        // Begin parallel scanline lambda
        {
            // Loop pixels across scanline
            for (int x = 0; x < width; x++)
            // Begin per-pixel loop
            {
                // Reset per-pixel color accumulator
                Vec3 accumulated = Vec3.Zero;
                // Loop anti-aliasing samples
                for (int sampleIndex = 0; sampleIndex < effectiveSamples; sampleIndex++)
                // Begin per-sample loop
                {
                    // Honor render cancellation promptly
                    cancellationToken.ThrowIfCancellationRequested();

                    // Compute horizontal subpixel jitter offset
                    double jitterX = effectiveSamples == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 0);
                    // Compute vertical subpixel jitter offset
                    double jitterY = effectiveSamples == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 1);
                    // Compute primary ray direction
                    Vec3 direction = RayTracer.RayDirection(
                        // Jittered pixel horizontal coordinate
                        x + jitterX,
                        // Jittered pixel vertical coordinate
                        y + jitterY,
                        // Image width normalization argument
                        width,
                        // Image height normalization argument
                        height,
                        // Camera basis argument
                        basis,
                        // Field of view argument
                        options.FieldOfViewDegrees);

                    // Build primary ray from camera
                    Ray ray = new(camera.Position, direction);
                    // Choose wireframe direct or path tracing
                    Vec3 sample = wireframe
                        // Trace wireframe hidden-line ray
                        ? tracer.Trace(ray, RenderMode.Wireframe)
                        // Fall back to shaded tracing
                        : options.Bounces <= 0
                            // Trace single direct-lighting ray
                            ? tracer.Trace(ray, RenderMode.Lit)
                            // Trace multi-bounce path sample
                            : tracer.TracePath(ray, options.Bounces, x, y, sampleIndex);
                    // Accumulate sample into pixel color
                    accumulated += sample;
                // End per-sample loop
                }

                // Average samples into linear color
                Vec3 linear = accumulated * sampleWeight;
                // Apply exposure display mapping
                Vec3 display = RayTracer.ToDisplayColor(linear, options.Exposure);
                // Store packed pixel color
                pixels[y * width + x] = PackDisplayColor(display);
            // End per-pixel loop
            }
        // End parallel scanline loop
        });

        // Build render description string
        details =
            // Format resolution and sample count
            $"CPU ray/path tracer - {width}x{height}, samples={options.Samples}, " +
            // Format bounces field of view exposure
            $"bounces={options.Bounces}, fov={options.FieldOfViewDegrees:0.##}°, exposure={options.Exposure:0.###}";
        // Tag wireframe renders in details
        if (wireframe) details += ", wireframe";
        // Return finished render image
        return new RenderImage(width, height, pixels);
    // End Render method
    }

    /// <summary>Packs a display color into a 32-bit RGBA value.</summary>
    // Pack display color into uint
    private static uint PackDisplayColor(Vec3 display)
    // Begin PackDisplayColor method
    {
        // Convert red channel to byte
        byte red = ToByte(display.X);
        // Convert green channel to byte
        byte green = ToByte(display.Y);
        // Convert blue channel to byte
        byte blue = ToByte(display.Z);
        // Pack RGBA bytes with opaque alpha
        return red | ((uint)green << 8) | ((uint)blue << 16) | 0xff000000u;
    // End PackDisplayColor method
    }

    /// <summary>Converts a color channel into a byte.</summary>
    // Convert channel value to byte
    private static byte ToByte(double value)
    // Begin ToByte method
    {
        // Map non-finite channels to black
        if (!double.IsFinite(value)) return 0;
        // Clamp scale and round channel
        return (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0), 0, 255);
    // End ToByte method
    }

    /// <summary>Computes a deterministic sub-pixel jitter value in the unit interval.</summary>
    // Compute deterministic unit jitter value
    private static double Jitter01(int x, int y, int sampleIndex, int axis)
    // Begin Jitter01 method
    {
        // Allow hash arithmetic overflow
        unchecked
        // Begin unchecked hash block
        {
            // Seed FNV hash offset basis
            uint hash = 2166136261u;
            // Fold pixel x into hash
            hash = (hash ^ (uint)x) * 16777619u;
            // Fold pixel y into hash
            hash = (hash ^ (uint)y) * 16777619u;
            // Fold sample index into hash
            hash = (hash ^ (uint)sampleIndex) * 16777619u;
            // Fold jitter axis into hash
            hash = (hash ^ (uint)(axis * 374761393)) * 16777619u;
            // Start final hash avalanche mix
            hash ^= hash >> 13;
            // Multiply hash avalanche constant
            hash *= 1274126177u;
            // Finish final hash avalanche mix
            hash ^= hash >> 16;
            // Normalize hash to unit interval
            return (hash & 0x00ffffffu) / 16777216.0;
        // End unchecked hash block
        }
    // End Jitter01 method
    }
// End CpuComposerRenderer class
}
