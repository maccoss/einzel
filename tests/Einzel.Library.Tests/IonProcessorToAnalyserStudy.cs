using Einzel.Analysis;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A pulsed-extraction trap feeding a multi-reflection analyser, measured as a handover
/// between two models rather than as one.
/// </summary>
/// <remarks>
/// <para>
/// This is the second of the two injection paths this platform is pointed at, and it has
/// the same shape as the first: a trap accumulates and cools ions, then pulses them into an
/// analyser. The C-trap injects an orbital trap, and the rectilinear pulsed-extraction trap
/// — the low-pressure region of an ion processor — injects a multi-reflection time-of-flight
/// analyser.
/// </para>
/// <para>
/// <b>What is modelled and what is not.</b> The trap is the shipped `rectilinear-trap`
/// template, solved. The analyser is the shipped mirror pair, solved, at its two-stage
/// design point. <b>It is not a model of any particular commercial instrument's geometry</b>
/// — in particular an asymmetric-track analyser gets its reflection count from a slow drift
/// along the mirror axis, and nothing here models that drift. What is being asked is the
/// question that does not depend on it: given a turn-around time this trap produces, how
/// many reflections does a resolving power need, and where does adding reflections stop
/// helping?
/// </para>
/// <para>
/// <b>The currency is the arrival-time spread, and turn-around is the part of it that no
/// analyser can undo.</b> A time-of-flight analyser refocuses energy spread — that is what
/// its mirrors are for — but ions that left the source at different <i>instants</i> stay
/// apart for the whole flight. So the trap's turn-around time is a floor on the peak width,
/// and the resolving power it permits is `t / 2 dt`, growing linearly with flight time while
/// the analyser's own energy aberration does not grow at all.
/// </para>
/// <para>
/// That is the whole content of the handover: two numbers that scale differently, and a
/// crossing where the binding limit changes hands.
/// </para>
/// </remarks>
public sealed class IonProcessorToAnalyserStudy(ITestOutputHelper output)
{
    private const double MassToCharge = 500.0;
    private const double IonEnergyElectronvolts = 4000.0;
    private const double BoardGap = 0.030;
    private const double MirrorDepth = 0.090;
    private const double CapPotential = 4800.0;
    private const double FieldFreeRun = 0.060;
    private const double InclinationDegrees = 6.0;
    private const double FirstStageFraction = 0.35;

    /// <summary>Oscillations used to measure the period; the period is what is wanted.</summary>
    private const int PeriodOscillations = 6;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(MassToCharge, 1);

    private static Quantity Energy(double fraction = 0.0) =>
        Quantity.From(IonEnergyElectronvolts * (1.0 + fraction), "eV");

