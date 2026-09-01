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

    /// <summary>The estimate says what it measured, or why it could not.</summary>
    /// <remarks>
    /// <para>
    /// GRD-12's rule applied to a cost: a number whose provenance is not stated invites more
    /// trust than it has earned. Either way the basis names the pilot's size, so a reader can
    /// see whether the measurement was worth anything.
    /// </para>
    /// <para>
    /// <b>A disjunction, and it has to be.</b> A first version asserted the measured branch
    /// unconditionally and <b>failed on a fast CI runner</b>, where the pilot solve took
    /// 12 ms - under the floor below which timing a pilot measures the clock rather than the
    /// solve. The engine was right and the test was asserting a machine speed. SPEC.md
    /// Amendment 27's own lesson met from a new direction: an absolute time is a statement
    /// about a machine, and so is the assumption that a given amount of work takes long
    /// enough to time at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACalibratedEstimateSaysWhatItMeasuredOrWhyItCouldNot()
    {
        var estimate = Cli("estimate", Template("planar-mirror-pair"));

        Assert.Equal(0, estimate.ExitCode);

        var basis = estimate.Stdout
            .Split('\n')
            .Single(l => l.Contains("basis:", StringComparison.Ordinal));

        output.WriteLine(basis.Trim());

        var measured = basis.Contains("measured on this machine", StringComparison.Ordinal);
        var tooFast = basis.Contains("too little to time", StringComparison.Ordinal);

        Assert.True(
            measured ^ tooFast,
            "a calibrated estimate must say either that it measured this machine or why it "
            + $"could not, and exactly one of those - the basis was: {basis.Trim()}");

        // Either way the pilot's size is on the page, which is what makes the claim
        // checkable rather than merely asserted.
        Assert.Contains("nodes", basis, StringComparison.Ordinal);
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
    /// stencil where a plane carries five, and its coarse levels are built by Galerkin rather
    /// than rediscretised. Both were costed at 13 s per million nodes, which put the C-trap's
    /// 5.9 s solve at 1.81 s.
    /// </para>
    /// <para>
    /// <b>The comparison is only meaningful when both rates come from the same source</b>, and
    /// a first version did not check that. On a fast CI runner the <i>plane</i> pilot came in
    /// under the floor below which timing a pilot measures the clock, so it fell back to the
    /// documented 13.0 while the volume pilot measured 11.2 - and the test compared a constant
    /// against a measurement and failed. Two numbers of different provenance are not
    /// comparable however reasonable each is on its own.
    /// </para>
    /// <para>
    /// So the ordering is asserted against the documented constants, which is deterministic
    /// and is the guarantee the code makes; and additionally against the measured pair
    /// <i>when both were measured</i>, which is the physical claim. The mode is printed either
    /// way, so a run where the second assertion did not apply says so rather than passing
    /// quietly.
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

        static bool Measured(string basis, string kind)
        {
            var after = basis[basis.IndexOf(kind, StringComparison.Ordinal)..];
            var sentence = after.Split(". ", StringSplitOptions.None)[0];

            return sentence.Contains("measured on this machine", StringComparison.Ordinal);
        }

        // The documented constants, which the code guarantees and which do not depend on
        // any machine finishing a pilot quickly or slowly.
        var quotedPlane = Cli("estimate", Template("planar-mirror-pair"), "--no-calibrate").Stdout;
        var quotedVolume = Cli("estimate", Template("c-trap"), "--no-calibrate").Stdout;

        var documentedPlane = Rate(quotedPlane, "Plane solves: ");
        var documentedVolume = Rate(quotedVolume, "Volume solves: ");

        output.WriteLine($"documented   plane {documentedPlane,6:F1}   volume {documentedVolume,6:F1}"
            + $"   ({documentedVolume / documentedPlane:F1}x)");

        Assert.True(
            documentedVolume > documentedPlane,
            $"the documented volume rate is {documentedVolume:F1} s per million nodes against "
            + $"{documentedPlane:F1} for a plane, so the 27-point stencil is charged no more "
            + "than the five-point one and every uncalibrated volume estimate runs under");

        // And the physical claim, when this machine actually measured both. A pilot under
        // the timing floor falls back to the constant, and comparing that with a measured
        // rate compares two different things.
        var plane = Cli("estimate", Template("planar-mirror-pair")).Stdout;
        var volume = Cli("estimate", Template("c-trap")).Stdout;

        var bothMeasured = Measured(plane, "Plane solves: ") && Measured(volume, "Volume solves: ");

        var planeRate = Rate(plane, "Plane solves: ");
        var volumeRate = Rate(volume, "Volume solves: ");

        output.WriteLine($"measured     plane {planeRate,6:F1}   volume {volumeRate,6:F1}"
            + $"   ({volumeRate / planeRate:F1}x)   both measured: {bothMeasured}");

        if (!bothMeasured)
        {
            // Said out loud rather than passed over: on this run one pilot was too fast to
            // time, so the measured pair is not a like-for-like comparison and only the
            // documented ordering above was checked.
            output.WriteLine(
                "  one pilot fell back to the documented rate, so the measured ordering "
                + "was not asserted on this run");

            return;
        }

        Assert.True(
            volumeRate > planeRate,
            $"a volume solve measured {volumeRate:F1} s per million nodes against "
            + $"{planeRate:F1} for a plane on the same machine in the same run, and the "
            + "27-point stencil should cost more than the five-point one");
    }
}
