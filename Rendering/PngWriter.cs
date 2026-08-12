/*
 * The code here converts renderer-neutral scene/camera data into pixels or backend-ready state. Dimensions, cache
 * identity, data packing, and deterministic conversion are treated as part of the rendering contract so
 * interactive UI code does not need to know backend details.
 *
 * `PngWriter` provides shared algorithms/registration behavior without per-instance state.
 *
 * `CanRead` is derived rather than separately stored: it evaluates `false`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CanSeek` is derived rather than separately stored: it evaluates `false`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `CanWrite` is derived rather than separately stored: it evaluates `true`. Keeping the value computed from its
 * source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `Length` is derived rather than separately stored: it evaluates `throw new NotSupportedException()`. Keeping
 * the value computed from its source fields prevents a second cached flag/value from drifting out of sync.
 *
 * `WriteRgba` writes rgba to the external stream/document in the format’s required order, using stable
 * indices/references so another reader can reconstruct the same relationships.
 *
 * `WriteChunk` writes chunk to the external stream/document in the format’s required order, using stable
 * indices/references so another reader can reconstruct the same relationships.
 *
 * `UpdateCrc` updates crc from the newest input while preserving the identities/metadata/caches that remain valid
 * and invalidating only what the change makes stale.
 *
 * `BuildCrcTable` derives crc table from lower-level input data, resolving indexing/grouping/derived values once
 * so callers can operate on a coherent higher-level representation.
 *
 * The `ChunkedIdatStream` constructor captures `destination`, `chunkSize`. Those are the dependencies/initial
 * values the instance needs for its lifetime, so callbacks and later operations use the same
 * objects/configuration rather than looking them up globally.
 *
 * `Dispose` ends this object’s active lifetime: owned cancellations/resources/listeners are released so completed
 * windows/renderers do not keep receiving work or retain unmanaged memory.
 *
 * `SetLength` sets length through the owning abstraction instead of exposing a mutable field. That gives the
 * method one place to validate the value and perform any history/cache/UI side effects required by the change.
 */
using System.Buffers.Binary;
using System.IO.Compression;

namespace LightingShowcase.Rendering;

/// <summary>Minimal streaming PNG writer for 8-bit RGBA images.</summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, RenderImage image)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An output path is required.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);

        using FileStream output = File.Create(fullPath);
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), checked((uint)image.Height));
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // no interlace
        WriteChunk(output, "IHDR"u8, ihdr);

        using (ChunkedIdatStream idat = new(output, 1024 * 1024))
        {
            using (ZLibStream zlib = new(idat, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] row = new byte[checked(image.Width * 4 + 1)];
                for (int y = 0; y < image.Height; y++)
                {
                    row[0] = 0;
                    int basePixel = y * image.Width;
                    for (int x = 0; x < image.Width; x++)
                    {
                        uint packed = image.PackedRgba32[basePixel + x];
                        int offset = 1 + x * 4;
                        row[offset] = (byte)(packed & 0xFF);
                        row[offset + 1] = (byte)((packed >> 8) & 0xFF);
                        row[offset + 2] = (byte)((packed >> 16) & 0xFF);
                        row[offset + 3] = (byte)((packed >> 24) & 0xFF);
                    }
                    zlib.Write(row);
                }
            }
            idat.Complete();
        }

        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }


    /// <summary>Writes an 8-bit RGBA pixel buffer to a PNG file.</summary>
    public static void WriteRgba(string path, int width, int height, byte[] rgba)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An output path is required.", nameof(path));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba == null) throw new ArgumentNullException(nameof(rgba));
        int expected = checked(width * height * 4);
        if (rgba.Length != expected)
            throw new ArgumentException("RGBA buffer size does not match the image dimensions.", nameof(rgba));

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);

        using FileStream output = File.Create(fullPath);
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR"u8, ihdr);

        using (ChunkedIdatStream idat = new(output, 1024 * 1024))
        {
            using (ZLibStream zlib = new(idat, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] row = new byte[checked(width * 4 + 1)];
                for (int y = 0; y < height; y++)
                {
                    row[0] = 0;
                    Buffer.BlockCopy(rgba, y * width * 4, row, 1, width * 4);
                    zlib.Write(row);
                }
            }
            idat.Complete();
        }

        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        output.Write(number);
        output.Write(type);
        output.Write(data);

        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFF);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>Turns one continuous zlib stream into bounded consecutive PNG IDAT chunks.</summary>
    private sealed class ChunkedIdatStream : Stream
    {
        private readonly Stream destination;
        private readonly byte[] buffer;
        private int count;
        private bool completed;

        public ChunkedIdatStream(Stream destination, int chunkSize)
        {
            this.destination = destination;
            buffer = new byte[Math.Max(4096, chunkSize)];
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => FlushChunk();

        public override void Write(byte[] source, int offset, int length) => Write(source.AsSpan(offset, length));

        public override void Write(ReadOnlySpan<byte> source)
        {
            ObjectDisposedException.ThrowIf(completed, this);
            while (!source.IsEmpty)
            {
                int copy = Math.Min(buffer.Length - count, source.Length);
                source[..copy].CopyTo(buffer.AsSpan(count));
                count += copy;
                source = source[copy..];
                if (count == buffer.Length) FlushChunk();
            }
        }

        public void Complete()
        {
            if (completed) return;
            FlushChunk();
            completed = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Complete();
            base.Dispose(disposing);
        }

        private void FlushChunk()
        {
            if (count == 0) return;
            WriteChunk(destination, "IDAT"u8, buffer.AsSpan(0, count));
            count = 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
