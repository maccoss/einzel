using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The Mathieu stability boundary, recovered by integration.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 19 calls this "the best single test that the RF path is correct",
/// and it earns the description. An ideal quadrupole turns Newton's equation into
/// the Mathieu equation, whose stable and unstable regions are known exactly and
/// have been tabulated since the nineteenth century. Nothing about the answer
/// comes from this code, and almost nothing about the code fails to show up in it:
/// a stage evaluated at the wrong instant, a step that skips a phase, a sign error
/// in the field, or a factor of two in the a-q mapping all move the boundary.
/// </para>
/// <para>
/// The number being recovered is <b>q = 0.90804</b> at a = 0, the first boundary
/// of the first stability region. Below it an ion oscillates about the axis
/// forever; above it the amplitude grows without bound and the ion is lost.
/// </para>
/// </remarks>
public sealed class MathieuStabilityTests(ITestOutputHelper output)
{
    /// <summary>The tabulated first stability boundary at a = 0.</summary>
    private const double BoundaryAtZeroA = 0.90804;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>
    /// Whether an ion launched slightly off axis stays bounded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stability is an asymptotic property, so it is tested by flying for many
    /// cycles and asking whether the excursion ever exceeds the aperture. Near the
    /// boundary the growth is slow, which is exactly why the cycle count sets how
    /// finely the boundary can be located - not the integrator tolerance.
    /// </para>
    /// <para>
    /// Launched off axis in both x and y, because the two are unstable in
    /// different places and an ion started on one axis would be blind to the
    /// other's boundary.
    /// </para>
    /// </remarks>
    private static bool IsStable(double a, double q, int cycles, out double excursion)
    {
        var species = Peptide;
        var radius = Quantity.From(4.0, "mm");
        var frequency = Quantity.From(1.0, "MHz");

        var field = IdealQuadrupoleRf.FromMathieu(
            a, q, species.Mass(), species.Charge(), frequency, radius);

        var start = new PhaseState(
            new Vec3(0.1e-3, 0.1e-3, 0.0),
            new Vec3(0.0, 0.0, 0.0));

        var flight = cycles * field.ShortestPeriodSeconds;
        var aperture = radius.In("m");

        // The flight ends when the ion leaves the aperture; an ion that survives
        // to the time limit stayed in.
        TrajectoryStopFunction inside = (in PhaseState s) =>
            aperture - Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Y));

        var recorder = new TrajectoryRecorder(field.ShortestPeriodSeconds / 4.0);

        var result = TrajectoryIntegrator.Integrate(
            start,
            species,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-9, MaximumFlightTime = flight },
            inside,
            recorder);

        excursion = 0.0;

        foreach (var sample in recorder.Samples)
        {
            excursion = Math.Max(
                excursion, Math.Max(Math.Abs(sample.Position.X), Math.Abs(sample.Position.Y)));
        }

        return result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached;
    }

    [Theory]
    [InlineData(0.4, true)]
    [InlineData(0.7, true)]
    [InlineData(0.85, true)]
    [InlineData(0.95, false)]
    [InlineData(1.2, false)]
    [InlineData(2.0, false)]
    public void StabilityAtZeroDcFallsWhereMathieuSaysItDoes(double q, bool expected)
    {
        var stable = IsStable(0.0, q, cycles: 200, out var excursion);

        output.WriteLine(
            $"q = {q:F3}: {(stable ? "stable" : "lost")}, excursion {excursion * 1e3:F3} mm "
            + $"(boundary is {BoundaryAtZeroA})");

        Assert.Equal(expected, stable);
    }

    [Fact]
    public void TheBoundaryIsRecoveredToTheClassBBudget()
    {
        // ACC-6 asks for a boundary resolved to one part in five hundred of the
        // scan. Bisection on stability over a scan of 0.4 to 1.4 makes that
        // 0.002 in q, which is well inside what distinguishes the tabulated
        // 0.90804 from anything else.
        const double Low = 0.4;
        const double High = 1.4;
        var tolerance = (High - Low) / 500.0;

        var low = Low;
        var high = High;

        Assert.True(IsStable(0.0, low, 200, out _), "the scan should start inside the stable region");
        Assert.False(IsStable(0.0, high, 200, out _), "the scan should end outside it");

        var probes = 0;

        while (high - low > tolerance)
        {
            var mid = 0.5 * (low + high);

            if (IsStable(0.0, mid, 200, out _))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }

            probes++;
        }

        var boundary = 0.5 * (low + high);
        var error = Math.Abs(boundary - BoundaryAtZeroA);

        output.WriteLine($"scan {Low} to {High}, ACC-6 resolution {tolerance:F4} in q");
        output.WriteLine($"boundary found at q = {boundary:F5} in {probes} probes");
        output.WriteLine($"tabulated        q = {BoundaryAtZeroA}");
        output.WriteLine($"difference       {error:F5} ({error / tolerance:F2} of the ACC-6 resolution)");

        // Within the resolution the scan can distinguish. Asking for better would
        // be asking the bisection to resolve a boundary it did not sample.
        Assert.True(
            error <= tolerance,
            $"the boundary came out at {boundary:F5} against a tabulated {BoundaryAtZeroA}");
    }

    [Fact]
    public void ADcComponentNarrowsTheStableRange()
    {
        // The shape of the diagram, not just one point of it. Adding DC lifts the
        // working point toward the apex at a = 0.237, so the stable range in q
        // shrinks from both sides - which is the whole principle of a mass filter:
        // hold a and q on the ratio that leaves one mass inside the tip.
        output.WriteLine("    a    stable q from    to");

        foreach (var a in new[] { 0.0, 0.1, 0.2 })
        {
            var first = double.NaN;
            var last = double.NaN;

            for (var q = 0.05; q <= 1.0; q += 0.01)
            {
                if (IsStable(a, q, 120, out _))
                {
                    if (double.IsNaN(first))
                    {
                        first = q;
                    }

                    last = q;
                }
            }

            output.WriteLine($"{a,5:F2} {first,15:F3} {last,7:F3}");

            if (a == 0.0)
            {
                // With no DC every small q is stable: there is nothing to defocus
                // the ion on average.
                Assert.True(first < 0.1, $"a = 0 should be stable at small q; first stable was {first:F3}");
            }
            else
            {
                // With DC the x or y motion is defocused until the RF is strong
                // enough to hold it, so small q is lost.
                Assert.True(first > 0.1, $"a = {a} should be unstable at small q; first stable was {first:F3}");
            }

            Assert.True(last < BoundaryAtZeroA + 0.05, "the upper edge should not exceed the a = 0 boundary");
        }
    }
}

