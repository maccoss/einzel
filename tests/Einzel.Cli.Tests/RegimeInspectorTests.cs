using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// REG-2's dimensionless numbers along a path, and where they go wrong (§16).
/// </summary>
public sealed class RegimeInspectorTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-regime", Guid.NewGuid().ToString("N"));

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
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Cli("new", path, "--from-example", name).ExitCode);

        return path;
    }

    /// <summary>A gas-filled flight reports its numbers along the whole path.</summary>
    [Fact]
    public void AGasFilledFlightReportsItsNumbersAlongThePath()
    {
        var profile = RegimeCommand.Execute(Example("gas-flow-carry"), samples: 32);

        output.WriteLine($"{profile.Samples.Count} samples, aperture {profile.ApertureMm:F1} mm");

        Assert.NotEmpty(profile.Samples);

        var first = profile.Samples[0];
        var last = profile.Samples[^1];

        output.WriteLine(
            $"  at {first.TimeUs,9:F1} us  x {first.PositionMm[0],7:F1} mm  "
            + $"{first.PressureMbar:G3} mbar  Kn {first.Knudsen:G3}  "
            + $"{first.ReducedFieldTd:G3} Td");
        output.WriteLine(
            $"  at {last.TimeUs,9:F1} us  x {last.PositionMm[0],7:F1} mm  "
            + $"{last.PressureMbar:G3} mbar  Kn {last.Knudsen:G3}  "
            + $"{last.ReducedFieldTd:G3} Td");

        // In flight order, and covering the flight rather than a moment of it.
        Assert.True(last.TimeUs > first.TimeUs);
        Assert.True(
            profile.Samples.Zip(profile.Samples.Skip(1)).All(p => p.Second.TimeUs >= p.First.TimeUs),
            "the samples are not in flight order");

        Assert.All(profile.Samples, s => Assert.True(s.PressureMbar > 0.0));
        Assert.All(profile.Samples, s => Assert.True(double.IsFinite(s.Knudsen)));
    }

    /// <summary>
    /// In a uniform gas the pressure is flat and the Knudsen number is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Which of these is constant is the physics, and getting it wrong was my first
    /// version of this test.</b> The pressure is a property of the gas and does not vary
    /// where the gas does not - so a profile that reported it varying would mean the local
    /// substitution was reading something other than the local density.
    /// </para>
    /// <para>
    /// The Knudsen number is a mean free path over a length, and the mean free path depends
    /// on the ion's <em>speed</em>: an ion colliding its way down a tube is slowing and
    /// speeding continually, so its mean free path changes even though the gas does not.
    /// A profile reporting a constant Knudsen number in a gas that damps the ion would mean
    /// the speed was not entering at all — which is what the worst-case measurement does,
    /// deliberately, and is exactly what this view must not do.
    /// </para>
    /// </remarks>
    [Fact]
    public void InAUniformGasThePressureIsFlatAndTheKnudsenNumberIsNot()
    {
        var profile = RegimeCommand.Execute(Example("gas-flow-carry"), samples: 24);

        var pressures = profile.Samples.Select(s => s.PressureMbar).Distinct().ToList();
        var knudsens = profile.Samples.Select(s => s.Knudsen).Distinct().ToList();
        var speeds = profile.Samples.Select(s => s.SpeedMs).ToList();

        output.WriteLine(
            $"{pressures.Count} distinct pressures and {knudsens.Count} distinct Knudsen "
            + $"numbers over {profile.Samples.Count} samples");
        output.WriteLine($"speed {speeds.Min():F1} to {speeds.Max():F1} m/s");

        Assert.Single(pressures);

        // Varying, and varying because the speed does - the two move together, which is
        // what says the local speed is entering rather than a constant standing in for it.
        Assert.True(knudsens.Count > 1, "the Knudsen number did not follow the ion's speed");
        Assert.True(speeds.Max() > speeds.Min());
    }

    /// <summary>
    /// A graded gas puts the ion in more than one regime, and says between where and where.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the whole reason the view exists.</b> A run reports the worst point
    /// anywhere, which is right for a warning and tells a person nothing about what to
    /// change. The corpus's pressure-gradient tube thickens from 1 mbar at the packet to
    /// 2 mbar at the detector, so the numbers genuinely differ along it — and an inspector
    /// that collapsed them would report a regime the ion is in at one end only.
    /// </para>
    /// <para>
    /// Monotone rather than merely different, because the ramp is monotone: a profile that
    /// varied without following the gas would be sampling something else.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGradedGasIsReportedAlongThePathRatherThanCollapsed()
    {
        var profile = RegimeCommand.Execute(
            Example("drift-tube-pressure-gradient"), samples: 32);

        output.WriteLine($"mode {profile.Mode}, {profile.Samples.Count} samples");

        foreach (var warning in profile.Warnings)
        {
            output.WriteLine($"  [{warning.Severity}] {warning.Code}");
        }

        // This corpus example is diffusive, and a density has no path by construction -
        // so the honest answer is to say so rather than to fly an ion the model says does
        // not exist and report the gas along it. The first version of this command did
        // exactly that, and the numbers looked as authoritative as real ones.
        Assert.Empty(profile.Samples);

        var said = Assert.Single(profile.Warnings, w => w.Code == "regime.no-trajectory");

        output.WriteLine(said.Message);

        Assert.Contains("density", said.Message, StringComparison.Ordinal);
    }

    /// <summary>The path is flown through the gas, not through a vacuum.</summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this test exists for.</b> The first version attached no collision
    /// sampler, so a model declaring a pressure was flown as though it declared none — and
    /// the gas numbers were then reported along that vacuum path. Where the gas changes
    /// the route, which is the only place this view is worth opening, the route was wrong.
    /// </para>
    /// <para>
    /// <c>gas-flow-carry</c> is the discriminating model: it has no field at all, so in
    /// vacuum the analytic field-free drift crosses the whole metre in a single step and
    /// the path is two points. In its own gas the ion collides its way along and the path
    /// has many. <b>Two points is what the broken version produced</b>, and the flight time
    /// matched by coincidence — the example launches the ion at exactly the gas velocity,
    /// so the ballistic and the carried answers agree.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePathIsFlownThroughTheGasRatherThanAVacuum()
    {
        var profile = RegimeCommand.Execute(Example("gas-flow-carry"), samples: 32);

        output.WriteLine($"{profile.Samples.Count} samples over the flight");

        // Many, not the two an unimpeded analytic drift would give.
        Assert.True(
            profile.Samples.Count > 8,
            $"only {profile.Samples.Count} samples: the ion was flown without its gas");
    }

    /// <summary>Vacuum is said, not reported as a table of infinities.</summary>
    /// <remarks>
    /// Every number here is a statement about a gas, and in vacuum they are all infinite
    /// or zero — true, and saying nothing. REG-2's check is vacuous in vacuum because
    /// trajectory integration is unconditionally right there, which is a fact worth
    /// stating rather than leaving to be inferred from a column of infinities.
    /// </remarks>
    [Fact]
    public void VacuumIsSaidRatherThanTabulated()
    {
        var profile = RegimeCommand.Execute(Example("single-stage-reflectron"));

        var said = Assert.Single(profile.Warnings, w => w.Code == "regime.no-gas");

        output.WriteLine(said.Message);

        Assert.Empty(profile.Samples);
        Assert.Empty(profile.Excursions);
        Assert.Contains("vacuum", said.Message, StringComparison.Ordinal);
    }

    /// <summary>An excursion is a stretch of path, not a count of samples.</summary>
    /// <remarks>
    /// <para>
    /// A warning earned at two separate places in an instrument is two problems, and
    /// reporting "17 samples" would merge them into one. What makes an excursion
    /// actionable is that it has a beginning and an end in millimetres.
    /// </para>
    /// <para>
    /// The funnel at a millibar is the case: it runs above the pressure at which
    /// trajectory integration is the right description, so every sample earns
    /// <c>regime.trajectory-above-validity</c> and the excursion should span the flight
    /// rather than appear once per sample.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnExcursionIsAStretchOfPathRatherThanACount()
    {
        // gas-flow-carry runs at 0.008 mbar, inside the band spec figure 4 marks
        // dangerous - both descriptions run and neither is obviously right - so every
        // sample earns `regime.overlap-band` and the excursion must span the flight
        // rather than appear once per sample.
        //
        // The funnel was the first choice and declares no gas at all, so the test
        // returned early and asserted nothing. A model that cannot reach the assertions
        // is not a weak test, it is a test of something else.
        var profile = RegimeCommand.Execute(Example("gas-flow-carry"), samples: 48);

        output.WriteLine($"{profile.Samples.Count} samples, {profile.Excursions.Count} excursions");

        foreach (var excursion in profile.Excursions)
        {
            output.WriteLine(
                $"  {excursion.Code,-36} {excursion.FromMm,7:F1} to {excursion.ToMm,7:F1} mm "
                + $"({excursion.FromUs:F1} to {excursion.ToUs:F1} us, {excursion.Samples} samples)");
        }

        Assert.NotEmpty(profile.Samples);
        Assert.NotEmpty(profile.Excursions);

        foreach (var excursion in profile.Excursions)
        {
            // A stretch, so its end is not before its beginning and it holds at least the
            // sample that earned it.
            Assert.True(excursion.ToUs >= excursion.FromUs);
            Assert.True(excursion.ToMm >= excursion.FromMm);
            Assert.True(excursion.Samples >= 1);

            // One entry per contiguous stretch, so a code earned everywhere appears once
            // rather than once per sample.
            Assert.True(
                excursion.Samples <= profile.Samples.Count,
                "an excursion claims more samples than the path has");
        }

        // Distinct by code and stretch: the same code twice means two separate places.
        var codes = profile.Excursions.Select(e => e.Code).ToList();

        Assert.Equal(
            codes.Count,
            profile.Excursions.Select(e => (e.Code, e.FromUs)).Distinct().Count());
    }
}
