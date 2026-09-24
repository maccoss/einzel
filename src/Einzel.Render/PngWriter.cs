using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Einzel.Render;

/// <summary>Writes an RGB image as a PNG, with its provenance in text chunks.</summary>
/// <remarks>
/// <para>
/// <b>Hand-written, for the reason the PDF writer is.</b> PNG is a chunked container over
/// zlib, and zlib is in the base library, so the whole format is a few dozen lines and a
/// checksum. An imaging library would be a dependency whose license has to be re-checked on
/// every release (LIC-1) to do something this small.
/// </para>
/// <para>
/// <b>The provenance travels in the file</b> (PRJ-3, GRD-2): <c>iTXt</c> chunks carry the
/// engine version, the model and its hash, the view, and every warning the viewport earned,
/// in UTF-8 because a warning quotes microseconds and plus-or-minus. A PNG is copied into
/// slides and chat without the JSON that described it, so what qualified it must go with it.
/// </para>
/// </remarks>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] Table = BuildTable();

    /// <summary>Encodes an image.</summary>
    /// <param name="rgb">Rows top to bottom, three bytes a pixel.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="text">Keyword and text pairs to carry in the file.</param>
    /// <returns>The PNG file's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rgb"/> or <paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException">The buffer does not hold that many pixels, or a keyword is not a PNG keyword.</exception>
    public static byte[] Write(
        byte[] rgb, int width, int height, IReadOnlyList<(string Keyword, string Text)> text)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (rgb.Length != 3 * width * height)
        {
            throw new ArgumentException(
                $"{rgb.Length} bytes is not a {width} by {height} RGB image", nameof(rgb));
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;  // bits per channel
        header[9] = 2;  // truecolor, no alpha: the ground is opaque
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // not interlaced
        Chunk(output, "IHDR", header);

        foreach (var (keyword, value) in text)
        {
            Chunk(output, "iTXt", International(keyword, value));
        }

        Chunk(output, "IDAT", Compressed(rgb, width, height));
        Chunk(output, "IEND", []);

        return output.ToArray();
    }

    /// <summary>The CRC-32 PNG uses, over a run of bytes.</summary>
    /// <param name="data">What to checksum.</param>
    /// <returns>The checksum.</returns>
    /// <remarks>Public so the one test that matters - the standard check value - can reach it.</remarks>
    public static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] Compressed(byte[] rgb, int width, int height)
    {
        var stride = 3 * width;

        using var buffer = new MemoryStream();

        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                // Filter type 0 on every row. A figure is flat fields of color, which
                // deflate already compresses well, and the adaptive filters would be code
                // spent saving bytes nobody is counting.
                zlib.WriteByte(0);
                zlib.Write(rgb, y * stride, stride);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] International(string keyword, string value)
    {
        if (keyword.Length is < 1 or > 79 || keyword.Any(c => c < 32 || c > 126))
        {
            throw new ArgumentException($"'{keyword}' is not a PNG keyword", nameof(keyword));
        }

        // keyword, NUL, not compressed, method 0, empty language tag NUL,
        // empty translated keyword NUL, then the text in UTF-8.
        var head = Encoding.ASCII.GetBytes(keyword);
        var body = Encoding.UTF8.GetBytes(value);

        var chunk = new byte[head.Length + 5 + body.Length];
        head.CopyTo(chunk, 0);
        body.CopyTo(chunk, head.Length + 5);

        return chunk;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);

        var typed = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        output.Write(typed);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typed));
        output.Write(number);
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var n = 0u; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
