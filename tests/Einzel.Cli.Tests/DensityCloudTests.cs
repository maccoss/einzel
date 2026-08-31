using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The density, drawn as a cloud rather than as nothing (§16, TRN-2).
/// </summary>
/// <remarks>
/// <para>
/// <b>RND-8 on its own is entirely negative.</b> It says never to draw trajectories
/// through a diffusive region, which the viewport already honoured — and the consequence
/// was that the mode's principal result could be summarised into a transmission and a
/// transit time and looked at in no other form. The honest picture of a funnel at a
/// millibar was an empty box.
/// </para>
/// <para>
/// Worse, the viewport's own warning said the density "is drawn as contours". The 2-D
/// section does draw them; the viewport drew nothing at all, so the message described a
/// capability of a different surface.
/// </para>
/// </remarks>
public sealed class DensityCloudTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-density-cloud", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private string Example(string name)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", $"{name}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, "--from-example", name).ExitCode);
        }

        return path;
    }

    /// <summary>A diffusive model draws a cloud where it draws no paths.</summary>
    /// <remarks>
    /// <para>
    /// Both halves in one test, because each alone is satisfied by the wrong thing: no
    /// paths and no cloud is the old behaviour, and a cloud alongside paths would be RND-8
    /// broken. What the requirement asks for is the substitution.
    /// </para>
    /// <para>
    /// The shells are nested by construction — each is a decade below the last — so the
    /// levels must fall monotonically and the last must be a thousandth of the peak. That
    /// is asserted rather than the count, because how many shells survive depends on how
    /// far the packet has spread and is a property of the run rather than of the drawing.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiffusiveModelDrawsACloudAndNoPaths()
    {
        var outcome = ViewportCommand.Execute(Example("drift-tube-diffusion"));

        Assert.False(outcome.ProducesTrajectories);
        Assert.Empty(outcome.Trajectories);

        Assert.NotNull(outcome.PeakDensityPerCubicMetre);
        Assert.NotEmpty(outcome.Density);

        foreach (var shell in outcome.Density)
        {
            output.WriteLine(
                $"decade -{shell.DecadesBelowPeak}: {shell.DensityPerCubicMetre:G4} /m3, "
                + $"{shell.VerticesMm.Count / 3} vertices, "
                + $"{shell.Triangles.Count / 3} triangles");
        }

        // Each shell is a decade below the one before it, and stands at the level it says.
        for (var k = 0; k < outcome.Density.Count; k++)
        {
            var shell = outcome.Density[k];

            Assert.Equal(k + 1, shell.DecadesBelowPeak);

            Assert.Equal(
                outcome.PeakDensityPerCubicMetre!.Value * Math.Pow(10.0, -(k + 1)),
                shell.DensityPerCubicMetre,
                12);
        }

        // A drawable surface: three coordinates per vertex, one normal per vertex, and
        // triangles indexing vertices that exist.
        foreach (var shell in outcome.Density)
        {
            Assert.Equal(0, shell.VerticesMm.Count % 3);
            Assert.Equal(shell.VerticesMm.Count, shell.Normals.Count);
            Assert.Equal(0, shell.Triangles.Count % 3);
            Assert.All(shell.Triangles, i => Assert.InRange(i, 0, (shell.VerticesMm.Count / 3) - 1));
        }
    }

    /// <summary>Every normal is a unit vector.</summary>
    /// <remarks>
    /// A shell lit by a normal that is not unit length renders darker or brighter than its
    /// neighbours for no reason in the data, which reads as structure in the density. The
    /// cheapest check that the orientation pass did its job.
    /// </remarks>
    [Fact]
    public void EveryShellNormalIsAUnitVector()
    {
        var outcome = ViewportCommand.Execute(Example("drift-tube-diffusion"));

        Assert.NotEmpty(outcome.Density);

        var worst = 0.0;
        var where = "";
        var zeros = 0;
        var total = 0;

        foreach (var shell in outcome.Density)
        {
            for (var i = 0; i < shell.Normals.Count; i += 3)
            {
                total++;

                var length = Math.Sqrt(
                    (shell.Normals[i] * shell.Normals[i])
                    + (shell.Normals[i + 1] * shell.Normals[i + 1])
                    + (shell.Normals[i + 2] * shell.Normals[i + 2]));

                if (length == 0.0)
                {
                    zeros++;
                }

                if (Math.Abs(length - 1.0) > worst)
                {
                    worst = Math.Abs(length - 1.0);
                    where = $"decade -{shell.DecadesBelowPeak} at "
                        + $"({shell.VerticesMm[i]:F3}, {shell.VerticesMm[i + 1]:F3}, "
                        + $"{shell.VerticesMm[i + 2]:F3}) mm";
                }
            }
        }

        output.WriteLine(
            $"{total} normals, {zeros} of them zero; worst departure {worst:E3} {where}");

        Assert.True(worst < 1e-9, $"a normal is {worst:E3} off unit length, {where}");
    }

    /// <summary>The shells enclose the packet, not the whole grid.</summary>
    /// <remarks>
    /// <para>
    /// <b>The assertion that separates a cloud from a box.</b> A shell extracted at a
    /// level nothing reaches, or one that failed to find its contour and fell back to the
    /// domain edge, is still a well-formed mesh with unit normals and valid indices — it
    /// passes every structural check above. What it cannot do is sit inside the grid.
    /// </para>
    /// <para>
    /// And the packet must be somewhere specific: the drift tube's ions start at one end
    /// and are carried along x, so the densest shell has to be narrower in x than the
    /// tracked region. A drawing whose innermost shell spanned the domain would be
    /// depicting a uniform gas rather than a packet.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShellsEncloseThePacketRatherThanTheGrid()
    {
        var model = Example("drift-tube-diffusion");
        var outcome = ViewportCommand.Execute(model);

        Assert.NotEmpty(outcome.Density);

        var validation = Core.Model.ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(model)), null, Path.GetDirectoryName(model));

        Assert.True(validation.IsValid);

        var box = validation.Model!.DensityGrid!;

        var spanMm = (box.MaxX - box.MinX) * 1e3;

        var core = outcome.Density[0];

        var xs = new List<double>();

        for (var i = 0; i < core.VerticesMm.Count; i += 3)
        {
            xs.Add(core.VerticesMm[i]);
        }

        var coreSpan = xs.Max() - xs.Min();

        output.WriteLine($"tracked region {spanMm:F1} mm in x");
        output.WriteLine($"densest shell  {coreSpan:F1} mm in x, "
            + $"centred at {(xs.Max() + xs.Min()) / 2.0:F1} mm");

        Assert.True(
            coreSpan < 0.9 * spanMm,
            $"the densest shell spans {coreSpan:F1} mm of a {spanMm:F1} mm region, which "
            + "is a picture of a uniform gas rather than of a packet");

        // Inside the tracked region in x, since a contour is traced on that grid and
        // cannot honestly leave it.
        Assert.InRange(xs.Min(), (box.MinX * 1e3) - 1e-6, (box.MaxX * 1e3) + 1e-6);
        Assert.InRange(xs.Max(), (box.MinX * 1e3) - 1e-6, (box.MaxX * 1e3) + 1e-6);
    }

    /// <summary>A trajectory model draws no cloud, and says nothing about one.</summary>
    /// <remarks>
    /// The control. A viewport that ran the diffusive transport for every model would be
    /// slow and would report a density for an instrument that computes none — the mirror
    /// of the defect being fixed, and just as misleading.
    /// </remarks>
    [Fact]
    public void ATrajectoryModelDrawsNoCloud()
    {
        var outcome = ViewportCommand.Execute(Example("single-stage-reflectron"));

        Assert.True(outcome.ProducesTrajectories);
        Assert.NotEmpty(outcome.Trajectories);

        output.WriteLine(
            $"{outcome.Trajectories.Count} paths, {outcome.Density.Count} shells, "
            + $"peak {outcome.PeakDensityPerCubicMetre?.ToString() ?? "absent"}");

        Assert.Empty(outcome.Density);

        // Absent rather than zero: zero is a real density and a reader cannot tell the
        // two apart if both print as zero. The rule the rest of this surface follows.
        Assert.Null(outcome.PeakDensityPerCubicMetre);
    }
}
