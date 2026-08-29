using System.Globalization;
using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The approximate space-charge method, against the reference it exists to be
/// validated against.
/// </summary>
/// <remarks>
/// <para>
/// SC-1 asks for a direct pairwise sum and an approximate method checked against it.
/// The direct sum was built first for exactly this reason, and both are
/// <c>ISelfField</c>, so the two can be handed the same configuration and differenced
/// — which is the only way "validated against" means anything.
/// </para>
/// <para>
/// The comparison has a floor that is not the method's fault. The direct sum softens
/// at the mean macroparticle spacing, and particle-in-cell smooths at the cell size;
/// two different smoothings of the same cloud do not agree particle by particle, and
/// neither is the smooth packet both are approximating. So what is compared is the
/// <em>collective</em> force — the field a particle feels from everything else —
/// sampled where that is a well-posed question.
/// </para>
/// </remarks>
public sealed class ParticleInCellTests(ITestOutputHelper output)
{
    private const double Elementary = 1.602176634e-19;
    private const double Dalton = 1.66053906892e-27;

    /// <summary>A uniformly filled ball of macroparticles.</summary>
    private static Vec3[] Ball(int count, double radiusSi, int seed)
    {
        var random = new Random(seed);
        var points = new Vec3[count];

        for (var k = 0; k < count; k++)
        {
            var cos = (2.0 * random.NextDouble()) - 1.0;
            var phi = 2.0 * Math.PI * random.NextDouble();
            var sin = Math.Sqrt(Math.Max(0.0, 1.0 - (cos * cos)));
            var r = radiusSi * Math.Cbrt(random.NextDouble());

            points[k] = new Vec3(
                r * sin * Math.Cos(phi), r * sin * Math.Sin(phi), r * cos);
        }

        return points;
    }

    private static Vec3[] Accelerate(ISelfField field, Vec3[] positions)
    {
        var active = new bool[positions.Length];
        var accelerations = new Vec3[positions.Length];

        Array.Fill(active, true);

        field.Accumulate(positions, active, accelerations);

        return accelerations;
    }

    [Fact]
    public void ItAgreesWithTheDirectSumOnTheSamePacket()
    {
        // The measurement SC-1 asks for. Both methods, one configuration, differenced
        // radially - the packet is a ball, so the self-force is radial and its
        // magnitude against radius is the quantity that has to match.
        const int Macroparticles = 4000;
        const double Population = 1.0e6;
        const double RadiusSi = 0.5e-3;

        var positions = Ball(Macroparticles, RadiusSi, seed: 20260828);

        var direct = new CoulombInteraction(
            Population, Macroparticles, Elementary, 500.0 * Dalton,
            CoulombInteraction.SpacingSoftening(RadiusSi, Macroparticles));

        var grid = new ParticleInCell(
            Population, Macroparticles, Elementary, 500.0 * Dalton, nodes: 32, padding: 4.0);

        var a = Accelerate(direct, positions);
        var b = Accelerate(grid, positions);

        output.WriteLine($"direct softening {direct.SofteningLengthSi * 1e6:F2} um");
        output.WriteLine($"grid: {grid.Solves} solve(s), charge outside {grid.ChargeOutside:P2}");
        output.WriteLine("   r/R    direct (m/s^2)    grid (m/s^2)     ratio    n");

        // Binned by radius, because the two methods smooth differently at the scale
        // of a single particle and identically at the scale of the packet.
        for (var bin = 0; bin < 5; bin++)
        {
            var lo = bin / 5.0 * RadiusSi;
            var hi = (bin + 1) / 5.0 * RadiusSi;

            double sumDirect = 0.0, sumGrid = 0.0;
            var n = 0;

            for (var k = 0; k < positions.Length; k++)
            {
                var r = positions[k].Length;

                if (r < lo || r >= hi)
                {
                    continue;
                }

                var unit = positions[k] * (1.0 / r);

                sumDirect += Vec3.Dot(a[k], unit);
                sumGrid += Vec3.Dot(b[k], unit);
                n++;
            }

            var meanDirect = sumDirect / n;
            var meanGrid = sumGrid / n;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{(bin + 0.5) / 5.0,6:F1}   {meanDirect,15:E4}   {meanGrid,13:E4}   "
                + $"{meanGrid / meanDirect,7:F4}   {n,4}"));