/// <summary>
/// What the RF path costs and what it gives up.
/// </summary>
public sealed class RfIntegrationTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static (IdealQuadrupoleRf Field, PhaseState Start) Setup(double q)
    {
        var species = Peptide;

        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, q, species.Mass(), species.Charge(),
            Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"));

        return (field, new PhaseState(new Vec3(0.2e-3, 0.0, 0.0), new Vec3(0.0, 0.0, 0.0)));
    }

    [Fact]
    public void EnergyDriftIsNotReportedForADrivenField()
    {
        // A driven field does work on the ion deliberately and continuously, so
        // there is no conserved quantity to check against. Reporting the change
        // against the t = 0 potential would give a figure that looks like a
        // diagnostic, moves when the physics moves, and means nothing.
        var (field, start) = Setup(0.4);

        var result = TrajectoryIntegrator.Integrate(
            start,
            Peptide,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = 20.0 * field.ShortestPeriodSeconds,
            });

        output.WriteLine($"energy drift reported as {result.MaximumRelativeEnergyDrift}");

        Assert.True(
            double.IsNaN(result.MaximumRelativeEnergyDrift),
            "a driven field has no energy invariant, so a number here would be a false diagnostic");
    }

    [Fact]
    public void TheStepIsCappedByTheDrivePeriod()
    {
        // The same lesson a gridded field taught, in a different variable. An
        // embedded error estimate compares two solutions of the problem it was
        // given; if every stage of a step lands on the same phase of the cycle,
        // both agree and the step is accepted as accurate. It was accurate for the
        // field it was shown, and it was not shown the field.
        var (field, start) = Setup(0.4);
        const int Cycles = 40;

        var result = TrajectoryIntegrator.Integrate(
            start,
            Peptide,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = Cycles * field.ShortestPeriodSeconds,
            });

        var perCycle = (double)result.AcceptedSteps / Cycles;
        output.WriteLine($"{result.AcceptedSteps} steps over {Cycles} cycles: {perCycle:F1} per cycle");

        // At least twenty, which is the cap. The controller may ask for more and
        // is never allowed fewer.
        Assert.True(perCycle >= 20.0, $"only {perCycle:F1} steps per RF cycle; the drive was not resolved");
    }

    [Fact]
    public void AStaticFieldIsUnchangedToTheLastBit()
    {
        // Threading time through every Dormand-Prince stage must cost a static
        // field nothing. Time enters as an added zero and a type test that always
        // fails the same way, so a reflectron integrated today has to agree with
        // one integrated before RF existed - exactly, not nearly.
        var species = Peptide;
        var field = UniformField.Create(new Vec3(-1000.0, 0.0, 0.0));
        var speed = species.SpeedAfterAcceleration(Quantity.From(2.0, "kV")).In("m/s");
        var start = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(speed, 0.0, 0.0));

        TrajectoryStopFunction detector = (in PhaseState s) => 0.05 - s.Position.X;

        var settings = new IntegrationSettings { RelativeTolerance = 1e-12, MaximumFlightTime = 1e-3 };
        var result = TrajectoryIntegrator.Integrate(start, species, field, settings, detector);

        // A uniform decelerating field with a known closed form: x = vt - at^2/2.
        var acceleration = Math.Abs(species.ChargeSi) * 1000.0 / species.MassSi;
        var exact = (speed - Math.Sqrt((speed * speed) - (2.0 * acceleration * 0.05))) / acceleration;

        var error = Math.Abs(result.FlightTimeSeconds - exact) / exact;
        output.WriteLine($"{result.FlightTimeSeconds * 1e6:F9} us against {exact * 1e6:F9}, off by {error:E3}");

        Assert.True(error < 1e-12, $"the static path moved by {error:E3} when time was threaded through it");

        // And it still has an energy diagnostic, because it still has an invariant.
        Assert.False(double.IsNaN(result.MaximumRelativeEnergyDrift));
    }
}

