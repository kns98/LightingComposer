// File: Rendering/Raster/RangeDrawDispatcher.cs
// Purpose: Draws a vertex buffer, switching identity/preview sets per range.

using Veldrid;

namespace LightingShowcase.Rendering;

internal static class RangeDrawDispatcher
{
    /// <summary>Draws a vertex buffer, switching between identity and preview resource sets per range.</summary>
    public static void Draw(
        CommandList commandList, uint totalVertexCount, IReadOnlyList<VertexRange>? previewRanges,
        ResourceSet identitySet, ResourceSet previewSet)
    {
        if (previewRanges == null || previewRanges.Count == 0)
        {
            commandList.SetGraphicsResourceSet(0, identitySet);
            commandList.Draw(totalVertexCount);
            return;
        }

        uint cursor = 0;
        foreach (VertexRange range in previewRanges)
        {
            if (range.Start > cursor)
            {
                commandList.SetGraphicsResourceSet(0, identitySet);
                commandList.Draw(range.Start - cursor, 1, cursor, 0);
            }

            if (range.Count > 0)
            {
                commandList.SetGraphicsResourceSet(0, previewSet);
                commandList.Draw(range.Count, 1, range.Start, 0);
            }
            cursor = Math.Max(cursor, range.Start + range.Count);
        }

        if (cursor < totalVertexCount)
        {
            commandList.SetGraphicsResourceSet(0, identitySet);
            commandList.Draw(totalVertexCount - cursor, 1, cursor, 0);
        }
    }
}