            // Both outward, and agreeing to about a per cent through the body of the
            // packet. The outermost bin is the worst and has to be: it straddles the
            // ball's surface, where the density steps to zero, and a smoothed deposit
            // and a point-softened sum disagree about a discontinuity by construction.
            // The shape - rising with radius as Qr/R^3 does - is what says they
            // describe the same packet.
            Assert.True(meanDirect > 0.0, "the self-force should push outward");
            Assert.InRange(meanGrid / meanDirect, bin == 4 ? 0.85 : 0.95, 1.05);
        }

        Assert.Equal(1, grid.Solves);
        Assert.Equal(0.0, grid.ChargeOutside);
    }

    [Fact]
    public void ItCostsOneSolveHoweverManyTimesItIsAsked()
    {
        // The whole economy of the method. The direct sum is O(N^2) every time it is
        // called, and a Runge-Kutta step calls it seven times; this solves once and
        // gathers, so a packet whose shape is not changing pays for one Poisson solve
        // and then reads from it.
        var positions = Ball(500, 0.5e-3, seed: 4);

        var grid = new ParticleInCell(1.0e5, 500, Elementary, 500.0 * Dalton, nodes: 16);

        for (var k = 0; k < 7; k++)
        {
            Accelerate(grid, positions);
        }

        output.WriteLine($"{grid.Gathers} gathers, {grid.Solves} solve");

        Assert.Equal(7, grid.Gathers);
        Assert.Equal(1, grid.Solves);
    }

    [Fact]
    public void AChangeOfShapeForcesANewSolveAndATranslationDoesNot()
    {
        // The refresh criterion, and why it is written on shape. The held potential is
        // anchored to the centroid it was solved at, so a packet in uniform
        // translation samples exactly the field it had - that is not an approximation
        // and it must not cost a solve. What ages is the shape.
        var positions = Ball(500, 0.5e-3, seed: 9);

        var grid = new ParticleInCell(
            1.0e5, 500, Elementary, 500.0 * Dalton, nodes: 16, refreshTolerance: 0.05);

        Accelerate(grid, positions);
        Assert.Equal(1, grid.Solves);

        // Moved a long way, unchanged in shape.
        var moved = new Vec3[positions.Length];

        for (var k = 0; k < positions.Length; k++)
        {
            moved[k] = positions[k] + new Vec3(0.25, 0.0, 0.0);
        }

        Accelerate(grid, moved);

        output.WriteLine($"after translating 250 mm: {grid.Solves} solve(s)");
        Assert.Equal(1, grid.Solves);

        // And the force is the same, because the packet is.
        var here = Accelerate(grid, positions);
        var there = Accelerate(grid, moved);

        var worst = 0.0;

        for (var k = 0; k < positions.Length; k++)
        {
            worst = Math.Max(worst, (here[k] - there[k]).Length / Math.Max(here[k].Length, 1e-30));
        }

        output.WriteLine($"worst difference across the translation: {worst:E2}");
        Assert.True(worst < 1e-9, $"a translated packet should feel the same force: {worst:E2}");

        // Expanded by a fifth, which is four times the tolerance.
        var expanded = new Vec3[positions.Length];

        for (var k = 0; k < positions.Length; k++)
        {
            expanded[k] = positions[k] * 1.2;
        }

        Accelerate(grid, expanded);

        output.WriteLine($"after expanding by 20 per cent: {grid.Solves} solve(s)");
        Assert.Equal(2, grid.Solves);
    }

    [Fact]
    public void APacketWithNobodyToPushOnFeelsNothing()
    {
        // One macroparticle is a packet with nobody to push on, and the direct sum
        // returns the same nothing. Neither is a special case worth a warning; it is
        // the sum over an empty set.
        var grid = new ParticleInCell(1.0e5, 1, Elementary, 500.0 * Dalton);

        var accelerations = Accelerate(grid, [new Vec3(1.0e-3, 0.0, 0.0)]);

        Assert.Equal(0.0, accelerations[0].Length);
        Assert.Equal(0, grid.Solves);
    }

    [Fact]
    public void ItDoesNotOverwriteTheAppliedField()
    {
        // Accumulated, not assigned. The applied field has already written its
        // acceleration into the span by the time a self-field is asked, and a method
        // that overwrote it would silently delete the instrument.
        var positions = Ball(200, 0.5e-3, seed: 11);

        var grid = new ParticleInCell(1.0e5, 200, Elementary, 500.0 * Dalton, nodes: 16);

        var active = new bool[positions.Length];
        var accelerations = new Vec3[positions.Length];

        Array.Fill(active, true);

        var applied = new Vec3(0.0, 0.0, 1.0e7);

        Array.Fill(accelerations, applied);

        grid.Accumulate(positions, active, accelerations);

        var mean = Vec3.Zero;

        foreach (var a in accelerations)
        {
            mean += a;
        }

        mean *= 1.0 / accelerations.Length;

        output.WriteLine($"mean z acceleration {mean.Z:E4}, applied {applied.Z:E4}");

        // The self-force of a symmetric packet averages to nothing, so what is left
        // on average is what was there before.
        Assert.Equal(applied.Z, mean.Z, 0.01 * applied.Z);
    }

    /// <summary>Flies a packet in free space and returns how far it spread.</summary>
    /// <remarks>
    /// Free flight on purpose: with no applied field the only thing that widens the
    /// packet is its own charge, so the widening IS the self-force integrated twice
    /// and there is nothing else for the two methods to agree about by accident.
    /// </remarks>
    private static (double RmsMm, double Seconds, int Solves, int Steps, int Gathers, int Cycles, int Rebuilds) Widen(
        ISelfField interaction, Vec3[] start, double flightSeconds)
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var launch = new PhaseState[start.Length];

        for (var k = 0; k < start.Length; k++)
        {
            launch[k] = new PhaseState(start[k], Vec3.Zero);
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = PacketIntegrator.Fly(
            launch,
            species,
            FieldFreeSpace.Instance,
            interaction,
            new IntegrationSettings
            {
                MaximumFlightTime = flightSeconds,
                RelativeTolerance = 1e-9,
            },
            (in PhaseState state) => 1.0);

        clock.Stop();

        var centroid = Vec3.Zero;

        foreach (var member in result.Members)
        {
            centroid += member.FinalState.Position;
        }

        centroid *= 1.0 / result.Members.Count;

        var spread = 0.0;

        foreach (var member in result.Members)
        {
            spread += (member.FinalState.Position - centroid).LengthSquared;
        }

        var pic = interaction as ParticleInCell;

        return (
            Math.Sqrt(spread / result.Members.Count) * 1e3,
            clock.Elapsed.TotalSeconds,
            pic?.Solves ?? 0,
            result.Steps,
            pic?.Gathers ?? 0,
            pic?.Cycles ?? 0,
            pic?.Rebuilds ?? 0);
    }

    [Fact]
    public void AFlownPacketWidensTheSameAmountBothWays()
    {
        // The end-to-end check, and the one that makes this "wired to the packet
        // integrator" rather than "an ISelfField that exists". A packet released in
        // free space expands under nothing but its own charge, so the widening after
        // a fixed time is the self-force integrated twice - and if the two methods
        // disagree anywhere along the flight, they disagree here.
        const int Macroparticles = 600;
        const double Population = 2.0e6;
        const double RadiusSi = 0.5e-3;
        const double Flight = 2.0e-6;

        var start = Ball(Macroparticles, RadiusSi, seed: 31);

        var initial = 0.0;

        foreach (var p in start)
        {
            initial += p.LengthSquared;
        }

        initial = Math.Sqrt(initial / Macroparticles) * 1e3;

        var direct = Widen(
            new CoulombInteraction(
                Population, Macroparticles, Elementary, 500.0 * Dalton,
                CoulombInteraction.SpacingSoftening(RadiusSi, Macroparticles)),
            start,
            Flight);

        var grid = Widen(
            new ParticleInCell(
                Population, Macroparticles, Elementary, 500.0 * Dalton, nodes: 32),
            start,
            Flight);

        output.WriteLine($"released at rms {initial:F4} mm, flown {Flight * 1e6:F1} us");
        output.WriteLine(
            $"direct  {direct.RmsMm:F4} mm   {direct.Seconds:F2} s   {direct.Steps} steps");
        output.WriteLine(
            $"grid    {grid.RmsMm:F4} mm   {grid.Seconds:F2} s   {grid.Steps} steps, "
            + $"{grid.Solves} solves ({grid.Cycles} cycles, {grid.Rebuilds} rebuilds), "
            + $"{grid.Gathers} gathers");
        output.WriteLine($"ratio   {grid.RmsMm / direct.RmsMm:F4}");

        // It really did expand, or the comparison is between two numbers that are
        // both the starting size.
        Assert.True(
            direct.RmsMm > 1.5 * initial,
            $"the packet should have expanded: {initial:F4} to {direct.RmsMm:F4} mm");

        // And the two agree to a few per cent, which is the same bound the static
        // comparison gives - so nothing accumulates over a flight that was not
        // already there in one evaluation.
        Assert.InRange(grid.RmsMm / direct.RmsMm, 0.95, 1.05);
    }

    [Fact]
    public void TighteningTheRefreshToleranceConvergesOnTheReference()
    {
        // The refresh criterion is the one number in this method that is a choice
        // rather than a consequence, so it needs evidence that it is a *controlled*
        // approximation - that tightening it goes somewhere, and somewhere is the
        // method it approximates. Otherwise 5% is a number that made something finish.
        //
        // The direction is a prediction rather than something explained afterwards. A
        // field held across a refresh is the field of a packet denser than the one
        // being pushed, so a stale field always pushes too hard: every tolerance
        // should come out WIDE, and tightening should reduce it monotonically.
        const int Macroparticles = 400;
        const double Population = 2.0e6;
        const double RadiusSi = 0.5e-3;
        const double Flight = 2.0e-6;

        var start = Ball(Macroparticles, RadiusSi, seed: 31);

        var reference = Widen(
            new CoulombInteraction(
                Population, Macroparticles, Elementary, 500.0 * Dalton,
                CoulombInteraction.SpacingSoftening(RadiusSi, Macroparticles)),
            start,
            Flight).RmsMm;

        output.WriteLine($"reference (direct sum)  {reference:F4} mm");
        output.WriteLine("tolerance     rms mm      error    solves");

        double[] tolerances = [0.30, 0.15, 0.05, 0.02];

        var errors = new double[tolerances.Length];

        for (var k = 0; k < tolerances.Length; k++)
        {
            var run = Widen(
                new ParticleInCell(
                    Population,
                    Macroparticles,
                    Elementary,
                    500.0 * Dalton,
                    nodes: 32,
                    refreshTolerance: tolerances[k]),
                start,
                Flight);

            errors[k] = run.RmsMm / reference - 1.0;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{tolerances[k],9:F2}  {run.RmsMm,9:F4}  {errors[k],8:P2}  {run.Solves,8}"));
        }

        // Monotone, which is the convergence claim and the assertion with teeth: a
        // knob that did not converge would still pass a sign test at every value.
        for (var k = 1; k < errors.Length; k++)
        {
            Assert.True(
                errors[k] < errors[k - 1],
                $"tightening {tolerances[k - 1]} to {tolerances[k]} did not help: "
                + $"{errors[k - 1]:P2} to {errors[k]:P2}");
        }

        // Wide at the coarse end, by a wide margin, where staleness is the whole
        // story: 12.7% at a tolerance of 0.30.
        Assert.True(errors[0] > 0.05, $"a stale field should push too hard: {errors[0]:P2}");

        // And it lands on the reference: 1.0% at the shipped default and -0.5% at
        // 0.02. It crosses zero rather than approaching from one side, and that is
        // NOT a failure of the prediction above - it is staleness falling below the
        // OTHER difference between the two methods, which is that they smooth the
        // self-field at different scales and in different shapes. That residual is
        // measured separately, in TheGridAndTheSumAgreeAtMatchedSmoothing, and it is
        // why the two are only comparable once the smoothing scales are matched.
        Assert.InRange(Math.Abs(errors[^1]), 0.0, 0.02);
    }

    [Fact]
    public void TheGridAndTheSumAgreeAtMatchedSmoothing()
    {
        // What "validated against the reference" actually requires, and it is not what
        // a first reading suggests. NEITHER method computes the point-charge field of
        // the macroparticles: the sum softens at short range and the grid smooths at
        // the cell, so a comparison at whatever settings each happens to default to is
        // a comparison of two different smoothing lengths. Agreement there is a
        // coincidence of magnitudes, and disagreement is not evidence of a defect.
        //
        // So the comparison is made at a scale both can be told: take the sum's
        // softening to zero, which is a limit it HAS, and bracket that limit with grid
        // cells either side of the mean macroparticle spacing.
        const int Macroparticles = 216;
        const double Population = 2.0e6;
        const double RadiusSi = 0.5e-3;
        const double Flight = 2.0e-6;
        const double Padding = 4.0;

        var start = Ball(Macroparticles, RadiusSi, seed: 31);

        var spacing = CoulombInteraction.SpacingSoftening(RadiusSi, Macroparticles);

        double Sum(double softening) => Widen(
            new CoulombInteraction(
                Population, Macroparticles, Elementary, 500.0 * Dalton, softening),
            start,
            Flight).RmsMm;

        // The sum has a limit and reaches it: a further tenfold reduction in softening
        // moves the answer by a fraction of a per cent.
        var softened = Sum(spacing);
        var nearlyPoint = Sum(0.1 * spacing);
        var point = Sum(0.01 * spacing);

        output.WriteLine($"mean macroparticle spacing  {spacing * 1e3:F5} mm");
        output.WriteLine($"sum, softened at spacing    {softened:F5} mm");
        output.WriteLine($"sum, softening / 10         {nearlyPoint:F5} mm");
        output.WriteLine($"sum, softening / 100        {point:F5} mm  <- the limit");

        Assert.Equal(point, nearlyPoint, 0.005 * point);

        // The default softening is NOT that limit, and by a margin larger than any
        // agreement this class claims elsewhere. That is the whole point: the
        // reference has an approximation in it too, so an unmatched comparison
        // measures the difference between two smoothings.
        Assert.True(
            softened < 0.98 * point,
            $"the sum's own softening should matter: {softened:F5} against {point:F5} mm");

        output.WriteLine(string.Empty);
        output.WriteLine("  nodes    cell mm    cell/spacing     rms mm     vs limit");

        // OverBox rounds each axis up to a power of two, so 24 and 32 would be the
        // same mesh - these three are three distinct meshes, chosen to straddle a cell
        // of one spacing (3.0, 1.5 and 0.75 of it at this macroparticle count).
        int[] counts = [16, 32, 64];

        var rms = new double[counts.Length];

        for (var k = 0; k < counts.Length; k++)
        {
            rms[k] = Widen(
                new ParticleInCell(
                    Population, Macroparticles, Elementary, 500.0 * Dalton,
                    nodes: counts[k], padding: Padding, refreshTolerance: 0.05),
                start,
                Flight).RmsMm;

            var cell = 2.0 * Padding * RadiusSi / counts[k];

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{counts[k],7}  {cell * 1e3,9:F5}  {cell / spacing,13:F2}  {rms[k],10:F5}  "
                + $"{rms[k] / point - 1.0,10:P2}"));
        }

        // The limit is BRACKETED by the grid resolution, which is a stronger statement
        // than any single agreement number: a coarse cell over-smooths, so the field it
        // gathers is too weak and the packet comes out narrow; a cell below the spacing
        // stops representing a density and starts representing the macroparticles as
        // lumps, which pushes too hard. The reference sits between them.
        Assert.True(rms[0] < point, $"a coarse grid should under-push: {rms[0]:F5} mm");
        Assert.True(rms[2] > point, $"a fine grid should over-push: {rms[2]:F5} mm");

        // Monotone through the bracket, so it is a crossing rather than scatter.
        Assert.True(rms[1] > rms[0], $"{rms[0]:F5} then {rms[1]:F5} mm");
        Assert.True(rms[2] > rms[1], $"{rms[1]:F5} then {rms[2]:F5} mm");

        // And the crossing is near a cell of one spacing rather than anywhere in the
        // bracket: the middle mesh, at 1.5 spacings, is already within a few per cent.
        // Measured at a cell of 0.92 spacings on a larger packet: 0.08%.
        Assert.Equal(point, rms[1], 0.06 * point);

        // Accuracy here has an OPTIMUM rather than a floor, which is the result worth
        // keeping: refining past the match is not a free improvement, and raising the
        // node count is exactly what a reader does when they want a better answer.
        // That is why the ratio is reported on every run.
        var matched = new ParticleInCell(
            Population, Macroparticles, Elementary, 500.0 * Dalton,
            nodes: counts[2], padding: Padding);

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"at {counts[2]} nodes the run reports {matched.CellsPerSpacing:F2} cells per spacing "
            + $"and advises {matched.MatchedNodes}");

        Assert.Equal(0.75, matched.CellsPerSpacing, 0.01);
        Assert.Equal(64, matched.MatchedNodes);
    }

    [Fact]
    public void TheCostStopsGrowingWithTheSquareOfThePacket()
    {
        // Why the approximate method exists. The direct sum is O(N^2) on every
        // evaluation and a Runge-Kutta step asks seven times; the grid is one solve
        // plus O(N) to deposit and gather. The crossing point is what matters, not
        // the constant.
        output.WriteLine("      N     direct (s)     grid (s)    ratio");

        var ratios = new List<(int N, double Ratio)>();

        foreach (var n in new[] { 250, 500, 1000, 2000 })
        {
            var start = Ball(n, 0.5e-3, seed: 5);

            var direct = Widen(
                new CoulombInteraction(
                    1.0e6, n, Elementary, 500.0 * Dalton,
                    CoulombInteraction.SpacingSoftening(0.5e-3, n)),
                start,
                5.0e-7);

            var grid = Widen(
                new ParticleInCell(1.0e6, n, Elementary, 500.0 * Dalton, nodes: 32),
                start,
                5.0e-7);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{n,7}   {direct.Seconds,12:F3}   {grid.Seconds,10:F3}   "
                + $"{direct.Seconds / grid.Seconds,6:F2}"));

            ratios.Add((n, direct.Seconds / grid.Seconds));
        }

        // The direct sum's share of the work grows with N and the grid's does not, so
        // whatever the constants are on this machine, the ratio has to rise. That is
        // the asymptotic claim, and it is the only one a wall-clock measurement on a
        // shared runner can honestly make - the absolute times are not asserted.
        Assert.True(
            ratios[^1].Ratio > ratios[0].Ratio,
            $"the direct sum should lose ground as the packet grows: "
            + $"{ratios[0].Ratio:F2} at {ratios[0].N} against {ratios[^1].Ratio:F2} at {ratios[^1].N}");
    }

    [Fact]
    public void TheStepCountTracksTheCellSize()
    {
        // The hypothesis: a trilinear gather is continuous but its DERIVATIVE jumps
        // at every cell face, and an embedded Runge-Kutta estimator reads those kinks
        // as error and refuses to stride. If that is what costs the steps, then a
        // finer grid - more faces per unit path - must cost more of them, and the
        // count should track the node count rather than being a fixed overhead.
        var start = Ball(400, 0.5e-3, seed: 17);

        output.WriteLine("  nodes    steps    solves    seconds");

        var counts = new List<(int Nodes, int Steps)>();

        foreach (var nodes in new[] { 16, 32, 64 })
        {
            var run = Widen(
                new ParticleInCell(1.0e6, 400, Elementary, 500.0 * Dalton, nodes: nodes),
                start,
                5.0e-7);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{nodes,7}  {run.Steps,7}  {run.Solves,8}  {run.Seconds,9:F2}"));

            counts.Add((nodes, run.Steps));
        }

        var direct = Widen(
            new CoulombInteraction(
                1.0e6, 400, Elementary, 500.0 * Dalton,
                CoulombInteraction.SpacingSoftening(0.5e-3, 400)),
            start,
            5.0e-7);

        output.WriteLine($" direct  {direct.Steps,7}  {"-",8}  {direct.Seconds,9:F2}");

        Assert.True(counts[^1].Steps > counts[0].Steps);
    }
}
