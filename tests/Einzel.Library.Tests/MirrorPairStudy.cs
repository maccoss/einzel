using Einzel.Analysis;
using Einzel.Core.Units;
using Einzel.Transport;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The companion memo's section 6 item 1, run: a mirror pair at its design point,
/// asked whether it reaches 20,000 across the energy acceptance it wants.
/// </summary>
/// <remarks>
/// The memo is one customer of a general tool, not the tool's purpose. Nothing
/// exercised below is specific to it — a piecewise-linear mirror profile, a
/// solved field, a periodic flight, and the Class T figures of merit. What is
/// specific is the numbers, and those live in this test rather than in the engine.
/// </remarks>
public sealed class MirrorPairStudy(ITestOutputHelper output)
{
    private const double MassToCharge = 500.0;
    private const double IonEnergyElectronvolts = 4000.0;
    private const double BoardGap = 0.030;
    private const double MirrorDepth = 0.090;
    private const double CapPotential = 4800.0;
    private const double FieldFreeRun = 0.060;
    private const double InclinationDegrees = 6.0;
    private const int Oscillations = 6;

    /// <summary>The memo's target for the analyzer.</summary>
    private const double TargetResolvingPower = 20000.0;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(MassToCharge, 1);

    private static Quantity Energy(double fraction = 0.0) =>
        Quantity.From(IonEnergyElectronvolts * (1.0 + fraction), "eV");

    private static List<double> Scan(MirrorPair pair, double span, int points)
    {
        var inclination = Quantity.From(InclinationDegrees, "deg");
        var arrivals = new List<double>(points);

        for (var k = 0; k < points; k++)
        {
            var fraction = -span + (2.0 * span * k / (points - 1));
            var flight = pair.Fly(Peptide, Energy(fraction), inclination, Oscillations);

            if (flight.Arrived)
            {
                arrivals.Add(flight.FlightTimeSeconds);
            }
        }

        return arrivals;
    }

    private static FocusingOrder FocusOf(MirrorPair pair, double span = 0.05, int points = 9)
    {
        var inclination = Quantity.From(InclinationDegrees, "deg");
        var samples = new List<(double, double)>(points);

        for (var k = 0; k < points; k++)
        {
            var fraction = -span + (2.0 * span * k / (points - 1));
            var flight = pair.Fly(Peptide, Energy(fraction), inclination, Oscillations);

            if (flight.Arrived)
            {
                samples.Add((fraction, flight.FlightTimeSeconds));
            }
        }

        return FocusingAnalysis.Fit(samples);
    }

