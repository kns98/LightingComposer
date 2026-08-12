/*
 * This is desktop-editor glue around the scene and rendering layers. The code should be read in terms of how it
 * translates user interaction into domain operations while keeping platform UI state, mutable scene state, and
 * renderer state from becoming entangled.
 */
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

/// <summary>
/// Renderer/view-mode settings used by Composer.
///
/// The dialog exposes the useful command-line render controls, but each view
/// mode only enables the controls its current renderer actually consumes.
/// </summary>
internal sealed record ComposerRenderOptions(
    int Width,
    int Height,
    int Samples,
    int Bounces,
    double FieldOfViewDegrees,
    double Exposure,
    double AmbientStrength,
    bool UseShadows,
    Vec3 BackgroundTop,
    Vec3 BackgroundBottom)
{
    public static readonly Vec3 DefaultBackgroundTop = new(0.055, 0.060, 0.072);
    public static readonly Vec3 DefaultBackgroundBottom = new(0.010, 0.012, 0.016);

    public static ComposerRenderOptions DefaultsFor(ComposerRendererKind kind) => kind switch
    {
        ComposerRendererKind.Raster => new(
            Width: 1280,
            Height: 720,
            Samples: 1,
            Bounces: 0,
            FieldOfViewDegrees: 72.0,
            Exposure: 1.0,
            AmbientStrength: 1.0,
            UseShadows: true,
            BackgroundTop: DefaultBackgroundTop,
            BackgroundBottom: DefaultBackgroundBottom),

        ComposerRendererKind.VulkanRaster => new(
            Width: 1280,
            Height: 720,
            Samples: 1,
            Bounces: 0,
            FieldOfViewDegrees: 72.0,
            Exposure: 1.0,
            AmbientStrength: 1.0,
            UseShadows: true,
            BackgroundTop: DefaultBackgroundTop,
            BackgroundBottom: DefaultBackgroundBottom),

        ComposerRendererKind.VulkanCompute => new(
            Width: 960,
            Height: 540,
            Samples: 1,
            Bounces: 0,
            FieldOfViewDegrees: 72.0,
            Exposure: 1.0,
            AmbientStrength: 1.0,
            UseShadows: true,
            BackgroundTop: DefaultBackgroundTop,
            BackgroundBottom: DefaultBackgroundBottom),

        ComposerRendererKind.Cpu => new(
            Width: 640,
            Height: 360,
            Samples: 1,
            Bounces: 1,
            FieldOfViewDegrees: 72.0,
            Exposure: 1.0,
            AmbientStrength: 1.0,
            UseShadows: true,
            BackgroundTop: DefaultBackgroundTop,
            BackgroundBottom: DefaultBackgroundBottom),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown renderer.")
    };

    public static ComposerRenderOptions CommandLineDefaultsFor(ComposerRendererKind kind) =>
        DefaultsFor(kind) with
        {
            Width = 1920,
            Height = 1080,
            Samples = 1,
            Bounces = 2,
            FieldOfViewDegrees = 72.0,
            Exposure = 1.0,
            AmbientStrength = 1.0,
            UseShadows = true,
            BackgroundTop = DefaultBackgroundTop,
            BackgroundBottom = DefaultBackgroundBottom
        };

    public static bool SupportsSamples(ComposerRendererKind kind) =>
        kind is ComposerRendererKind.VulkanCompute or ComposerRendererKind.Cpu;

    public static bool SupportsBounces(ComposerRendererKind kind) =>
        kind is ComposerRendererKind.VulkanCompute or ComposerRendererKind.Cpu;

    public static bool SupportsFieldOfView(ComposerRendererKind kind) =>
        kind is ComposerRendererKind.VulkanCompute or ComposerRendererKind.Cpu;

    public static bool SupportsExposure(ComposerRendererKind kind) =>
        kind is ComposerRendererKind.VulkanCompute or ComposerRendererKind.Cpu;

    public static bool SupportsAmbient(ComposerRendererKind kind) =>
        kind == ComposerRendererKind.VulkanCompute;

    public static bool SupportsShadows(ComposerRendererKind kind) =>
        kind == ComposerRendererKind.VulkanCompute;

    public static bool SupportsBackground(ComposerRendererKind kind) =>
        kind == ComposerRendererKind.VulkanCompute;

    public void Validate()
    {
        if (Width is < 1 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(Width), "Width must be between 1 and 32768 pixels.");
        if (Height is < 1 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(Height), "Height must be between 1 and 32768 pixels.");
        if ((long)Width * Height * 4L > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Width), "The RGBA output buffer must be smaller than 4 GiB.");
        if (Samples is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(Samples), "Samples must be between 1 and 4096.");
        if (Bounces is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(Bounces), "Bounces must be between 0 and 8.");
        if (!double.IsFinite(FieldOfViewDegrees) || FieldOfViewDegrees is < 1.0 or > 179.0)
            throw new ArgumentOutOfRangeException(nameof(FieldOfViewDegrees), "Field of view must be between 1 and 179 degrees.");
        if (!double.IsFinite(Exposure) || Exposure is < 0.01 or > 100.0)
            throw new ArgumentOutOfRangeException(nameof(Exposure), "Exposure must be between 0.01 and 100.");
        if (!double.IsFinite(AmbientStrength) || AmbientStrength is < 0.0 or > 100.0)
            throw new ArgumentOutOfRangeException(nameof(AmbientStrength), "Ambient strength must be between 0 and 100.");
        ValidateColor(BackgroundTop, nameof(BackgroundTop));
        ValidateColor(BackgroundBottom, nameof(BackgroundBottom));
    }

    public string Describe(ComposerRendererKind kind)
    {
        List<string> parts = [$"{Width}x{Height}"];
        if (SupportsSamples(kind)) parts.Add($"samples={Samples}");
        if (SupportsBounces(kind)) parts.Add($"bounces={Bounces}");
        if (SupportsFieldOfView(kind)) parts.Add($"fov={FieldOfViewDegrees:0.##}°");
        if (SupportsExposure(kind)) parts.Add($"exposure={Exposure:0.###}");
        if (SupportsAmbient(kind)) parts.Add($"ambient={AmbientStrength:0.###}");
        if (SupportsShadows(kind)) parts.Add($"shadows={(UseShadows ? "on" : "off")}");
        return string.Join(", ", parts);
    }

    // ValidateColor checks the invariants required for color and throws/reports an error for non-finite,
    // out-of-range, or otherwise unsupported values. Keeping validation next to mutation prevents invalid state
    // from propagating into renderers.
    private static void ValidateColor(Vec3 value, string name)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z) ||
            value.X < 0.0 || value.Y < 0.0 || value.Z < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Background colors must contain finite non-negative values.");
        }
    }
}
