using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using Einzel.Render;

using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// The rasterizer and the PNG writer behind <c>einzel render still</c>, checked with no
/// display, which is the point of them.
/// </summary>
public sealed class RasterTests(ITestOutputHelper output)
{
    private static readonly (double R, double G, double B) White = (1.0, 1.0, 1.0);

    // Every surface and line at full brightness, so a pixel's color is exactly its mesh's.
    private static readonly RasterLighting Flat = new((_, _, _) => 1.0, 1.0);

    /// <summary>The checksum is the one PNG specifies.</summary>
    /// <remarks>
    /// The standard check value for CRC-32 over the nine ASCII digits. A table built with the
    /// wrong polynomial or the wrong reflection gives a different number and a file every
    /// reader rejects.
    /// </remarks>
    [Fact]
    public void TheChecksumIsTheStandardOne() =>
        Assert.Equal(0xCBF43926u, PngWriter.Crc(Encoding.ASCII.GetBytes("123456789")));

    /// <summary>A PNG reads back to the pixels written, with its text intact.</summary>
    /// <remarks>
    /// Read back by parsing the chunks and inflating the image data here, not by trusting the
    /// writer - every chunk's checksum is recomputed, and the text is UTF-8 with a micro sign
    /// and a plus-or-minus in it, because a warning quotes both and a Latin-1 chunk would
    /// mangle them.
    /// </remarks>
    [Fact]
    public void APngReadsBackToWhatWasWritten()
    {
        byte[] rgb =
        [
            255, 0, 0, 0, 255, 0, 0, 0, 255,
            10, 20, 30, 40, 50, 60, 200, 210, 220,
        ];

        const string warning = "[Qualified] example: 12.5 µs ± 0.1";

        var png = PngWriter.Write(rgb, 3, 2, [("Warning 1", warning)]);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);

        var chunks = Chunks(png);

        output.WriteLine(string.Join(", ", chunks.Select(c => $"{c.Type} {c.Data.Length}")));

        Assert.Equal(["IHDR", "iTXt", "IDAT", "IEND"], chunks.Select(c => c.Type));

