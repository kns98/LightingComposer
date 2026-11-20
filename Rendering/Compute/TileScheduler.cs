// File: Rendering/Compute/TileScheduler.cs
// Purpose: Tile sizing policy for bounded compute dispatches.

namespace LightingShowcase.Rendering;

internal static class TileScheduler
{
    /// <summary>Chooses the row height for compute tile dispatches.</summary>
    public static int ChooseTileRows(int width, int height, int triangleCount, int bounceCount, int lightCount, int sampleCount)
    {
        // The GPU shader traverses a BVH, so it can use larger tiles than the
        // original brute-force shader. However, sample batching multiplies the
        // work inside every dispatch. Include the sample count in the scheduler
        // so each dispatch stays bounded on watchdog-sensitive drivers.
        long directShadowRays = Math.Min(Math.Max(0L, lightCount), 4L);
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + directShadowRays;
        long approximateNodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        long samples = Math.Max(1L, sampleCount);
        long workPerRow = Math.Max(1L, width) * approximateNodeVisits * raysPerPixel * samples;

        // Target a modest per-dispatch workload. Command-list batching still
        // removes most WaitForIdle overhead, so many small dispatches are safer
        // than one watchdog-prone giant dispatch.
        const long targetWorkPerDispatch = 64_000_000L;
        long computedRows = Math.Max(1L, targetWorkPerDispatch / Math.Max(1L, workPerRow));

        long maxRows = 128L;
        if (bounceCount >= 6 || samples >= 4)
            maxRows = 32L;
        if (bounceCount >= 6 && samples >= 4)
            maxRows = 16L;

        int rows = (int)Math.Clamp(computedRows, 1L, maxRows);
        return Math.Min(Math.Max(1, rows), Math.Max(1, height));
    }

    /// <summary>Chooses how many tiles to submit per command list.</summary>
    public static int ChooseTilesPerSubmit(int tileRows, int bounceCount, int sampleCount, int triangleCount, int width, int lightCount)
    {
        // tileRows limits the size of one dispatch. It does not limit the size
        // of a command-list submission when every tile is recorded into a single
        // command list. Bound the number of tile dispatches per submit as well.
        long directShadowRays = Math.Min(Math.Max(0L, lightCount), 4L);
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + directShadowRays;
        long approximateNodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        long workPerTile = Math.Max(1L, width) * Math.Max(1L, tileRows) * approximateNodeVisits * raysPerPixel * Math.Max(1L, sampleCount);

        const long targetWorkPerSubmit = 96_000_000L;
        int computed = (int)Math.Clamp(targetWorkPerSubmit / Math.Max(1L, workPerTile), 1L, 8L);

        if (bounceCount >= 6)
            computed = Math.Min(computed, 1);
        else if (sampleCount > 1 || bounceCount >= 3)
            computed = Math.Min(computed, 2);

        return Math.Max(1, computed);
    }
}
