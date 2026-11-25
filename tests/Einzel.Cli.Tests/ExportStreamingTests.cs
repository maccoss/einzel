using Einzel.Commands;
using Einzel.Fields.Solved;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// An exported volume is written to its file as it is formed, not built as a string and then as a
/// byte array first.
/// </summary>
/// <remarks>
/// The channel files were staged as <c>stream.Write(ArtifactFiles.Utf8(channel.Write(...)))</c>:
/// the text as a string (two bytes a character), the builder it was formed in, and then the bytes
/// - about three times the file, held at once, where a volume's text is about 24 bytes a node.
/// </remarks>
[Collection(nameof(RunsAlone))]
public sealed class ExportStreamingTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-export-streaming", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ScalarField3D Field(int n)
    {
        var field = new ScalarField3D(new Grid3D(-0.01, -0.02, 0.003, 1e-4, 2e-4, 3e-4, n, n + 1, n + 2));

        for (var k = 0; k < n + 2; k++)
        {
            for (var j = 0; j < n + 1; j++)
            {
                for (var i = 0; i < n; i++)
                {
                    // Values whose round-trip text is long, so the file is the size a solve's is.
                    field[i, j, k] = Math.Sin(i * 0.37) * 123.456789 + j * 1e-7 - k / 3.0;
                }
            }
        }

        return field;
    }

    /// <summary>
    /// The staged file is the bytes the string form of the document gives in UTF-8 without a mark -
    /// what every export wrote before - and staging it allocates a small fraction of its size.
    /// </summary>
    [Fact]
    public void AVolumeIsStagedAsTheSameBytesWithoutFormingItInMemory()
    {
        Directory.CreateDirectory(_root);
        var field = Field(48);
        string[] provenance = ["engine: test", "a -- comment that must be escaped", "élément"];

        var expected = ArtifactFiles.Utf8(VtuWriter.WriteScalarField(field, "potential_V", provenance));
        var path = Path.Combine(_root, "field.vti");

        // Warm the path once, so the measurement is of the write and not of the JIT.
        using (var warm = ArtifactFiles.Begin())
        {
            ExportCommand.StageText(warm, Path.Combine(_root, "warm.vti"), w => VtuWriter.WriteScalarField(w, Field(3), "x"));
        }

        long allocated;

        using (var staged = ArtifactFiles.Begin())
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            ExportCommand.StageText(staged, path, w => VtuWriter.WriteScalarField(w, field, "potential_V", provenance));
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            staged.Commit();
        }

        var bytes = File.ReadAllBytes(path);
        output.WriteLine($"file {bytes.Length} bytes; staging allocated {allocated} bytes ({(double)allocated / bytes.Length:F3} of the file)");

        Assert.Equal(expected, bytes);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "no byte-order mark");

        // Formed in memory first it is about three times the file; streamed, a fixed buffer.
        Assert.True(
            allocated < bytes.Length / 4,
            $"staging a {bytes.Length}-byte file allocated {allocated} bytes: it is being formed in memory");
    }
}
