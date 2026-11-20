// File: Rendering/Compute/ComputeStageLog.cs
// Purpose: Diagnostic log for compute stages, memory, and cancellation.

namespace LightingShowcase.Rendering;

internal sealed class ComputeStageLog
{
    public static string LogPath => Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-stage-log.txt");

    /// <summary>Resets the Vulkan stage diagnostic log.</summary>
    public void Reset(int width, int height, int bounceCount, int sampleIndex)
    {
        try
        {
            File.WriteAllText(LogPath, $"LightingShowcase Vulkan stage log{Environment.NewLine}Resolution: {width}x{height}{Environment.NewLine}Bounces: {bounceCount}{Environment.NewLine}Sample: {sampleIndex}{Environment.NewLine}Started: {DateTime.Now:O}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Appends a stage entry to the Vulkan diagnostic log.</summary>
    public void Stage(string name)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:O} - {name}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>Logs current managed memory usage to the stage log.</summary>
    public void Memory(string label)
    {
        try
        {
            long managed = GC.GetTotalMemory(forceFullCollection: false);
            Stage($"Memory {label}: managed={managed / (1024.0 * 1024.0):F1} MB");
        }
        catch { }
    }

    /// <summary>Throws when cancellation was requested at the given stage.</summary>
    public void ThrowIfCancellationRequested(CancellationToken cancellationToken, string stageName)
    {
        if (!cancellationToken.IsCancellationRequested)
            return;

        Stage($"Cancellation requested before/at: {stageName}");
        throw new OperationCanceledException(cancellationToken);
    }
}