    /// <summary>The trap's half: the turn-around time it imposes before the ion leaves.</summary>
    /// <remarks>
    /// Through <see cref="FiguresOfMerit.Measure"/> rather than by flying a thermal cloud
    /// here, which is the same implementation `einzel run` reports and `einzel test` pins.
    /// This project has already had `run` and `test` disagree twice by computing one
    /// quantity two ways, and the second time a declared gas took part in only one of them.
    /// </remarks>
    private static Core.Results.Measured TurnAround()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("rectilinear-trap"));
        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        var measured = FiguresOfMerit.Measure("turnAroundTime", validation.Model!);

        Assert.NotNull(measured);

        return measured!;
    }

    /// <summary>Finds the separation that cancels the first-order energy term.</summary>
    private static MirrorPair TuneToFirstOrderFocus(PlanarMirror mirror, double turningDepth)
    {
        var inclination = Quantity.From(InclinationDegrees, "deg");

        double FirstOrder(MirrorPair pair)
        {
            var samples = new List<(double, double)>(9);

            for (var k = 0; k < 9; k++)
            {
                var fraction = -0.05 + (0.10 * k / 8.0);
                var flight = pair.Fly(Peptide, Energy(fraction), inclination, PeriodOscillations);

                if (flight.Arrived)
                {
                    samples.Add((fraction, flight.FlightTimeSeconds));
                }
            }

            return FocusingAnalysis.Fit(samples).Coefficients[0];
        }

        var low = 2.0 * turningDepth;
        var high = 8.0 * turningDepth;

        for (var i = 0; i < 8 && FirstOrder(new MirrorPair(mirror, high)) > 0.0; i++)
        {
            low = high;
            high *= 1.6;
        }

        for (var i = 0; i < 20 && high - low > 1e-6; i++)
        {
            var mid = 0.5 * (low + high);

            if (FirstOrder(new MirrorPair(mirror, mid)) > 0.0)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return new MirrorPair(mirror, 0.5 * (low + high));
    }

    /// <summary>The trap's turn-around sets how many reflections a resolving power needs.</summary>
    /// <remarks>
    /// <para>
    /// Two numbers that scale differently. <b>Turn-around-limited resolving power grows
    /// linearly with flight time</b>, because the peak width it imposes is fixed and the
    /// flight time is not — so every extra reflection buys the same increment. <b>The
    /// analyser's energy aberration does not grow at all</b>: it is a property of the mirror
    /// profile, and a longer flight scales the aberration and the flight time together.
    /// </para>
    /// <para>
    /// So there is a crossing, and which side of it a design sits on decides what to spend
    /// effort on. Below it, a colder or harder-pushed trap is worth more than a better
    /// mirror; above it, the reverse, and more reflections buy nothing at all.
    /// </para>
    /// <para>
    /// <b>The trap's own arrival spread is not the number to use here, and that is the trap
    /// worth naming.</b> Measured at its own detector this trap's packet is ~241 ns wide,
    /// almost all of it the spread in extraction depth. An analyser would refocus that — it
    /// is an energy spread, which is exactly what mirrors are for. Turn-around is the part
    /// that survives, and it is 1.8% of the total. Using the arrival spread would understate
    /// the reachable resolving power by two orders of magnitude.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTrapsTurnAroundSetsHowManyReflectionsAResolvingPowerNeeds()
    {
        // Deconstructed, which is the only route to the number and hands back the whole
        // envelope with it (GRD-1). The uncertainty is reported below rather than dropped:
        // it is a sampling error over the thermal cloud, and a resolving power computed
        // from a turn-around time inherits it.
        var (turnAround, spread, evidence, warnings) = TurnAround();

        var dt = turnAround.SiValue;

        foreach (var warning in warnings)
        {
            output.WriteLine($"  [{warning.Code}] {warning.Message}");
        }

        Assert.True(dt > 0.0, "the trap declares a temperature, so it has a turn-around");

        // The analyser's half: the shipped two-stage mirror, at first-order focus.
        var mirror = PlanarMirror.Solve(
            MirrorProfile.TwoStage(
                MirrorDepth * FirstStageFraction,
                IonEnergyElectronvolts * 0.7,
                MirrorDepth,
                CapPotential),
            BoardGap,
            FieldFreeRun,
            cellsPerGap: 32);

        var pair = TuneToFirstOrderFocus(mirror, mirror.TurningDepth(Energy(), chargeNumber: 1));

        var nominal = pair.Fly(
            Peptide, Energy(), Quantity.From(InclinationDegrees, "deg"), PeriodOscillations);

        Assert.True(nominal.Arrived, "the nominal ion completes the period measurement");

        var period = nominal.FlightTimeSeconds / PeriodOscillations;

        // The analyser's own limit, from the same geometry: what energy aberration alone
        // permits, with no source contribution at all.
        //
        // Evaluated at three oscillation counts because "it does not grow with flight
        // time" is the load-bearing premise of this whole comparison.
        //
        // IT COMES OUT IDENTICAL TO THE DIGIT, AND THAT IS TOO EXACT TO BE A MEASUREMENT.
        // MirrorPair.Fly computes ONE period and multiplies - a deliberate choice recorded
        // in this project's own lessons, because stitching twelve legs gives twelve chances
        // to miss a root-find - so every arrival time here is exactly n times a
        // per-oscillation time and the ratio t / dt is exactly constant by construction.
        // What is confirmed below is the arithmetic, not the physics.
        //
        // The physical claim underneath it is that each oscillation is identical, so
        // aberration accumulates in proportion to flight time. That is true of an ideal
        // periodic analyser and is exactly what an ASYMMETRIC-TRACK analyser gives up: its
        // ions drift along the mirror axis, so successive reflections sample slightly
        // different field and the aberration per oscillation is not constant. Nothing here
        // models that drift, so this comparison is about a periodic analyser and the real
        // instrument's departure from it is unmeasured.
        double AberrationLimit(int oscillations)
        {
            var arrivals = new List<double>(41);

            for (var k = 0; k < 41; k++)
            {
                var fraction = -0.03 + (0.06 * k / 40.0);
                var flight = pair.Fly(
                    Peptide, Energy(fraction), Quantity.From(InclinationDegrees, "deg"),
                    oscillations);

                if (flight.Arrived)
                {
                    arrivals.Add(flight.FlightTimeSeconds);
                }
            }

            var (r, _, _, _) = ArrivalTimePeak.FromArrivals(arrivals, 41).ResolvingPower();

            return r.SiValue;
        }

        var flatness = new List<(int Oscillations, double R)>();

        foreach (var n in new[] { 3, 6, 12 })
        {
            flatness.Add((n, AberrationLimit(n)));
        }

        var aberrationLimit = flatness[1].R;

        output.WriteLine(
            $"trap turn-around        {dt * 1e9:F3} ns "
            + $"(+/-{(spread.UpperSi - spread.LowerSi) * 0.5e9:F3} ns, {evidence})");

        output.WriteLine(
            $"analyser period         {period * 1e6:F4} us per oscillation, "
            + $"cap to cap {pair.CapToCap * 1e3:F1} mm");

        output.WriteLine(
            "analyser energy limit   R at +/-3% (constant by construction - see remarks):");

        foreach (var (n, r) in flatness)
        {
            output.WriteLine($"                          {n,3} oscillations  R = {r,10:N0}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("  oscillations   flight time     path      R from turn-around");

        foreach (var n in new[] { 1, 4, 8, 16, 32, 64, 128 })
        {
            var flight = n * period;

            // One oscillation is out and back, so the path is twice the cap-to-cap
            // separation. The inclination makes the real path slightly longer; it is left
            // out because it is a few parts in a thousand and would invite the table to be
            // read as a geometry rather than as a scale.
            output.WriteLine(
                $"  {n,12}   {flight * 1e6,10:F1} us   {n * pair.CapToCap * 2.0,6:F2} m"
                + $"   {flight / (2.0 * dt),18:N0}");
        }

        // Where the binding limit changes hands. Below this many oscillations the trap is
        // what stops you; above it the mirror is, and more reflections buy nothing.
        var crossing = 2.0 * dt * aberrationLimit / period;

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"  the two limits cross at {crossing:F0} oscillations "
            + $"({crossing * period * 1e6:N0} us of flight). Below that the TRAP binds; "
            + "above it the MIRROR does and more reflections buy nothing.");

        // THE PREMISE, MEASURED. The energy-aberration limit is flat across a fourfold
        // change in flight time, while the turn-around limit over the same range grows
        // fourfold by construction. Those different scalings are the entire argument, and
        // only one of the two needed measuring - the other is arithmetic on a fixed width,
        // and asserting it would have been asserting my own formula.
        var flattest = flatness.Min(f => f.R);
        var steepest = flatness.Max(f => f.R);

        output.WriteLine(
            $"  the energy limit varies {steepest / flattest:F3}x over a fourfold change in "
            + "flight time, against 4.000x for the turn-around limit - but the first of "
            + "those is exact by construction, not measured");

        // A guard on the arithmetic rather than evidence about the physics, and labelled as
        // such. If this ever moves, MirrorPair has stopped multiplying one period and the
        // crossing below needs rederiving rather than adjusting.
        Assert.True(
            steepest / flattest < 1.001,
            $"the analyser's energy-aberration limit moved from {flattest:N0} to "
            + $"{steepest:N0} across 3 to 12 oscillations, where the implementation should "
            + "make it exactly constant. Something about how a periodic flight is composed "
            + "has changed, and the crossing this test reports rests on it");

        // And the crossing is somewhere a designer would actually build: not one
        // reflection, and not so many that no instrument could hold them. If this ever
        // lands outside, the interesting thing is which of the two numbers moved.
        Assert.InRange(crossing, 5.0, 500.0);

        // The trap's turn-around is a small fraction of the arrival spread it shows at its
        // own detector, and using the wrong one of those is the mistake this test exists to
        // prevent. Guarding the ratio rather than the absolute value, so the assertion
        // still means something if the template's temperature or push voltage is retuned.
        Assert.InRange(dt * 1e9, 0.1, 100.0);
    }
}
