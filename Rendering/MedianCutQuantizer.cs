// -----------------------------------------------------------------------------
// File: Rendering/MedianCutQuantizer.cs
// Purpose: Median-cut palette quantization shared across animation frames.
//
// Builds one 256-color palette from sampled pixels so colors stay stable,
// then maps every pixel through an exact-match cache backed by a nearest
// palette search. Sampling keeps multi-megapixel animations fast.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Rendering;

internal sealed class MedianCutQuantizer
{
    private const int SampleStride = 16;

    private readonly byte[] red;
    private readonly byte[] green;
    private readonly byte[] blue;
    private readonly int[] sample;
    private readonly byte[] boxOfSample;

    private MedianCutQuantizer(byte[] red, byte[] green, byte[] blue)
    {
        this.red = red;
        this.green = green;
        this.blue = blue;
        int sampleCount = (red.Length + SampleStride - 1) / SampleStride;
        sample = new int[sampleCount];
        for (int i = 0; i < sampleCount; i++)
            sample[i] = i * SampleStride;
        boxOfSample = new byte[red.Length];
    }

    /// <summary>Quantizes RGB pixels into a 256-color palette plus one index per pixel.</summary>
    public static (byte[] Palette, byte[] Indices) Quantize(byte[] red, byte[] green, byte[] blue)
    {
        MedianCutQuantizer quantizer = new(red, green, blue);
        List<(int Start, int Count)> boxes = new() { (0, quantizer.sample.Length) };

        while (boxes.Count < 256)
        {
            int widest = -1;
            int widestRange = 0;
            int widestChannel = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                quantizer.ChannelRange(boxes[i], out int channel, out int range);
                if (range > widestRange)
                {
                    widest = i;
                    widestRange = range;
                    widestChannel = channel;
                }
            }
            if (widest < 0)
                break;

            (int start, int count) = boxes[widest];
            quantizer.SortByChannel(start, count, widestChannel);
            int half = count / 2;
            boxes[widest] = (start, half);
            boxes.Add((start + half, count - half));
            for (int i = start + half; i < start + count; i++)
                quantizer.boxOfSample[quantizer.sample[i]] = (byte)(boxes.Count - 1);
        }

        byte[] palette = new byte[256 * 3];
        for (int i = 0; i < boxes.Count; i++)
        {
            (int start, int count) = boxes[i];
            long r = 0;
            long g = 0;
            long b = 0;
            for (int j = start; j < start + count; j++)
            {
                int pixel = quantizer.sample[j];
                r += quantizer.red[pixel];
                g += quantizer.green[pixel];
                b += quantizer.blue[pixel];
            }
            palette[i * 3] = (byte)(r / count);
            palette[i * 3 + 1] = (byte)(g / count);
            palette[i * 3 + 2] = (byte)(b / count);
        }
        return (palette, quantizer.MapAll(palette));
    }

    private byte[] MapAll(byte[] palette)
    {
        Dictionary<int, byte> cache = new(4096);
        for (int i = 0; i < sample.Length; i++)
        {
            int pixel = sample[i];
            cache[(red[pixel] << 16) | (green[pixel] << 8) | blue[pixel]] = boxOfSample[pixel];
        }

        byte[] indices = new byte[red.Length];
        for (int pixel = 0; pixel < red.Length; pixel++)
        {
            int key = (red[pixel] << 16) | (green[pixel] << 8) | blue[pixel];
            if (!cache.TryGetValue(key, out byte index))
            {
                index = Nearest(palette, red[pixel], green[pixel], blue[pixel]);
                cache[key] = index;
            }
            indices[pixel] = index;
        }
        return indices;
    }

    private static byte Nearest(byte[] palette, int r, int g, int b)
    {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < 256; i++)
        {
            int dr = palette[i * 3] - r;
            int dg = palette[i * 3 + 1] - g;
            int db = palette[i * 3 + 2] - b;
            int distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return (byte)best;
    }

    private void ChannelRange((int Start, int Count) box, out int channel, out int range)
    {
        int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
        for (int i = box.Start; i < box.Start + box.Count; i++)
        {
            int pixel = sample[i];
            minR = Math.Min(minR, red[pixel]);
            maxR = Math.Max(maxR, red[pixel]);
            minG = Math.Min(minG, green[pixel]);
            maxG = Math.Max(maxG, green[pixel]);
            minB = Math.Min(minB, blue[pixel]);
            maxB = Math.Max(maxB, blue[pixel]);
        }
        int rangeR = maxR - minR;
        int rangeG = maxG - minG;
        int rangeB = maxB - minB;
        channel = rangeR >= rangeG && rangeR >= rangeB ? 0 : rangeG >= rangeB ? 1 : 2;
        range = Math.Max(rangeR, Math.Max(rangeG, rangeB));
    }

    private void SortByChannel(int start, int count, int channel)
    {
        Array.Sort(sample, start, count, Comparer<int>.Create((left, right) =>
            channel == 0 ? red[left].CompareTo(red[right]) :
            channel == 1 ? green[left].CompareTo(green[right]) :
            blue[left].CompareTo(blue[right])));
    }
}
