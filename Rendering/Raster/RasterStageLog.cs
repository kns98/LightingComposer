// File: Rendering/Raster/RasterStageLog.cs
// Purpose: Timestamped diagnostic log for rasterizer stages and timings.

namespace LightingShowcase.Rendering;

internal sealed class RasterStageLog
{
    public static string LogPath => Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-stage-log.txt");

    private readonly RasterOptions options;

    public RasterStageLog(RasterOptions options)
    {
        this.options = options;
    }

    /// <summary>Resets the raster stage log with the current resolution when logging is enabled.</summary>
    public void Reset(int width, int height)
    {
        if (!options.StageLoggingEnabled)
            return;
        try
        {
            File.WriteAllText(LogPath,
                $"LightingShowcase Vulkan raster stage log{Environment.NewLine}" +
                $"Resolution: {width}x{height}{Environment.NewLine}" +
                $"Started: {DateTime.Now:O}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Appends a timestamped entry to the raster stage log when logging is enabled.</summary>
    public void Stage(string name)
    {
        if (!options.StageLoggingEnabled)
            return;
        try { File.AppendAllText(LogPath, $"{DateTime.Now:O} - {name}{Environment.NewLine}"); }
        catch { }
    }

    /// <summary>Logs the stage name and throws when cancellation was requested.</summary>
    public void ThrowIfCancellationRequested(CancellationToken cancellationToken, string stageName)
    {
        if (!cancellationToken.IsCancellationRequested)
            return;
        Stage("Cancellation requested before/at: " + stageName);
        throw new OperationCanceledException(cancellationToken);
    }
}