    /// <summary>Finds the separation that cancels the first-order energy term.</summary>
    /// <remarks>
    /// Four penetration depths is the answer for a single linear stage only. A
    /// two-stage profile divides its time differently between drift and mirror, so
    /// its first-order focus sits elsewhere and has to be found rather than
    /// assumed. A one-parameter search, done by hand here so the stage does not
    /// depend on the optimiser in Einzel.Sweeps, which does not exist yet.
    /// </remarks>
    private static (MirrorPair Pair, FocusingOrder Focus) TuneToFirstOrderFocus(
        PlanarMirror mirror, double turningDepth)
    {
        var low = 2.0 * turningDepth;
        var high = 8.0 * turningDepth;

        // Four penetration depths brackets the root for one linear stage, but a
        // two-stage profile spends far more of its period inside the mirror and
        // needs a longer drift to balance it — so the bracket is grown until it
        // actually contains a sign change rather than assumed to. Without this the
        // bisection silently converges on its own ceiling and returns a geometry
        // that is not at focus at all, which reads as a bad mirror rather than as
        // a failed search.
        for (var i = 0; i < 8 && FocusOf(new MirrorPair(mirror, high)).Coefficients[0] > 0.0; i++)
        {
            low = high;
            high *= 1.6;
        }

        // More drift means a fast ion loses more time between mirrors, which is
        // what cancels the time it gains inside one, so c1 falls monotonically
        // with separation and bisection is enough.
        for (var i = 0; i < 20 && high - low > 1e-6; i++)
        {
            var mid = 0.5 * (low + high);

            if (FocusOf(new MirrorPair(mirror, mid)).Coefficients[0] > 0.0)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var pair = new MirrorPair(mirror, 0.5 * (low + high));
        return (pair, FocusOf(pair));
    }

    private void Report(string label, MirrorPair pair, FocusingOrder focus)
    {
        var inclination = Quantity.From(InclinationDegrees, "deg");
        var nominal = pair.Fly(Peptide, Energy(), inclination, Oscillations);

        output.WriteLine(label);
        output.WriteLine($"  cap to cap         {pair.CapToCap * 1e3:F2} mm");
        output.WriteLine(
            $"  flight time        {nominal.FlightTimeSeconds * 1e6:F4} us over {Oscillations} oscillations");
        output.WriteLine($"  drift needed       {nominal.DriftDistanceMetres * 1e3:F1} mm at {InclinationDegrees:F0} deg");
        output.WriteLine($"  energy drift       {nominal.EnergyDrift:E2} (ACC-4 budget 1e-6)");
        output.WriteLine(
            $"  focusing           c1 {focus.Coefficients[0]:E3}  c2 {focus.Coefficients[1]:E3}  "
            + $"c3 {focus.Coefficients[2]:E3}");
        output.WriteLine($"  binding order      {focus.BindingOrder}  (fit residual {focus.ResidualOfFit:E2})");

        foreach (var span in new[] { 0.03, 0.05 })
        {
            var peak = ArrivalTimePeak.FromArrivals(Scan(pair, span, points: 41), 41);
            var (r, uncertainty, _, _) = peak.ResolvingPower();
            var half = (uncertainty.UpperSi - uncertainty.LowerSi) / 2.0;

            output.WriteLine(
                $"  +/-{span:P0} energy    R = {r.SiValue,8:F0} +/- {half,-6:F0}  "
                + $"half-width {peak.CentralWidthSeconds(0.5) * 1e9,8:F2} ns  "
                + $"transmission {(double)peak.Arrived / peak.Launched:P0}");
        }
    }

    [Fact]
    public void ASingleStageMirrorPairFallsWellShortOfTwentyThousand()
    {
        var mirror = PlanarMirror.Solve(
            MirrorProfile.SingleStage(MirrorDepth, CapPotential), BoardGap, FieldFreeRun, cellsPerGap: 32);

        var depth = mirror.TurningDepth(Energy(), chargeNumber: 1);
        var (pair, focus) = TuneToFirstOrderFocus(mirror, depth);

        output.WriteLine(
            $"turning depth {depth * 1e3:F3} mm from the solve; four depths would be {4.0 * depth * 1e3:F2} mm");
        output.WriteLine(string.Empty);

        Report("SINGLE-STAGE", pair, focus);

        Assert.True(
            Math.Abs(focus.Coefficients[0]) < 5e-3,
            $"first-order focus not reached: c1 = {focus.Coefficients[0]:E3}");

        // What is left binds at second order. The closed form for one linear stage
        // gives a relative time shift of e^2/2 in the fractional *velocity* offset
        // e; the fit here is in fractional *energy*, and e is half of that, so the
        // quadratic coefficient is a quarter as large — one eighth, not one half.
        // Measured: 0.130.
        Assert.Equal(2, focus.BindingOrder);
        Assert.InRange(focus.Coefficients[1], 0.10, 0.16);

        var peak = ArrivalTimePeak.FromArrivals(Scan(pair, 0.03, points: 41), 41);
        var (resolvingPower, _, _, _) = peak.ResolvingPower();

        // The conclusion the memo asked for, and it does not depend on tuning the
        // separation: that degree of freedom is spent cancelling first order, and
        // second order is what remains. Measured R is about 8,300 at plus or minus
        // three percent, so a single stage is short of the target by a factor of
        // roughly two and a half — not hopeless, but not reachable by tuning.
        Assert.True(
            resolvingPower.SiValue < TargetResolvingPower,
            $"a single-stage pair was expected to fall short of {TargetResolvingPower:N0}; it reported "
            + $"{resolvingPower.SiValue:F0}, which would be worth re-deriving before believing");

        Assert.InRange(resolvingPower.SiValue, 5000.0, 12000.0);

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"  Conclusion: at +/-3% energy this reaches R = {resolvingPower.SiValue:F0} against a target of "
            + $"{TargetResolvingPower:N0} — short by {TargetResolvingPower / resolvingPower.SiValue:F1}x.");
    }

