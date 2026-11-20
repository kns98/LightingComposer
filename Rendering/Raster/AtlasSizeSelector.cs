// File: Rendering/Raster/AtlasSizeSelector.cs
// Purpose: Chooses the smallest atlas page size that packs all textures.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed record AtlasSizeSelection(List<RasterTextureUpload> Uploads, int Width, int Height, int PageCount, long AllocatedPixels);

internal sealed class AtlasSizeSelector
{
    private const int Padding = 1;

    private readonly IReadOnlyList<TextureMap> sorted;
    private readonly int maxPageWidth;
    private readonly int maxPageHeight;
    private readonly int maxPageCount;

    public AtlasSizeSelector(IReadOnlyList<TextureMap> sorted, int maxPageWidth, int maxPageHeight, int maxPageCount)
    {
        this.sorted = sorted;
        this.maxPageWidth = maxPageWidth;
        this.maxPageHeight = maxPageHeight;
        this.maxPageCount = maxPageCount;
    }

    /// <summary>Measures entries and returns the smallest packing that fits the device limits.</summary>
    public AtlasSizeSelection Select()
    {
        long totalArea = 0;
        int largestEntryWidth = 1;
        int largestEntryHeight = 1;
        foreach (TextureMap texture in sorted)
        {
            int entryWidth = checked(Math.Max(1, texture.Width) + Padding * 2);
            int entryHeight = checked(Math.Max(1, texture.Height) + Padding * 2);
            if (entryWidth > maxPageWidth || entryHeight > maxPageHeight)
            {
                throw new InvalidOperationException(
                    $"Texture '{texture.Name}' is {texture.Width}x{texture.Height}; with filtering borders it exceeds " +
                    $"this Vulkan device's {maxPageWidth}x{maxPageHeight} sampled-texture limit.");
            }

            totalArea = checked(totalArea + checked((long)entryWidth * entryHeight));
            largestEntryWidth = Math.Max(largestEntryWidth, entryWidth);
            largestEntryHeight = Math.Max(largestEntryHeight, entryHeight);
        }

        List<int> candidateWidths = AtlasDimensionPlanner.CandidateDimensions(largestEntryWidth, maxPageWidth, totalArea);
        List<int> candidateHeights = AtlasDimensionPlanner.CandidateDimensions(largestEntryHeight, maxPageHeight, totalArea);

        List<RasterTextureUpload>? bestUploads = null;
        int bestWidth = 0;
        int bestHeight = 0;
        int bestPageCount = 0;
        long bestAllocatedPixels = long.MaxValue;
        long bestLargestPage = long.MaxValue;
        AtlasRowPacker packer = new(sorted, maxPageCount);

        foreach (int candidateWidth in candidateWidths)
        {
            foreach (int candidateHeight in candidateHeights)
            {
                if (!packer.TryPack(candidateWidth, candidateHeight, out List<RasterTextureUpload> uploads, out int pageCount))
                    continue;

                long pagePixels = checked((long)candidateWidth * candidateHeight);
                long allocatedPixels = checked(pagePixels * pageCount);
                if (allocatedPixels > bestAllocatedPixels ||
                    (allocatedPixels == bestAllocatedPixels && pagePixels >= bestLargestPage))
                    continue;

                bestAllocatedPixels = allocatedPixels;
                bestLargestPage = pagePixels;
                bestWidth = candidateWidth;
                bestHeight = candidateHeight;
                bestPageCount = pageCount;
                bestUploads = uploads;
            }
        }

        if (bestUploads == null)
        {
            throw new InvalidOperationException(
                $"The scene's {sorted.Count} textures cannot be packed into the Vulkan device's " +
                $"{maxPageWidth}x{maxPageHeight}, {maxPageCount}-layer sampled-texture array limits.");
        }

        return new AtlasSizeSelection(bestUploads, bestWidth, bestHeight, bestPageCount, bestAllocatedPixels);
    }
}
