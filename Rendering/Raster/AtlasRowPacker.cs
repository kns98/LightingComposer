// File: Rendering/Raster/AtlasRowPacker.cs
// Purpose: Row-based texture packing into fixed-size atlas pages.

using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed class AtlasRowPacker
{
    private const int Padding = 1;

    private readonly IReadOnlyList<TextureMap> sorted;
    private readonly int maxPageCount;

    public AtlasRowPacker(IReadOnlyList<TextureMap> sorted, int maxPageCount)
    {
        this.sorted = sorted;
        this.maxPageCount = maxPageCount;
    }

    /// <summary>Packs textures into atlas pages with a row-based layout, returning false when they overflow.</summary>
    public bool TryPack(int pageWidth, int pageHeight, out List<RasterTextureUpload> uploads, out int pageCount)
    {
        uploads = new List<RasterTextureUpload>(sorted.Count);
        int page = 0;
        int cursorX = 0;
        int cursorY = 0;
        int rowHeight = 0;

        foreach (TextureMap texture in sorted)
        {
            int entryWidth = checked(Math.Max(1, texture.Width) + Padding * 2);
            int entryHeight = checked(Math.Max(1, texture.Height) + Padding * 2);

            if (cursorX > 0 && cursorX + entryWidth > pageWidth)
            {
                cursorY = checked(cursorY + rowHeight);
                cursorX = 0;
                rowHeight = 0;
            }

            if (cursorY + entryHeight > pageHeight)
            {
                page++;
                if (page >= maxPageCount)
                {
                    pageCount = 0;
                    uploads.Clear();
                    return false;
                }
                cursorX = 0;
                cursorY = 0;
                rowHeight = 0;
            }

            uploads.Add(new RasterTextureUpload
            {
                Texture = texture,
                X = cursorX + Padding,
                Y = cursorY + Padding,
                Page = page
            });
            cursorX = checked(cursorX + entryWidth);
            rowHeight = Math.Max(rowHeight, entryHeight);
        }

        pageCount = page + 1;
        return pageCount <= maxPageCount;
    }
}