/// <summary>
/// A rectangular drive, against Schrader, Anderson and Russell (JASMS 2024).
/// </summary>
/// <remarks>
/// <para>
/// "Increasing Isolation Efficiency Using a Segmented Quadrupole Mass Filter
/// Operated with Rectangular Waveforms", J. Am. Soc. Mass Spectrom. 35 (2024)
/// 1237-1244. A switching drive rather than a resonant one, which changes the
/// equation of motion from Mathieu's to Meissner's and moves the stability
/// boundaries with it.
/// </para>
/// <para>
/// Two numbers from the paper are checkable here and neither comes from this code.
/// The low-mass cut-off moves from q = 0.908 to <b>q = 0.712</b>. And an
/// asymmetric duty cycle supplies its own a: at 61.15/38.85 and q = 0.5897 the
/// paper quotes a = -0.2640, which is the waveform's mean doing the job a DC
/// supply would.
/// </para>
/// <para>
/// The authors simulated this in SIMION 8.1. That is the project thesis restated
/// as a fact for the second time in these targets - the Ion Processor was
/// simulated in an in-house package nobody outside the group can run, and this one
/// in software behind a licence.
/// </para>
/// </remarks>
public sealed class RectangularWaveformTests(ITestOutputHelper output)
{
    /// <summary>The square-wave low-mass cut-off the paper quotes.</summary>
    private const double SquareBoundary = 0.712;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static bool IsStable(double a, double q, RfWaveform waveform, int cycles)
    {
        var species = Peptide;
        var radius = Quantity.From(4.0, "mm");

        var field = IdealQuadrupoleRf.FromMathieu(
            a, q, species.Mass(), species.Charge(), Quantity.From(1.0, "MHz"), radius, waveform);

        var start = new PhaseState(new Vec3(0.1e-3, 0.1e-3, 0.0), new Vec3(0.0, 0.0, 0.0));
        var aperture = radius.In("m");

        TrajectoryStopFunction inside = (in PhaseState s) =>
            aperture - Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Y));

        var result = TrajectoryIntegrator.Integrate(
            start,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = cycles * field.ShortestPeriodSeconds,
            },
            inside);

        return result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached;
    }

    [Fact]
    public void ASquareWaveMovesTheCutOffToSevenOneTwo()
    {
        // The headline check. A switching drive is not a cosmetic change to a
        // sinusoidal one: the boundary moves by more than twenty per cent, and an
        // implementation that treated the waveform as a detail would land on 0.908
        // and look fine.
        const double Low = 0.4;
        const double High = 1.0;
        var tolerance = (High - Low) / 500.0;

        var waveform = new RfWaveform.Rectangular(0.5);
        var low = Low;
        var high = High;

        Assert.True(IsStable(0.0, low, waveform, 200), "the scan should start inside the stable region");
        Assert.False(IsStable(0.0, high, waveform, 200), "the scan should end outside it");

        while (high - low > tolerance)
        {
            var mid = 0.5 * (low + high);

            if (IsStable(0.0, mid, waveform, 200))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var boundary = 0.5 * (low + high);

        output.WriteLine($"square wave cut-off  q = {boundary:F5}");
        output.WriteLine($"paper quotes         q = {SquareBoundary}");
        output.WriteLine($"sinusoid, for scale  q = 0.90804");
        output.WriteLine($"difference           {Math.Abs(boundary - SquareBoundary):F5}");

        // The paper quotes three figures, so three figures is what can be checked.
        Assert.True(
            Math.Abs(boundary - SquareBoundary) < 0.005,
            $"square-wave cut-off came out at {boundary:F5} against a published {SquareBoundary}");
    }

    [Fact]
    public void AnAsymmetricDutyCycleSuppliesItsOwnDc()
    {
        // The trick that makes a digital filter work without a DC supply. The
        // paper's prefilter runs at 61.15/38.85 and q = 0.5897, and quotes
        // a = -0.2640; the mean of the waveform is 2d - 1, which enters the
        // equation of motion exactly where a DC offset would.
        var species = Peptide;
        const double Duty = 0.6115;
        const double Q = 0.5897;

        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, Q, species.Mass(), species.Charge(),
            Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"),
            new RfWaveform.Rectangular(Duty));

        var a = field.MathieuA(species.Mass(), species.Charge());
        var q = field.MathieuQ(species.Mass(), species.Charge());

        output.WriteLine($"duty {Duty:P2} at q = {q:F4} gives a = {a:F4}");
        output.WriteLine($"paper quotes a = -0.2640 for the same working point (sign by pair)");

        Assert.Equal(Q, q, 1e-9);

        // a = 2q(2d - 1), which for this duty and q is 0.263.
        Assert.Equal(2.0 * Q * ((2.0 * Duty) - 1.0), a, 1e-9);
        Assert.Equal(0.2640, Math.Abs(a), 0.002);
    }

    [Fact]
    public void ABalancedSquareWaveHasNoMeanAndAnOffsetOneDoes()
    {
        // The mechanism, isolated from any trajectory. If the mean were wrong the
        // stability results would be wrong by a shifted working point and would
        // still look self-consistent.
        Assert.Equal(0.0, new RfWaveform.Sinusoid().Mean);
        Assert.Equal(0.0, new RfWaveform.Rectangular(0.5).Mean, 1e-15);
        Assert.Equal(0.223, new RfWaveform.Rectangular(0.6115).Mean, 1e-3);

        // And the shape itself: high for the duty fraction, low for the rest.
        var wave = new RfWaveform.Rectangular(0.6115);

        Assert.Equal(1.0, wave.At(0.0));
        Assert.Equal(1.0, wave.At(0.61));
        Assert.Equal(-1.0, wave.At(0.62));
        Assert.Equal(-1.0, wave.At(0.99));

        // Phase arrives from an accumulated flight time and will not be tidy.
        Assert.Equal(wave.At(0.3), wave.At(7.3), 1e-15);
    }
}
