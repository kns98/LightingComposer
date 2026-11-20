// File: Rendering/Raster/AtlasDimensionPlanner.cs
// Purpose: Candidate atlas page dimensions from content size and device limits.

namespace LightingShowcase.Rendering;

internal static class AtlasDimensionPlanner
{
    /// <summary>Builds candidate atlas dimensions from minimum, square estimate, and power-of-two sizes.</summary>
    public static List<int> CandidateDimensions(int minimum, int maximum, long area)
    {
        SortedSet<int> candidates = new();
        Add(minimum);
        Add(NextPowerOfTwo(minimum));

        int squareEstimate = checked((int)Math.Min(int.MaxValue, Math.Ceiling(Math.Sqrt(Math.Max(1L, area)))));
        Add(squareEstimate);
        Add(NextPowerOfTwo(squareEstimate));

        long power = NextPowerOfTwo(minimum);
        while (power > 0 && power <= maximum)
        {
            Add((int)power);
            if (power > int.MaxValue / 2)
                break;
            power *= 2;
        }

        // The physical device limit is included as a last-resort candidate;
        // the score still chooses the smallest total allocation that fits.
        Add(maximum);
        return candidates.ToList();

        void Add(int value) => candidates.Add(Math.Clamp(value, minimum, maximum));
    }

    /// <summary>Returns the smallest power of two greater than or equal to the value.</summary>
    public static int NextPowerOfTwo(int value)
    {
        if (value <= 1)
            return 1;
        uint v = checked((uint)value - 1u);
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v >= 0x7fffffffu ? int.MaxValue : checked((int)(v + 1u));
    }
}