    [Fact]
    public void ATwoStageMirrorReducesTheBindingSecondOrderTerm()
    {
        // Mamyrin's arrangement: a short steep decelerating stage in front of a
        // shallower reflecting stage. The ratio between them is the free parameter
        // available for second order, once the separation has been spent on first.
        var single = PlanarMirror.Solve(
            MirrorProfile.SingleStage(MirrorDepth, CapPotential), BoardGap, FieldFreeRun, cellsPerGap: 32);

        var (singlePair, singleFocus) = TuneToFirstOrderFocus(
            single, single.TurningDepth(Energy(), chargeNumber: 1));

        output.WriteLine("first stage    V1      separation      c1            c2           R at +/-3%");

        MirrorPair? best = null;
        FocusingOrder? bestFocus = null;
        var smallestSecondOrder = double.MaxValue;
        var bestResolvingPower = 0.0;

        foreach (var firstStageFraction in new[] { 0.25, 0.35, 0.40, 0.45 })
        {
            var firstDepth = MirrorDepth * firstStageFraction;
            var firstPotential = IonEnergyElectronvolts * 0.7;

            var mirror = PlanarMirror.Solve(
                MirrorProfile.TwoStage(firstDepth, firstPotential, MirrorDepth, CapPotential),
                BoardGap, FieldFreeRun, cellsPerGap: 32);

            var depth = mirror.TurningDepth(Energy(), chargeNumber: 1);
            var (pair, focus) = TuneToFirstOrderFocus(mirror, depth);

            var peak = ArrivalTimePeak.FromArrivals(Scan(pair, 0.03, points: 41), 41);
            var (r, _, _, _) = peak.ResolvingPower();

            output.WriteLine(
                $"{firstStageFraction,11:P0}   {firstPotential,5:F0}   {pair.CapToCap * 1e3,9:F2} mm   "
                + $"{focus.Coefficients[0],11:E3}   {focus.Coefficients[1],11:E3}   {r.SiValue,10:F0}");

            // Only a profile that actually reached first-order focus is a
            // candidate. A small second-order coefficient on a geometry whose
            // first-order term survives is not a better mirror, it is a search
            // that did not finish, and ranking on c2 alone would prefer it.
            if (Math.Abs(focus.Coefficients[0]) > 5e-3)
            {
                continue;
            }

            if (r.SiValue > bestResolvingPower)
            {
                bestResolvingPower = r.SiValue;
                smallestSecondOrder = Math.Abs(focus.Coefficients[1]);
                best = pair;
                bestFocus = focus;
            }
        }

        Assert.NotNull(best);
        Assert.NotNull(bestFocus);

        output.WriteLine(string.Empty);
        Report("BEST TWO-STAGE", best, bestFocus);

        // A second stage reduces the term that binds a single-stage mirror, and
        // far enough to clear the target.
        Assert.True(
            smallestSecondOrder < Math.Abs(singleFocus.Coefficients[1]),
            "a two-stage profile should reduce the second-order term below the single-stage "
            + $"{Math.Abs(singleFocus.Coefficients[1]):E3}, but the best found was {smallestSecondOrder:E3}");

        var bestPeak = ArrivalTimePeak.FromArrivals(Scan(best, 0.03, points: 41), 41);
        var (bestR, _, _, _) = bestPeak.ResolvingPower();

        Assert.True(
            bestR.SiValue > TargetResolvingPower,
            $"the best two-stage profile reached R = {bestR.SiValue:F0}, short of {TargetResolvingPower:N0}");

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"  At +/-3% energy the best two-stage profile reaches R = {bestR.SiValue:F0}, clearing the "
            + $"{TargetResolvingPower:N0} target that the single stage misses.");
        output.WriteLine(
            "  c2 changes sign across the scan, so the true second-order focus lies between the bracketing "
            + "fractions; finding it exactly is a job for the optimiser in Einzel.Sweeps.");

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"  Second-order term {Math.Abs(singleFocus.Coefficients[1]):E3} single-stage against "
            + $"{smallestSecondOrder:E3} two-stage, a {Math.Abs(singleFocus.Coefficients[1]) / smallestSecondOrder:F1}x "
            + "reduction. Separation moved from "
            + $"{singlePair.CapToCap * 1e3:F1} mm to {best.CapToCap * 1e3:F1} mm.");
    }
}
