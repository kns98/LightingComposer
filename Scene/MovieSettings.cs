// -----------------------------------------------------------------------------
// File: Scene/MovieSettings.cs
// Purpose: Output settings for rendering the studio timeline to a movie.
// -----------------------------------------------------------------------------
namespace LightingShowcase.SceneGraph;

/// <summary>Frame rate, size, backend, and output name for movie rendering.</summary>
public sealed class MovieSettings
{
    public int Fps { get; set; } = 12;
    public int Width { get; set; } = 480;
    public int Height { get; set; } = 360;
    public RenderBackend Backend { get; set; } = RenderBackend.ShadowRasterPreview;
    public int Samples { get; set; } = 1;
    public int Bounces { get; set; } = 2;
    public double Exposure { get; set; } = 1.0;
    public string OutputName { get; set; } = "movie";

    /// <summary>Returns a copy of these settings.</summary>
    public MovieSettings Clone() => new()
    {
        Fps = Fps,
        Width = Width,
        Height = Height,
        Backend = Backend,
        Samples = Samples,
        Bounces = Bounces,
        Exposure = Exposure,
        OutputName = OutputName
    };

    /// <summary>Throws when any setting falls outside its valid range.</summary>
    public void Validate()
    {
        if (Fps is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(Fps), "Fps must be between 1 and 50.");
        if (Width is < 1 or > 32768) throw new ArgumentOutOfRangeException(nameof(Width), "Width must be between 1 and 32768.");
        if (Height is < 1 or > 32768) throw new ArgumentOutOfRangeException(nameof(Height), "Height must be between 1 and 32768.");
        if ((long)Width * Height * 4L > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Width), "The RGBA output buffer must be smaller than 4 GiB.");
        if (!Enum.IsDefined(Backend) || Backend == RenderBackend.VulkanDiagnostic)
            throw new ArgumentOutOfRangeException(nameof(Backend), "Movie backend must be a renderable backend.");
        if (Samples is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(Samples), "Samples must be between 1 and 4096.");
        if (Bounces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(Bounces), "Bounces must be between 0 and 8.");
        if (Exposure is < 0.01 or > 100.0) throw new ArgumentOutOfRangeException(nameof(Exposure), "Exposure must be between 0.01 and 100.");
        if (string.IsNullOrWhiteSpace(OutputName)) throw new ArgumentException("An output name is required.", nameof(OutputName));
        if (OutputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The output name contains characters invalid in file names.", nameof(OutputName));
    }
}
