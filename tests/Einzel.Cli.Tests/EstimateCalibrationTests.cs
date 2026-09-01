using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// GRD-8's cost gate has to be about the machine that will do the work.
/// </summary>
/// <remarks>
/// <para>
/// The rate was a single hardcoded constant — 13 s per million nodes, measured on the
/// shipped 2-D templates on one developer's box — and it was applied to volume solves too,
/// declared as a floor and read as an estimate. On the shipped C-trap that put a 5.9 s
/// solve at 1.81 s, <b>3.3x under</b>, and an estimate that runs under is worse than one
/// that runs over.
/// </para>
/// <para>
/// <b>An absolute time is a statement about a machine.</b> That is the same thing the
/// extension timing tests learned twice (SPEC.md Amendment 27), and it matters more here,
/// because this number is what somebody plans a multi-day run against, on hardware this
/// engine has never seen.
/// </para>
/// <para>
/// So the rate is measured by solving a coarsened copy of the model's own geometry.
/// Coarsened rather than fabricated: the rate is not a property of the solver alone, since
/// a boundary-value problem converges faster per node than one with interior electrodes —
/// which is exactly the spread the old constant hid by taking the larger of two figures.
/// </para>
/// </remarks>
public sealed class EstimateCalibrationTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-estimate", Guid.NewGuid().ToString("N"));

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

    private string Template(string name)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", $"{name}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, "--from-template", name).ExitCode);
        }

        return path;
    }

    /// <summary>The estimate says it measured this machine, and what it measured.</summary>
    /// <remarks>
    /// GRD-12's rule applied to a cost: a number whose provenance is not stated invites more
    /// trust than it has earned. The basis line carries the pilot's node count and its time,
    /// so a reader can see whether the measurement was worth anything.
    /// </remarks>
    [Fact]
    public void ACalibratedEstimateSaysWhatItMeasured()
    {
        var estimate = Cli("estimate", Template("planar-mirror-pair"));

        Assert.Equal(0, estimate.ExitCode);

        var basis = estimate.Stdout
            .Split('\n')
            .Single(l => l.Contains("basis:", StringComparison.Ordinal));

        output.WriteLine(basis.Trim());

        Assert.Contains("measured on this machine", basis, StringComparison.Ordinal);
        Assert.Contains("nodes in", basis, StringComparison.Ordinal);
    }

    /// <summary>Refusing to calibrate says the number is not about this machine.</summary>
    /// <remarks>
    /// A pilot solve does not fit PERF-8's 500 ms cold-start budget on a volume geometry —
    /// the C-trap's pilot alone takes about 750 ms. The opt-out keeps that budget reachable,
    /// and the point of the test is that the two modes are <b>distinguishable in the
    /// output</b>: an uncalibrated estimate must not look like a measured one.
    /// </remarks>
    [Fact]
    public void RefusingToCalibrateSaysTheNumberIsNotAboutThisMachine()
    {
        var model = Template("planar-mirror-pair");

        var measured = Cli("estimate", model);
        var quoted = Cli("estimate", model, "--no-calibrate");

        Assert.Equal(0, quoted.ExitCode);

        static string Basis(string stdout) =>
            stdout.Split('\n').Single(l => l.Contains("basis:", StringComparison.Ordinal)).Trim();

        output.WriteLine($"calibrated:   {Basis(measured.Stdout)}");
        output.WriteLine($"uncalibrated: {Basis(quoted.Stdout)}");

        Assert.Contains("nothing was measured here", Basis(quoted.Stdout), StringComparison.Ordinal);
        Assert.DoesNotContain("measured on this machine", Basis(quoted.Stdout), StringComparison.Ordinal);

        // And the two really are different numbers arrived at differently, rather than the
        // same constant wearing two labels.
        Assert.NotEqual(Basis(measured.Stdout), Basis(quoted.Stdout));
    }

    /// <summary>A volume solve is not costed at a plane solve's rate.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the defect that prompted the work.</b> A volume cycle carries a 27-point
    /// stencil where a plane carries five, and its coarse levels are built by Galerkin
    /// rather than rediscretised. Both were costed at 13 s per million nodes, which put the
    /// C-trap's 5.9 s solve at 1.81 s.
    /// </para>
    /// <para>
    /// What is asserted is that the volume rate comes out <i>above</i> the plane rate on the
    /// same machine, because that ordering is a property of the two stencils rather than of
    /// any particular hardware — a machine-independent claim, which is the only kind worth
    /// asserting here.
    /// </para>
    /// </remarks>
    [Fact]
    public void AVolumeSolveIsCostedAboveAPlaneSolve()
    {
        static double Rate(string basis, string kind)
        {
            var after = basis[(basis.IndexOf(kind, StringComparison.Ordinal) + kind.Length)..];
            var number = after.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

            return double.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
        }

        var plane = Cli("estimate", Template("planar-mirror-pair")).Stdout;
        var volume = Cli("estimate", Template("c-trap")).Stdout;

        var planeRate = Rate(plane, "Plane solves: ");
        var volumeRate = Rate(volume, "Volume solves: ");

        output.WriteLine($"plane   {planeRate:F1} s per million nodes");
        output.WriteLine($"volume  {volumeRate:F1} s per million nodes  ({volumeRate / planeRate:F1}x)");

        Assert.True(
            volumeRate > planeRate,
            $"a volume solve was costed at {volumeRate:F1} s per million nodes against "
            + $"{planeRate:F1} for a plane, so the 27-point stencil is being charged no more "
            + "than the five-point one and a volume estimate will run under");
    }
}
