// -----------------------------------------------------------------------------
// File: Rendering/GifLzwCompressor.cs
// Purpose: GIF-variant LZW compression for indexed pixel data.
//
// Packs palette indices with clear/end codes, LSB-first code packing, code
// widths growing to 12 bits, and a mid-stream clear when the table fills.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Rendering;

internal static class GifLzwCompressor
{
    private const int MaxBits = 12;
    private const int MaxTableSize = 4096;

    /// <summary>Compresses palette indices with GIF LZW at the given minimum code size.</summary>
    public static byte[] Compress(byte[] indices, int minimumCodeSize)
    {
        int clear = 1 << minimumCodeSize;
        int endOfInformation = clear + 1;
        int codeSize = minimumCodeSize + 1;
        int nextCode = endOfInformation + 1;

        Dictionary<int, int> table = new(MaxTableSize);
        List<byte> output = new(indices.Length);
        int accumulator = 0;
        int accumulatorBits = 0;

        Emit(clear);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int key = (prefix << 8) | indices[i];
            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            Emit(prefix);
            if (nextCode < MaxTableSize)
            {
                table[key] = nextCode++;
                // Grow only once an assigned code no longer fits the current
                // width. Growing when the table merely fills desynchronizes
                // spec decoders, which widen one code later.
                if (nextCode > (1 << codeSize) && codeSize < MaxBits)
                    codeSize++;
            }
            else
            {
                Emit(clear);
                table.Clear();
                codeSize = minimumCodeSize + 1;
                nextCode = endOfInformation + 1;
            }
            prefix = indices[i];
        }
        Emit(prefix);
        Emit(endOfInformation);

        if (accumulatorBits > 0)
            output.Add((byte)accumulator);
        return output.ToArray();

        void Emit(int code)
        {
            accumulator |= code << accumulatorBits;
            accumulatorBits += codeSize;
            while (accumulatorBits >= 8)
            {
                output.Add((byte)accumulator);
                accumulator >>= 8;
                accumulatorBits -= 8;
            }
        }
    }
}