        var header = chunks[0].Data;
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(header));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        Assert.Equal(8, header[8]);
        Assert.Equal(2, header[9]);

        var text = chunks[1].Data;
        var keywordEnd = Array.IndexOf(text, (byte)0);
        Assert.Equal("Warning 1", Encoding.ASCII.GetString(text, 0, keywordEnd));
        Assert.Equal(warning, Encoding.UTF8.GetString(text, keywordEnd + 5, text.Length - keywordEnd - 5));

        Assert.Equal(rgb, Pixels(chunks, 3, 2));
    }

    /// <summary>A triangle covers the pixels inside it and none outside.</summary>
    /// <remarks>
    /// The left half of a square is filled, the right half is not. One sample a pixel so the
    /// test reads coverage rather than antialiasing.
    /// </remarks>
    [Fact]
    public void ATriangleCoversWhatIsInsideIt()
    {
        // The lower-left triangle of the square -10..10 mm.
        var mesh = new RasterMesh([-10, -10, 0, 10, -10, 0, -10, 10, 0], Up(3), [0, 1, 2], 1.0, 0.0, 0.0);

        var rgb = Rasterizer.Draw([Layer(mesh)], Scale(10.0), Flat, White, 20, 20, supersample: 1);

        // Row 17 is near the bottom of the image, column 2 near the left: inside.
        Assert.Equal((255, 0, 0), At(rgb, 20, 2, 17));

        // Near the top right: outside, so still the ground.
        Assert.Equal((255, 255, 255), At(rgb, 20, 17, 2));
    }

    /// <summary>The nearer surface wins whichever is drawn first.</summary>
    /// <remarks>
    /// Both orders, because a painter's algorithm passes this in one order by accident - the
    /// later draw wins - and only the depth test passes it in both.
    /// </remarks>
    [Fact]
    public void TheNearerSurfaceWinsWhicheverIsDrawnFirst()
    {
        // Clip-space depth here is -z/10, so larger z is nearer.
        var near = Square(z: 5.0, 0.0, 0.0, 1.0);
        var far = Square(z: -5.0, 1.0, 0.0, 0.0);

        foreach (var order in new[] { new[] { near, far }, new[] { far, near } })
        {
            var rgb = Rasterizer.Draw(
                [.. order.Select(Layer)], Scale(10.0), Flat, White, 10, 10, supersample: 1);

            Assert.Equal((0, 0, 255), At(rgb, 10, 5, 5));
        }
    }

    /// <summary>A translucent layer blends over what is behind it and hides nothing drawn later.</summary>
    /// <remarks>
    /// The rule the viewport's "see through the metal" depends on: a translucent surface that
    /// writes depth would reject whatever is drawn behind it afterwards. So a far opaque square
    /// drawn after a near translucent one must still show through it.
    /// </remarks>
    [Fact]
    public void ATranslucentLayerHidesNothingDrawnAfterIt()
    {
        var glass = new RasterLayer([Square(z: 5.0, 0.0, 0.0, 1.0)], [], 0.5, WritesDepth: false);
        var behind = Layer(Square(z: -5.0, 1.0, 0.0, 0.0));

        var rgb = Rasterizer.Draw([glass, behind], Scale(10.0), Flat, White, 10, 10, supersample: 1);

        var (r, g, b) = At(rgb, 10, 5, 5);

        output.WriteLine($"through the glass: {r}, {g}, {b}");

        // The opaque square overwrote the blend, which is what a GPU does too with depth
        // writes off - what matters is that it was not rejected.
        Assert.Equal((255, 0, 0), (r, g, b));

        // And with it written first, the glass blends over it half and half.
        var blended = Rasterizer.Draw([behind, glass], Scale(10.0), Flat, White, 10, 10, supersample: 1);

        Assert.Equal((128, 0, 128), At(blended, 10, 5, 5));
    }

    /// <summary>A line is drawn along its length and nowhere else.</summary>
    [Fact]
    public void ALineIsDrawnWhereItIs()
    {
        var line = new RasterLine([-8, 0, 0, 8, 0, 0], 0.0, 0.0, 0.0);
        var rgb = Rasterizer.Draw(
            [new RasterLayer([], [line], 1.0, true)], Scale(10.0), Flat, White, 40, 40, supersample: 1, lineWidth: 2.0);

        // Along the middle row, inside the ends.
        Assert.Equal((0, 0, 0), At(rgb, 40, 20, 20));

        // Past its end, and well off it.
        Assert.Equal((255, 255, 255), At(rgb, 40, 38, 20));
        Assert.Equal((255, 255, 255), At(rgb, 40, 20, 5));
    }

    /// <summary>The violation band is at the bottom and nowhere else.</summary>
    [Fact]
    public void TheHatchIsAlongTheBottomOnly()
    {
        var rgb = Enumerable.Repeat((byte)255, 3 * 100 * 100).ToArray();

        Rasterizer.Hatch(rgb, 100, 100);

        var bottom = Enumerable.Range(0, 100).Count(x => At(rgb, 100, x, 99) != (255, 255, 255));
        var top = Enumerable.Range(0, 100).Count(x => At(rgb, 100, x, 0) != (255, 255, 255));

        output.WriteLine($"marked pixels: {bottom} on the bottom row, {top} on the top");

        Assert.True(bottom > 30, "the bottom row is not hatched");
        Assert.Equal(0, top);
    }

    /// <summary>
    /// A picture drawn in bands is the same bytes as one drawn in a single pass, however thin
    /// the bands and wherever their edges fall.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every kind of thing a band boundary could cut: triangles and lines crossing it,
    /// overlapping opaque surfaces sorted only by depth, and a translucent layer blending
    /// over them - the one where a sample drawn twice, or once too few, would show. Lit by
    /// a normal-dependent function so shading is compared too, and at an odd size so the
    /// last band is shorter than the rest.
    /// </para>
    /// <para>
    /// Byte equality rather than a tolerance, because the banding keeps each sample in the
    /// whole picture's coordinates and so does the same arithmetic a single pass does.
    /// </para>
    /// </remarks>
    /// <param name="samplesPerBand">The band budget: one output row, three, or a whole pass.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(3 * 37 * 4)]
    [InlineData(Rasterizer.DefaultSamplesPerBand)]
    public void ABandedPictureIsTheSameBytesAsOnePass(int samplesPerBand)
    {
        var lit = new RasterLighting((nx, ny, nz) => 0.3 + (0.7 * Math.Abs(nz) / Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz))), 0.8);

        RasterLayer[] layers =
        [
            new([], [new RasterLine([-9, -9, 2, 9, 7, 2, -4, 9, 2], 0.0, 0.2, 0.9)], 1.0, WritesDepth: true),
            Layer(new RasterMesh([-10, -10, 0, 8, -6, 4, -3, 10, -2], [0.2, 0, 1, 0, 0.3, 1, -0.4, 0, 1], [0, 1, 2], 0.9, 0.1, 0.1)),
            Layer(new RasterMesh([10, -9, 3, 6, 10, -1, -10, 2, 1], [0, 0, 1, 0.5, 0, 1, 0, -0.5, 1], [0, 1, 2], 0.1, 0.8, 0.2)),
            new([Square(z: 6.0, 0.9, 0.9, 0.0)], [], 0.35, WritesDepth: false),
        ];

        var single = Rasterizer.Draw(layers, Scale(10.0), lit, White, 37, 29, samplesPerBand: int.MaxValue);
        var banded = Rasterizer.Draw(layers, Scale(10.0), lit, White, 37, 29, samplesPerBand: samplesPerBand);

        Assert.Equal(single, banded);
        Assert.Contains(single, value => value is > 0 and < 255);
    }

    /// <summary>
    /// A large picture holds one band in memory at a time, not the whole supersampled canvas.
    /// </summary>
    /// <remarks>
    /// At 2048 pixels a side and two samples a pixel each way, the whole canvas is 16.8 million
    /// samples of 32 bytes: 537 MB. The bound here is the output plus one band with room to
    /// spare, under a third of that - so a canvas allocated whole, or a buffer allocated per
    /// band rather than reused, fails it. At the 8192 pixels `einzel render still` allows, the
    /// whole canvas was 8.6 GB and ran out of memory.
    /// </remarks>
    [Fact]
    public void ALargePictureHoldsOneBandAtATime()
    {
        const int Side = 2048;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var rgb = Rasterizer.Draw([Layer(Square(z: 0.0, 1.0, 0.0, 0.0))], Scale(10.0), Flat, White, Side, Side);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var bound = (3L * Side * Side) + (40L * Rasterizer.DefaultSamplesPerBand);

        output.WriteLine($"allocated {allocated / 1e6:F1} MB, bound {bound / 1e6:F1} MB, whole canvas {32.0 * 4 * Side * Side / 1e6:F1} MB");

        Assert.Equal(3 * Side * Side, rgb.Length);
        Assert.True(allocated < bound, $"drawing allocated {allocated / 1e6:F1} MB against {bound / 1e6:F1} MB");
    }

    private static RasterLayer Layer(RasterMesh mesh) => new([mesh], [], 1.0, WritesDepth: true);

    private static RasterMesh Square(double z, double r, double g, double b) =>
        new([-10, -10, z, 10, -10, z, 10, 10, z, -10, 10, z], Up(4), [0, 1, 2, 0, 2, 3], r, g, b);

    private static readonly double[] Normal = [0.0, 0.0, 1.0];

    private static double[] Up(int vertices) => [.. Enumerable.Range(0, vertices).SelectMany(_ => Normal)];

    /// <summary>An orthographic matrix mapping plus or minus <paramref name="half"/> mm onto the clip box.</summary>
    private static double[] Scale(double half) =>
    [
        1.0 / half, 0, 0, 0,
        0, 1.0 / half, 0, 0,
        0, 0, -1.0 / half, 0,
        0, 0, 0, 1,
    ];

    private static (int R, int G, int B) At(byte[] rgb, int width, int x, int y)
    {
        var i = 3 * ((y * width) + x);
        return (rgb[i], rgb[i + 1], rgb[i + 2]);
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        var at = 8;

        while (at < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            var typed = png.AsSpan(at + 4, 4 + length);
            var stored = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));

            Assert.Equal(PngWriter.Crc(typed), stored);

            chunks.Add((Encoding.ASCII.GetString(typed[..4]), typed[4..].ToArray()));
            at += 12 + length;
        }

        return chunks;
    }

    private static byte[] Pixels(List<(string Type, byte[] Data)> chunks, int width, int height)
    {
        var data = chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();

        using var inflated = new MemoryStream();

        using (var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var raw = inflated.ToArray();
        var stride = 3 * width;
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, raw[y * (stride + 1)]);
            Array.Copy(raw, (y * (stride + 1)) + 1, pixels, y * stride, stride);
        }

        return pixels;
    }
}
