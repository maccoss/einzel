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
