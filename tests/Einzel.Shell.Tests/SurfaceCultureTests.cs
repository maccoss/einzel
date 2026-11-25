using System.Globalization;
using System.Text;

using Einzel.Commands;
using Einzel.Project;

namespace Einzel.Shell.Tests;

/// <summary>
/// What <c>render surface</c> writes is the same under a desktop locale that prints a decimal
/// comma.
/// </summary>
/// <remarks>
/// In this project, where globalization is on as it is in the shell; the command line's own
/// tests run globalization-invariant and cannot see a culture at all. The surface's warnings
/// and the text it puts in the PNG were formatted with plain interpolation beside strings that
/// named the invariant culture, so under de-DE one file said "t = 2,5 us" a line away from
/// "2.5".
/// </remarks>
[Collection(nameof(ProcessCulture))]
public sealed class SurfaceCultureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-surface-culture", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A driven field drawn at t = 2.5 us says 2.5, in the file and in every warning.</summary>
    [Fact]
    public void TheSurfaceWritesInvariantNumbersUnderADecimalCommaLocale()
    {
        Directory.CreateDirectory(_root);
        var model = Path.Combine(_root, "quadrupole.json");
        Assert.Equal(0, Einzel.Cli.Program.Main(["new", model, "--from-example", "quadrupole-rf-stable"]));

        var before = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // The control: as things stand on this thread, a number prints with a comma.
            Assert.Equal("2,5", $"{2.5:G6}");

            var png = Path.Combine(_root, "surface.png");
            var request = new SurfaceRequest(Plane: "xy", BoxMm: [-2.5, -2.5, 2.5, 2.5], Samples: 11, AtUs: 2.5);

            var outcome = SurfaceCommand.Execute(model, new ProjectLayout(_root), request, width: 200, height: 150, outputPath: png);
            var file = Encoding.UTF8.GetString(File.ReadAllBytes(png));

            Assert.Contains("the instant t = 2.5 us", file, StringComparison.Ordinal);
            Assert.DoesNotContain("2,5", file, StringComparison.Ordinal);
            Assert.All(outcome.Warnings, w => Assert.DoesNotContain("2,5", w.Message, StringComparison.Ordinal));

            // And a refusal names the instant the same way.
            var refusal = Assert.Throws<Einzel.Core.Errors.EinzelException>(() => SurfaceCommand.Sample(
                Example("sequenced-uniform"), new SurfaceRequest(BoxMm: [-5, -5, 5, 5], Samples: 5, AtUs: 2.5, CycleMean: true)));

            Assert.Contains("t = 2.5 us", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private string Example(string name)
    {
        var path = Path.Combine(_root, name + ".json");
        Assert.Equal(0, Einzel.Cli.Program.Main(["new", path, "--from-example", name]));

        return path;
    }
}
