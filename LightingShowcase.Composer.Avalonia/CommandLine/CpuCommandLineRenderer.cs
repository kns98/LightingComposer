// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/CpuCommandLineRenderer.cs
// Purpose: Cross-platform CPU renderer for the CLI.
//
// CPU ray and path tracer used by the headless render command on any operating system.
// -----------------------------------------------------------------------------

// Import camera definitions and basis types
using LightingShowcase.CameraSystem;
// Import lighting state types
using LightingShowcase.Lighting;
// Import vector and ray math
using LightingShowcase.Math3D;
// Import ray tracer and render types
using LightingShowcase.Rendering;
// Import scene graph types
using LightingShowcase.SceneGraph;

// Declare command-line renderer namespace
namespace LightingShowcase.CommandLine;

/// <summary>Cross-platform CPU ray/path tracer used by the command-line application.</summary>
// Define CPU command-line renderer helper
internal static class CpuCommandLineRenderer
// Begin renderer class block
{
    /// <summary>Renders the scene with the CPU ray and path tracer using the given camera and request.</summary>
    // Declare CPU render entry point
    public static RenderImage Render(
        // Accept scene to render
        Scene scene,
        // Accept render camera definition
        CameraDefinition camera,
        // Accept render size and quality
        RenderRequest request,
        // Accept cancellation token
        CancellationToken cancellationToken,
        // Output render status message
        out string details)
    // Begin Render method body
    {
        // Reject null scene argument
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        // Reject null camera argument
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        // Reject null request argument
        if (request == null) throw new ArgumentNullException(nameof(request));

        // Create ray tracer with fresh lighting
        RayTracer tracer = new(scene, new LightingState());
        // Build camera basis vectors
        CameraBasis basis = camera.ToBasis();
        // Allocate output pixel buffer
        uint[] pixels = new uint[checked(request.Width * request.Height)];
        // Read samples per pixel
        int sampleCount = request.Samples;
        // Compute per-sample average weight
        double sampleWeight = 1.0 / sampleCount;
        // Configure cancellable parallel options
        ParallelOptions options = new() { CancellationToken = cancellationToken };

        // Render image rows in parallel
        Parallel.For(0, request.Height, options, y =>
        // Begin parallel row lambda body
        {
            // Loop across pixels in row
            for (int x = 0; x < request.Width; x++)
            // Begin per-pixel block
            {
                // Initialize accumulated pixel color
                Vec3 accumulated = Vec3.Zero;
                // Loop over pixel samples
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                // Begin per-sample block
                {
                    // Honor cancellation between samples
                    cancellationToken.ThrowIfCancellationRequested();
                    // Compute horizontal subpixel jitter
                    double jitterX = sampleCount == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 0);
                    // Compute vertical subpixel jitter
                    double jitterY = sampleCount == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 1);
                    // Compute ray direction for sample
                    Vec3 direction = RayTracer.RayDirection(
                        // Pass jittered pixel X coordinate
                        x + jitterX,
                        // Pass jittered pixel Y coordinate
                        y + jitterY,
                        // Pass image width
                        request.Width,
                        // Pass image height
                        request.Height,
                        // Pass camera basis vectors
                        basis,
                        // Pass camera field of view
                        request.FieldOfViewDegrees);
                    // Build primary ray from camera
                    Ray ray = new(camera.Position, direction);
                    // Choose direct or path tracing
                    Vec3 sample = request.Bounces <= 0
                        // Trace single direct ray
                        ? tracer.Trace(ray, RenderMode.Lit)
                        // Trace multi-bounce path sample
                        : tracer.TracePath(ray, request.Bounces, x, y, sampleIndex);
                    // Accumulate sample into pixel
                    accumulated += sample;
                // End per-sample loop
                }

                // Average and expose pixel color
                Vec3 linear = accumulated * sampleWeight * request.Exposure;
                // Pack pixel into output buffer
                pixels[y * request.Width + x] = PackDisplayColor(linear);
            // End per-pixel loop
            }
        // End parallel row loop
        });

        // Report render resolution and settings
        details = $"CPU ray/path tracer OK - {request.Width}x{request.Height}, samples={request.Samples}, bounces={request.Bounces}";
        // Return finished render image
        return new RenderImage(request.Width, request.Height, pixels);
    // End Render method
    }

    /// <summary>Packs a linear color into a display-ready packed pixel value.</summary>
    // Declare linear to display packer
    private static uint PackDisplayColor(Vec3 linear)
    // Begin PackDisplayColor body
    {
        // Convert red channel byte
        byte red = ToDisplayByte(linear.X);
        // Convert green channel byte
        byte green = ToDisplayByte(linear.Y);
        // Convert blue channel byte
        byte blue = ToDisplayByte(linear.Z);
        // Pack RGBA bytes into pixel
        return red | ((uint)green << 8) | ((uint)blue << 16) | 0xff000000u;
    // End PackDisplayColor method
    }

    /// <summary>Converts one linear channel value into a tone-mapped display byte.</summary>
    // Declare tone-mapping channel converter
    private static byte ToDisplayByte(double value)
    // Begin ToDisplayByte body
    {
        // Clamp invalid or dark values
        if (!double.IsFinite(value) || value <= 0.0) return 0;
        // Apply Reinhard tone mapping
        double mapped = value / (1.0 + value);
        // Apply gamma 2.2 correction
        double gamma = Math.Pow(Math.Clamp(mapped, 0.0, 1.0), 1.0 / 2.2);
        // Scale to 0-255 byte
        return (byte)Math.Clamp((int)Math.Round(gamma * 255.0), 0, 255);
    // End ToDisplayByte method
    }

    /// <summary>Returns a deterministic pseudo-random value in [0,1) for pixel sample jitter.</summary>
    // Declare deterministic jitter hash function
    private static double Jitter01(int x, int y, int sampleIndex, int axis)
    // Begin Jitter01 body
    {
        // Allow hash arithmetic overflow
        unchecked
        // Begin unchecked hash block
        {
            // Seed FNV-1a hash value
            uint hash = 2166136261u;
            // Mix pixel X into hash
            hash = (hash ^ (uint)x) * 16777619u;
            // Mix pixel Y into hash
            hash = (hash ^ (uint)y) * 16777619u;
            // Mix sample index into hash
            hash = (hash ^ (uint)sampleIndex) * 16777619u;
            // Mix jitter axis into hash
            hash = (hash ^ (uint)(axis * 374761393)) * 16777619u;
            // Start hash avalanche mixing
            hash ^= hash >> 13;
            // Multiply hash scramble constant
            hash *= 1274126177u;
            // Finish hash avalanche mixing
            hash ^= hash >> 16;
            // Normalize hash to unit interval
            return (hash & 0x00ffffffu) / 16777216.0;
        // End unchecked hash block
        }
    // End Jitter01 method
    }
// End renderer class block
}
