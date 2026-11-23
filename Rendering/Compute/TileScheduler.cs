// File: Rendering/Compute/TileScheduler.cs
// Purpose: Tile sizing policy for bounded compute dispatches.

namespace LightingShowcase.Rendering;

internal static class TileScheduler
{
    /// <summary>Chooses the row height for compute tile dispatches.</summary>
    public static int ChooseTileRows(int width, int height, int triangleCount, int bounceCount, int lightCount, int sampleCount)
    {
        // The shader traverses a BVH, so tiles can be larger than with the old
        // brute-force shader. Sample batching multiplies the work per dispatch,
        // so include it to keep each dispatch bounded on watchdog drivers.
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + Math.Min(Math.Max(0L, lightCount), 4L);
        long nodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        long workPerRow = Math.Max(1L, width) * nodeVisits * raysPerPixel * Math.Max(1L, sampleCount);

        const long targetWorkPerDispatch = 64_000_000L;
        long computedRows = Math.Max(1L, targetWorkPerDispatch / Math.Max(1L, workPerRow));

        long maxRows = 128L;
        if (bounceCount >= 6 || sampleCount >= 4)
            maxRows = 32L;
        if (bounceCount >= 6 && sampleCount >= 4)
            maxRows = 16L;

        return Math.Min(Math.Max(1, (int)Math.Clamp(computedRows, 1L, maxRows)), Math.Max(1, height));
    }

    /// <summary>Chooses how many tiles to submit per command list.</summary>
    public static int ChooseTilesPerSubmit(int tileRows, int bounceCount, int sampleCount, int triangleCount, int width, int lightCount)
    {
        // tileRows bounds one dispatch, not a command-list submission holding
        // several dispatches. Bound the tiles per submit as well.
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + Math.Min(Math.Max(0L, lightCount), 4L);
        long nodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        long workPerTile = Math.Max(1L, width) * Math.Max(1L, tileRows) * nodeVisits * raysPerPixel * Math.Max(1L, sampleCount);

        const long targetWorkPerSubmit = 96_000_000L;
        int computed = (int)Math.Clamp(targetWorkPerSubmit / Math.Max(1L, workPerTile), 1L, 8L);

        if (bounceCount >= 6)
            computed = Math.Min(computed, 1);
        else if (sampleCount > 1 || bounceCount >= 3)
            computed = Math.Min(computed, 2);

        return Math.Max(1, computed);
    }
}
