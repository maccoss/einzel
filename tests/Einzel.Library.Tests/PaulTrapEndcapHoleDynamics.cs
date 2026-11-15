using System.Globalization;
using Einzel.Analysis;
using Einzel.Transport.Integration;
using Xunit.Abstractions;
using static Einzel.Library.Tests.PaulTrapHoles;

namespace Einzel.Library.Tests;

/// <summary>
/// What an ion does in a quadrupole trap with holes in its endcaps and a stretched endcap
/// spacing: where it leaves, its secular frequency, the stability edge, and whether a
/// nonlinear resonance band appears inside the stable region.
/// </summary>
/// <remarks>
/// <para>
/// Companion to <see cref="PaulTrapEndcapHoleStudy"/>, which measures the same geometries'
/// fields; both take them from <see cref="PaulTrapHoles"/>. The numbers printed here are the
/// ones in <c>docs/device-templates.md</c>.
/// </para>
/// <para>
/// <b>An ion is lost when it first passes the plane of an endcap's vertex</b>
/// (<see cref="Loss.TrappingRegion"/>), whether it then strikes metal, enters a bore or
/// leaves. That is the same criterion in every geometry: in a solid trap an ion reaching the
/// vertex plane strikes the endcap, and in a holed one it has entered the bore. The first
/// version of this study asked instead where the ion was at the last instant of the hold
/// (<see cref="Loss.FinalPosition"/>), which in a holed trap could count an ion that swung into
/// the bore and back as held; the two are compared below and agree everywhere they were
/// measured, so no published number moved for that reason.
/// </para>
/// </remarks>
public sealed class PaulTrapEndcapHoleDynamics(ITestOutputHelper output)
{
    /// <summary>
    /// How long the edge is held for. Not 200 cycles: with holes the edge at 200 cycles is a
    /// ragged strip - held islands above the first loss - so a bisection lands on whichever
    /// crossing its path meets first, and a single marginal flight decides it. Measured in
    /// <see cref="AtTwoHundredCyclesTheHoledEdgeIsARaggedStrip"/>.
    /// </summary>
    private const double EdgeCycles = 800.0;

    private static readonly double[] OneHoleLosses = [0.79, 0.87, 0.88, 0.89];

    private static readonly double[] EdgeOnly = [0.89];

    private static readonly Dictionary<string, Solved> Traps = new(StringComparer.Ordinal);

    /// <summary>Each geometry solved once for the whole class, and flown at any amplitude.</summary>
    private static Solved Trap(string name, double cells = 20.0)
    {
        var key = name + "@" + cells.ToString(CultureInfo.InvariantCulture);

        lock (Traps)
        {
            if (!Traps.TryGetValue(key, out var trap))
            {
                trap = new Solved(Geometry(name).Append(("cellsPerRadius", cells)).ToArray());
                Traps[key] = trap;
            }

            return trap;
        }
    }

    private static double Line(IReadOnlyList<TrajectorySample> samples, int axis, double drive)
    {
        var spectrum = SecularSpectrum.From(samples, axis, 0.02 * drive, 0.90 * drive, 6000);
        var peak = spectrum.Peak();

        Assert.NotNull(peak);

        var (value, _, _, _) = peak;

        return value.In("Hz");
    }

    /// <summary>
    /// An ion ejected along the axis through the exit hole arrives at the detector; one
    /// that meets the exit endcap off the hole is itemised on <c>endcapExit</c>.
    /// </summary>
    /// <remarks>
    /// The launches differ in how far off axis the ion starts. Driven past the stability edge
    /// on the a = 0 line the axial motion grows and the radial motion stays bounded, so the
    /// radial offset decides whether the ion meets the hole or the metal around it. Flown with
    /// the detector as the only stop, so the outcome is the engine's own.
    /// </remarks>
    [Fact]
    public void AnIonThroughTheExitHoleArrivesAndOneOffItIsItemised()
    {
        var trap = Trap("exit hole only");

        Flight? onAxis = null;
        Flight? offAxis = null;

        foreach (var (axial, volts) in new[] { (0.05, 800.0), (-0.05, 800.0), (-0.05, 850.0), (-0.05, 900.0) })
        {
            foreach (var radial in new[] { 0.01, 1.0, 2.0, 2.5 })
            {
                var flight = trap.Fly(volts, axial, radial, 200, Loss.FinalPosition);
                var p = flight.Result.FinalState.Position;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{volts,4:F0} V  z {axial,5:F2} mm  r {radial,4:F2} mm  {flight.Result.Outcome}  {flight.Result.StruckSurface ?? "-"}  "
                    + $"end x = {p.X * 1e3:F3} mm, r = {Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z)) * 1e3:F3} mm"));

                if (radial == 0.01 && flight.Result.Outcome == TrajectoryOutcome.StopConditionMet)
                {
                    onAxis ??= flight;
                }

                if (flight.Result.StruckSurface == "endcapExit")
                {
                    offAxis ??= flight;
                }
            }
        }

        Assert.NotNull(onAxis);
        Assert.NotNull(offAxis);

        // Arrived at the detector plane behind the exit endcap, having passed through it.
        Assert.Equal(trap.Model.DetectorPoint.X, onAxis.Result.FinalState.Position.X, 9);

        // The control: with no hole the same on-axis ion cannot arrive.
        foreach (var axial in new[] { 0.05, -0.05 })
        {
            var solid = Trap("ideal").Fly(800.0, axial, 0.01, 200, Loss.FinalPosition);

            Assert.Equal(TrajectoryOutcome.StruckElectrode, solid.Result.Outcome);
            Assert.StartsWith("endcap", solid.Result.StruckSurface!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An ion ejected through the entrance hole strikes <c>entranceStop</c> and is itemised
    /// there, rather than coasting out of the domain and reading as still confined.
    /// </summary>
    [Fact]
    public void AnIonThroughTheEntranceHoleIsItemisedOnTheStop()
    {
        var trap = new Solved(("endcapEntranceHoleFraction", HoleFraction));

        var flights = new[] { 0.05, -0.05 }
            .Select(axial => trap.Fly(800.0, axial, 0.01, 200, Loss.FinalPosition))
            .ToArray();

        foreach (var flight in flights)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{flight.Result.Outcome} {flight.Result.StruckSurface ?? "-"} at x = {flight.Result.FinalState.Position.X * 1e3:F3} mm"));
        }

        var stopped = flights.Where(f => f.Result.StruckSurface == "entranceStop").ToArray();

        Assert.NotEmpty(stopped);
        Assert.All(stopped, f => Assert.Equal(
            -trap.Model.Parameters["domainHalfLength"].SiValue, f.Result.FinalState.Position.X, 9));
        Assert.DoesNotContain(flights, f => f.Result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached);
    }

    /// <summary>
    /// The secular frequency at q_eff = 0.5 is Mathieu's at the effective radius in every
    /// geometry, and the ejection edge in q_eff is where the holes put it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The secular line is measured at a small launch (0.05 mm) and compared with Mathieu at
    /// the q the field's own curvature implies, so what it checks is that the effective
    /// radius describes the ion's motion. The edge is bisected at the same launch over
    /// <see cref="EdgeCycles"/> cycles, by the trapping-region criterion.
    /// </para>
    /// </remarks>
    [Fact]
    public void SecularFrequencyAndEdge()
    {
        output.WriteLine(
            "geometry          r0_eff(mm)  V@q.5   beta_z meas  Mathieu  ratio      edge (V)          q_eff edge       q_nom edge");

        var edges = new Dictionary<string, double>(StringComparer.Ordinal);
        var predicted = Beta(0.0, 0.5);

        foreach (var name in new[] { "ideal", "both holes", "exit hole only", "holes + stretch" })
        {
            var trap = Trap(name);

            var volts = 0.5 / trap.QPerVolt;
            var flight = trap.Fly(volts, 0.05, 0.05, 200, Loss.TrappingRegion, record: true);

            Assert.True(flight.Held);

            var betaZ = 2.0 * Line(flight.Samples, 0, trap.DriveHz) / trap.DriveHz;
            var (held, lost) = trap.Edge(0.05, 0.0, EdgeCycles, Loss.TrappingRegion);

            edges[name] = trap.QPerVolt * 0.5 * (held + lost);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-17} {trap.EffectiveRadius * 1e3,9:F4}  {volts,7:F2}  {betaZ,11:F5}  {predicted,7:F5}  "
                + $"{betaZ / predicted,7:F5}  {held,7:F2}-{lost,7:F2}  {trap.QPerVolt * held,7:F4}-{trap.QPerVolt * lost:F4}  "
                + $"{trap.NominalQPerVolt * held,8:F4}"));

            // The effective radius describes the small-amplitude motion: measured within 4e-4
            // of Mathieu in every geometry, asserted to 1e-3.
            Assert.True(Math.Abs((betaZ / predicted) - 1.0) < 1e-3, $"{name}: beta_z / Mathieu {betaZ / predicted:F5}");
        }

        // The edge in q_eff, at 800 cycles and the shipped mesh. With holes the edge there is
        // still a strip rather than a step - both holes: first loss at q_eff 0.9178, last held
        // amplitude at 0.9209 - and a bisection may land anywhere in it, so each window is the
        // measured strip with 0.002 either side, which is wider than anything a change of
        // arithmetic was seen to do (moving the launch by a micrometre moved the edge 0.0005).
        // The windows do not overlap, so what is asserted is the ordering the holes and the
        // stretch produce as well as where each edge is.
        Assert.InRange(edges["ideal"], 0.9045, 0.9065);
        Assert.InRange(edges["exit hole only"], 0.909, 0.912);
        Assert.InRange(edges["holes + stretch"], 0.913, 0.917);
        Assert.InRange(edges["both holes"], 0.916, 0.923);
    }

    /// <summary>
    /// With holes, the ejection edge held for 200 cycles is a ragged strip, which is why a
    /// bisection of it gave 759 V on one platform and 762.5 V on another; the loss criterion
    /// and the phase at which the hold ends decide nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both holes, 0.25 V steps across the edge: amplitudes are held above the first one lost,
    /// up to 3.25 V above it, so the edge is several crossings rather than one. A bisection from
    /// 600 and 1000 V meets 762.5 V as its fifth midpoint, which sits on the last held island:
    /// held there, the search climbs to 762.5-763.3 V; lost there, it falls to 759.4-760.2 V.
    /// That one marginal flight is all that separated the two platforms' results, and flying the
    /// same trap by re-weighting a solved field rather than recompiling it flips it the same way
    /// - so the arithmetic decides it, but only because the edge offers it two places to land.
    /// The ideal trap is the control: at the same hold its edge is one clean step.
    /// </para>
    /// <para>
    /// Every verdict is asserted equal under the two loss criteria and at holds of 200 and
    /// 200.5 cycles: an ion that crosses the vertex plane does not come back to be counted held,
    /// and nothing depends on the phase of the last cycle.
    /// </para>
    /// </remarks>
    [Fact]
    public void AtTwoHundredCyclesTheHoledEdgeIsARaggedStrip()
    {
        foreach (var (name, from) in new[] { ("ideal", 736.0), ("both holes", 757.0) })
        {
            var trap = Trap(name);
            var line = new System.Text.StringBuilder();
            double? firstLost = null;
            double? lastHeld = null;

            for (var k = 0; k <= 32; k++)
            {
                var volts = from + (0.25 * k);
                var held = trap.Fly(volts, 0.05, 0.0, 200, Loss.TrappingRegion).Held;

                Assert.Equal(held, trap.Fly(volts, 0.05, 0.0, 200, Loss.FinalPosition).Held);
                Assert.Equal(held, trap.Fly(volts, 0.05, 0.0, 200.5, Loss.TrappingRegion).Held);

                line.Append(held ? '.' : 'x');

                if (held)
                {
                    lastHeld = volts;
                }
                else
                {
                    firstLost ??= volts;
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-11} {from:F2} {line} {from + 8.0:F2}  first lost {firstLost:F2} V, last held {lastHeld:F2} V"));

            Assert.NotNull(firstLost);
            Assert.NotNull(lastHeld);

            if (name == "ideal")
            {
                Assert.True(lastHeld < firstLost, "the ideal trap's edge should be one step");
            }
            else
            {
                Assert.True(lastHeld - firstLost >= 1.0, "the holed edge should be a strip at 200 cycles");
            }
        }
    }

    /// <summary>
    /// The stable region scanned for loss at a 0.3 mm launch: one hole opens a band, and the
    /// others hold every ion up to the amplitude-dependent edge, over 200 cycles.
    /// </summary>
    [Fact]
    public void ResonanceScan()
    {
        output.WriteLine("0.3 mm launch (axial and radial), 200 cycles; '.' held, 'x' lost:");

        foreach (var name in new[] { "ideal", "both holes", "exit hole only", "holes + stretch" })
        {
            var trap = Trap(name);
            var line = new System.Text.StringBuilder();
            var lost = new List<double>();

            for (var k = 0; k <= 29; k++)
            {
                var q = 0.60 + (0.01 * k);

                if (trap.Fly(q / trap.QPerVolt, 0.3, 0.3, 200, Loss.TrappingRegion).Held)
                {
                    line.Append('.');
                }
                else
                {
                    line.Append('x');
                    lost.Add(q);
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-17} q 0.60 {line} 0.89   lost at q_eff {string.Join(", ", lost.Select(q => q.ToString("F2", CultureInfo.InvariantCulture)))}"));

            if (name == "exit hole only")
            {
                // The hexapole band at 0.79 and the early edge from 0.87, and nothing else.
                Assert.Equal(OneHoleLosses, lost.Select(q => Math.Round(q, 2)).ToArray());
            }
            else
            {
                // Only the amplitude-dependent edge at 0.89.
                Assert.Equal(EdgeOnly, lost.Select(q => Math.Round(q, 2)).ToArray());
            }
        }
    }

    /// <summary>The longest run of consecutive lost points in a scan, and every lost point outside it.</summary>
    private static (int Start, int Length, IReadOnlyList<int> Isolated) LongestRun(List<bool> lost)
    {
        int bestStart = -1, bestLength = 0;

        for (var i = 0; i < lost.Count;)
        {
            if (!lost[i])
            {
                i++;
                continue;
            }

            var start = i;

            while (i < lost.Count && lost[i])
            {
                i++;
            }

            if (i - start > bestLength)
            {
                bestStart = start;
                bestLength = i - start;
            }
        }

        var isolated = Enumerable.Range(0, lost.Count)
            .Where(i => lost[i] && (i < bestStart || i >= bestStart + bestLength))
            .ToArray();

        return (bestStart, bestLength, isolated);
    }

    /// <summary>
    /// One hole, and a resonance band inside the stable region that two holes do not show
    /// over the same hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a hole in the exit endcap alone the trap is no longer symmetric about its center
    /// plane, so odd multipoles appear - a hexapole above all, measured in
    /// <see cref="PaulTrapEndcapHoleStudy"/> - and a hexapole's resonances satisfy
    /// <c>n_z beta_z + n_r beta_r = 2</c> with <c>n_z + n_r = 3</c>. In an axisymmetric field
    /// the radial index must be even, which leaves <c>3 beta_z = 2</c> and
    /// <c>beta_z + 2 beta_r = 2</c>. The scan finds the band - the longest run of consecutive
    /// lost points, so an isolated loss elsewhere is reported rather than folded in - and the
    /// frequencies are then measured on an ion held just below it, in the solved geometry, and
    /// the conditions evaluated. Two meshes, because a band that moved with the mesh would be
    /// the mesh.
    /// </para>
    /// </remarks>
    [Fact]
    public void OneHoleOpensAHexapoleBand()
    {
        var bands = new List<(double Low, double High)>();

        foreach (var cells in new[] { 20.0, 40.0 })
        {
            var trap = Trap("exit hole only", cells);
            var line = new System.Text.StringBuilder();
            var lost = new List<bool>();

            for (var k = 0; k <= 24; k++)
            {
                var q = 0.75 + (0.0025 * k);
                var held = trap.Fly(q / trap.QPerVolt, 0.3, 0.3, 200, Loss.TrappingRegion).Held;

                line.Append(held ? '.' : 'x');
                lost.Add(!held);
            }

            var (start, length, isolated) = LongestRun(lost);

            var others = isolated.Count == 0
                ? "none"
                : string.Join(", ", isolated.Select(i => (0.75 + (0.0025 * i)).ToString("F4", CultureInfo.InvariantCulture)));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{cells,3:F0} cells/r0  q_eff 0.750 {line} 0.810; isolated losses: {others}"));

            Assert.True(length >= 2, "no band of two or more lost points");

            var low = 0.75 + (0.0025 * start);
            var high = 0.75 + (0.0025 * (start + length - 1));

            bands.Add((low, high));

            // Measured at 0.7850-0.7900 at both meshes.
            Assert.InRange(low, 0.780, 0.790);
            Assert.InRange(high, 0.785, 0.795);
            Assert.Empty(isolated);

            // The frequencies of an ion held just below the band.
            var below = low - 0.005;
            var flight = trap.Fly(below / trap.QPerVolt, 0.3, 0.3, 200, Loss.TrappingRegion, record: true);

            Assert.True(flight.Held);

            var betaZ = 2.0 * Line(flight.Samples, 0, trap.DriveHz) / trap.DriveHz;
            var betaR = 2.0 * Line(flight.Samples, 1, trap.DriveHz) / trap.DriveHz;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"   band q_eff {low:F4}-{high:F4}; at {below:F4}: beta_z {betaZ:F4}, beta_r {betaR:F4}, "
                + $"3 beta_z = {3.0 * betaZ:F4}, beta_z + 2 beta_r = {betaZ + (2.0 * betaR):F4}; "
                + $"Mathieu beta_z there {Beta(0.0, below):F4}"));

            // 3 beta_z approaching 2 from below as the band is entered (measured 1.980), and the
            // other hexapole condition nowhere near (1.23).
            Assert.InRange(3.0 * betaZ, 1.97, 2.00);
            Assert.InRange(betaZ + (2.0 * betaR), 1.1, 1.4);
        }

        // The band does not move with the mesh by more than one scan step.
        Assert.True(Math.Abs(bands[0].Low - bands[1].Low) <= 0.0026);

        // And the control: two equal holes, the same scan over the same hold, nothing lost.
        var both = Trap("both holes");

        for (var k = 0; k <= 24; k++)
        {
            var q = 0.75 + (0.0025 * k);

            Assert.True(
                both.Fly(q / both.QPerVolt, 0.3, 0.3, 200, Loss.TrappingRegion).Held,
                $"two holes lost an ion at q_eff {q:F4} in 200 cycles");
        }
    }

    /// <summary>
    /// Two holes do open the octupole's resonance - narrowly, and only given a longer hold
    /// than the scans above use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flat-annulus trap lost its ion in a band where <c>2 beta_z + 2 beta_r = 2</c>, the
    /// order-four condition an octupole drives. Two holes make the octupole -2.2 percent, and
    /// over 200 cycles nothing is lost; over 800 an ion launched at 0.3 mm is lost in a band
    /// narrower than 0.005 in q_eff at 0.810, at both meshes, and an ion held just below it, at
    /// 0.805, has <c>2 beta_z + 2 beta_r</c> = 1.978, approaching 2. The ideal trap, whose
    /// truncation octupole is +0.9 percent, loses nothing over the same scan and hold - the
    /// control. Holes plus a stretch, whose octupole is -0.5 percent, lose the same point only
    /// at 2000 cycles: slower, as a smaller octupole should be.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoHolesOpenTheOctupoleBandGivenTime()
    {
        foreach (var name in new[] { "ideal", "both holes" })
        {
            var trap = Trap(name);
            var line = new System.Text.StringBuilder();
            var lost = new List<bool>();

            for (var k = 0; k <= 8; k++)
            {
                var q = 0.80 + (0.0025 * k);
                var held = trap.Fly(q / trap.QPerVolt, 0.3, 0.3, 800, Loss.TrappingRegion).Held;

                line.Append(held ? '.' : 'x');
                lost.Add(!held);
            }

            output.WriteLine($"{name,-11} 800 cycles  q_eff 0.800 {line} 0.820");

            if (name == "ideal")
            {
                Assert.False(lost.Contains(true), "the ideal trap lost an ion");
                continue;
            }

            var (start, length, isolated) = LongestRun(lost);

            Assert.True(length >= 1, "two holes lost nothing in 800 cycles");
            Assert.Empty(isolated);

            var low = 0.80 + (0.0025 * start);

            Assert.InRange(low, 0.805, 0.815);

            var below = low - 0.005;
            var flight = trap.Fly(below / trap.QPerVolt, 0.3, 0.3, 200, Loss.TrappingRegion, record: true);

            Assert.True(flight.Held);

            var betaZ = 2.0 * Line(flight.Samples, 0, trap.DriveHz) / trap.DriveHz;
            var betaR = 2.0 * Line(flight.Samples, 1, trap.DriveHz) / trap.DriveHz;
            var sum = (2.0 * betaZ) + (2.0 * betaR);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"   at {below:F4}: beta_z {betaZ:F4}, beta_r {betaR:F4}, 2 beta_z + 2 beta_r = {sum:F4}"));

            // Measured 1.978: below the band and approaching the order-four condition.
            Assert.InRange(sum, 1.96, 2.00);
        }
    }
}