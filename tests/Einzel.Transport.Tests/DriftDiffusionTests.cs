using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The diffusive transport mode, against closed forms.
/// </summary>
/// <remarks>
/// Three exact targets: a Gaussian spreading as the square root of time, a packet
/// drifting at the mobility times the field, and the Boltzmann distribution a
/// density settles into in a potential well. The third is the sharpest, for the
/// same reason equipartition was sharpest for the collision models - it is a
/// statement the solver does not contain anywhere, so reproducing it means drift
/// and diffusion are in the right ratio rather than merely both present.
/// </remarks>
public sealed class DriftDiffusionTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;
    private const double ElementaryCharge = 1.602176634e-19;

    private static BackgroundGas Nitrogen(double pressurePa) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
    };

    /// <summary>A density concentrated at one node, holding one ion.</summary>
    private static DensityField PointSource(Grid2D grid, int i, int j)
    {
        var density = new DensityField(grid);

        density[i, j] = 1.0 / (grid.SpacingX * grid.SpacingY);

        return density;
    }

    [Fact]
    public void FreeDiffusionSpreadsAsTheSquareRootOfTime()
    {
        // The exact result for a point released into a quiet gas: the variance grows
        // linearly in time, so the width grows as its square root. Nothing in the
        // solver says so - it says what the flux between two cells is.
        var grid = Grid2D.OverBox(-0.02, -0.02, 0.02, 0.02, 128);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);
        var diffusion = Mobility.DiffusionSi(gas.TemperatureK, species.ChargeSi, mobility.ZeroFieldSi);

        output.WriteLine($"K = {mobility.ZeroFieldSi:E3} m^2/(V s), D = {diffusion:E3} m^2/s");
        output.WriteLine("time / ms    sigma_x measured    sqrt(2 D t)      ratio");

        var start = PointSource(grid, grid.CountX / 2, grid.CountY / 2);

        foreach (var milliseconds in new[] { 0.5, 1.0, 2.0 })
        {
            var seconds = milliseconds * 1e-3;

            var result = DriftDiffusion.Run(
                start, FieldFreeSpace.Instance, gas, mobility, species, seconds,
                new DriftDiffusion.DomainEdges(
                    Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting));

            var (spreadX, _) = result.Density.Spread();
            var expected = Math.Sqrt(2.0 * diffusion * seconds);

            output.WriteLine(
                $"{milliseconds,8:F1}    {spreadX * 1e3,15:F4} mm    {expected * 1e3,8:F4} mm   {spreadX / expected,7:F4}");

            // The grid is finite and the initial condition is a single cell rather
            // than a delta, so the measured width carries the cell width in
            // quadrature. A few per cent at these times.
            Assert.InRange(spreadX / expected, 0.92, 1.08);
        }
    }

    /// <summary>The density can be looked at while it is still moving.</summary>
    /// <remarks>
    /// <para>
    /// A run reports the density it <em>ended</em> with. For a model whose ions have all
    /// arrived that is an empty box - correctly, and uselessly, because the interesting
    /// picture is the packet in flight. The only way to see one was to shorten
    /// <c>maximumFlightTime</c> and lose everything after it.
    /// </para>
    /// <para>
    /// Three things are asserted, and the third is the one with teeth. The centroid
    /// advances at the drift speed <em>between</em> snapshots, which says each is the
    /// density at its own instant rather than a copy of one; the last snapshot is the
    /// final density to the last bit, which says nothing is lost between the last
    /// snapshot and the end; and the whole run is <b>bit-identical</b> to one that took
    /// no snapshots, which says the recording does not perturb what it records.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDensityCanBeRecordedWhileItIsStillMoving()
    {
        var grid = Grid2D.OverBox(-0.05, -0.02, 0.05, 0.02, 256, 128);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);
        var strength = 200.0;
        var field = UniformField.Create(new Vec3(strength, 0.0, 0.0));

        var seconds = 1e-4;
        var edges = new DriftDiffusion.DomainEdges(
            Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

        double[] wanted = [0.0, 0.25e-4, 0.5e-4, 0.75e-4, 1e-4];

        var recorded = DriftDiffusion.Run(
            PointSource(grid, grid.CountX / 4, grid.CountY / 2),
            field, gas, mobility, species, seconds, edges,
            snapshotSeconds: wanted);

        Assert.Equal(wanted.Length, recorded.Snapshots.Count);

        var expected = mobility.ZeroFieldSi * strength;

        for (var k = 0; k < recorded.Snapshots.Count; k++)
        {
            var snapshot = recorded.Snapshots[k];
            var (x, _) = snapshot.Density.Centroid();

            output.WriteLine(
                $"asked {snapshot.RequestedSeconds * 1e6,7:F3} us, taken at "
                + $"{snapshot.AtSeconds * 1e6,7:F3} us, centroid {x * 1e3,8:F4} mm");

            // Taken at or after what was asked for, and by less than one step.
            Assert.True(snapshot.AtSeconds >= snapshot.RequestedSeconds);
            Assert.True(snapshot.AtSeconds - snapshot.RequestedSeconds < recorded.StepSeconds);

            if (k == 0)
            {
                continue;
            }

            var (previous, _) = recorded.Snapshots[k - 1].Density.Centroid();
            var span = snapshot.AtSeconds - recorded.Snapshots[k - 1].AtSeconds;

            // Each is the density at its own instant: the centroid advances at mu E
            // between one and the next. A snapshot list of copies of one field would
            // give zero here.
            Assert.InRange((x - previous) / span / expected, 0.97, 1.03);
        }

        // The last one is the end, to the last bit.
        var final = recorded.Snapshots[^1].Density;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                Assert.Equal(recorded.Density[i, j], final[i, j]);
            }
        }

        // And recording changed nothing. Not "to a tolerance": the snapshots are clones
        // taken between steps, so the arithmetic of the run is untouched and any
        // difference at all would mean the recording had entered it.
        var plain = DriftDiffusion.Run(
            PointSource(grid, grid.CountX / 4, grid.CountY / 2),
            field, gas, mobility, species, seconds, edges);

        Assert.Empty(plain.Snapshots);
        Assert.Equal(plain.Steps, recorded.Steps);
        Assert.Equal(plain.Collected, recorded.Collected);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                Assert.Equal(plain.Density[i, j], recorded.Density[i, j]);
            }
        }
    }

    [Fact]
    public void APacketDriftsAtTheMobilityTimesTheField()
    {
        // The other half of the operator, isolated: with a uniform field the centroid
        // moves at exactly mu E, whatever diffusion is doing around it.
        var grid = Grid2D.OverBox(-0.05, -0.02, 0.05, 0.02, 256, 128);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);

        var strength = 200.0;
        var field = UniformField.Create(new Vec3(strength, 0.0, 0.0));

        var seconds = 1e-4;

        var start = PointSource(grid, grid.CountX / 4, grid.CountY / 2);
        var (fromX, _) = start.Centroid();

        var result = DriftDiffusion.Run(
            start, field, gas, mobility, species, seconds,
            new DriftDiffusion.DomainEdges(
                Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting));

        var (toX, _) = result.Density.Centroid();

        var measured = (toX - fromX) / seconds;
        var expected = mobility.ZeroFieldSi * strength;

        output.WriteLine($"field         {strength:F0} V/m");
        output.WriteLine($"mu E          {expected:F3} m/s");
        output.WriteLine($"measured      {measured:F3} m/s over {result.Steps} steps");
        output.WriteLine($"ratio         {measured / expected:F5}");

        Assert.InRange(measured / expected, 0.97, 1.03);
    }

    [Fact]
    public void TheBoltzmannDistributionIsExactlyStationary()
    {
        // The sharpest check available, and the one that says drift and diffusion are
        // in the right ratio rather than merely both present.
        //
        // Scharfetter-Gummel is built so that its zero-flux state is exactly the
        // Boltzmann factor: setting the flux to zero gives n_there / n_here =
        // B(-P) / B(P) = exp(P), and P is precisely q dphi / kT. So the discrete
        // equilibrium is the continuous one, not an approximation converging to it -
        // and the way to test that is to seed the equilibrium and watch it not move.
        //
        // Relaxing *to* it from a uniform density is a different and much slower
        // measurement: it takes the drift time across the domain, which for a field
        // weak enough to resolve the exponential per cell is milliseconds. A first
        // draft ran for a fifth of that, found a nearly flat density, and was
        // measuring the transient rather than the equilibrium.
        var grid = Grid2D.OverBox(-0.01, -0.002, 0.01, 0.002, 128, 32);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);

        var strength = 20.0;
        var field = new WedgeField(strength);

        var kT = BackgroundGas.BoltzmannSi * gas.TemperatureK / ElementaryCharge;

        DensityField Boltzmann()
        {
            var seeded = new DensityField(grid);

            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    seeded[i, j] = Math.Exp(-strength * Math.Abs(grid.X(i)) / kT);
                }
            }

            return seeded;
        }

        var edges = new DriftDiffusion.DomainEdges(
            Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

        var seeded = Boltzmann();

        var settled = DriftDiffusion.Run(
            seeded, field, gas, mobility, species, 1e-3, edges);

        output.WriteLine($"kT/q = {kT * 1e3:F3} mV, well {strength * 0.01:F3} V = {strength * 0.01 / kT:F1} kT");
        output.WriteLine($"{strength * grid.SpacingX / kT:F3} kT per cell, so the exponential is resolved");
        output.WriteLine($"{settled.Steps} steps");
        output.WriteLine(string.Empty);
        output.WriteLine("x / mm      seeded        after         ratio");

        var reference = Boltzmann();
        var middle = grid.CountY / 2;
        var worst = 0.0;

        for (var i = grid.CountX / 2; i < grid.CountX - 1; i++)
        {
            var before = reference[i, middle];
            var after = settled.Density[i, middle];

            if (before < 1e-3)
            {
                break;
            }

            worst = Math.Max(worst, Math.Abs(Math.Log(after / before)));

            if (i % 8 == 0)
            {
                output.WriteLine(
                    $"{grid.X(i) * 1e3,7:F2}   {before,11:E3}   {after,11:E3}   {after / before,7:F5}");
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"worst departure over three decades of density: {Math.Exp(worst):F5}x");

        // A per cent would be generous; this should be near machine precision away
        // from the boundaries, and anything worse is a bug rather than a
        // discretisation error.
        Assert.True(Math.Exp(worst) < 1.01, $"the equilibrium moved by {Math.Exp(worst):F4}x");

        // The control that makes the above mean something: a density that is NOT the
        // equilibrium does move, so the test is not passing because the solver is
        // doing nothing.
        var uniform = new DensityField(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                uniform[i, j] = 1.0;
            }
        }

        var moved = DriftDiffusion.Run(uniform, field, gas, mobility, species, 1e-3, edges);

        var edgeBefore = 1.0;
        var edgeAfter = moved.Density[grid.CountX - 2, middle];

        output.WriteLine($"control: a uniform density at the well edge went {edgeBefore:F3} -> {edgeAfter:F3}");

        Assert.True(
            edgeAfter < 0.9 * edgeBefore,
            "a uniform density did not move, so the stationarity above proves nothing");
    }

    [Fact]
    public void AStillGasIsBitIdenticalToNoGasVelocityAtAll()
    {
        // Asserted rather than assumed, the same way a vacuum flight is asserted to
        // be bit-identical with a collision sampler attached. Adding an advection
        // term to the flux is exactly the kind of change that perturbs the answer
        // everywhere by rounding, and a diffusive result nobody can reproduce across
        // a build is one nothing downstream can pin.
        var grid = Grid2D.OverBox(-0.02, -0.01, 0.04, 0.01, 128, 64);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var field = UniformField.Create(new Vec3(300.0, 0.0, 0.0));

        var still = Nitrogen(100.0);
        var declared = still with { DriftVelocitySi = Vec3.Zero };

        var mobility = Mobility.FromCrossSection(still, species);

        var edges = new DriftDiffusion.DomainEdges(
            Escape.Absorbing, Escape.Collecting, Escape.Absorbing, Escape.Absorbing);

        var a = DriftDiffusion.Run(
            PointSource(grid, 8, grid.CountY / 2), field, still, mobility, species, 1e-4, edges);

        var b = DriftDiffusion.Run(
            PointSource(grid, 8, grid.CountY / 2), field, declared, mobility, species, 1e-4, edges);

        Assert.Equal(a.Steps, b.Steps);
        Assert.Equal(a.Collected, b.Collected);

        for (var k = 0; k < a.Density.Values.Length; k++)
        {
            Assert.Equal(a.Density.Values[k], b.Density.Values[k]);
        }

        output.WriteLine(
            $"{a.Steps} steps, {a.Density.Values.Length} nodes, every value identical to the bit");
    }

    [Fact]
    public void AMovingGasCarriesTheDensityAtItsOwnSpeed()
    {
        // Pure advection, with the field switched off so nothing else can move the
        // ions: the centroid travels at exactly the declared gas velocity. This is
        // the closed form for the term, and it is the one GAS-1 says is easy to omit
        // and hard to notice missing - a model that ignores it does not fail, it
        // answers about an instrument whose gas is standing still.
        var grid = Grid2D.OverBox(-0.02, -0.02, 0.06, 0.02, 256, 128);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var still = Nitrogen(100.0);
        var mobility = Mobility.FromCrossSection(still, species);

        var edges = new DriftDiffusion.DomainEdges(
            Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

        var seconds = 2e-4;

        output.WriteLine("gas / m/s     measured / m/s    ratio");

        foreach (var speed in new[] { 40.0, 120.0 })
        {
            var flowing = still with { DriftVelocitySi = new Vec3(speed, 0.0, 0.0) };

            var start = PointSource(grid, grid.CountX / 4, grid.CountY / 2);
            var (fromX, _) = start.Centroid();

            var result = DriftDiffusion.Run(
                start, FieldFreeSpace.Instance, flowing, mobility, species, seconds, edges);

            var (toX, _) = result.Density.Centroid();
            var measured = (toX - fromX) / seconds;

            output.WriteLine($"{speed,9:F1}    {measured,14:F3}    {measured / speed,8:F6}");

            // Tight on purpose. Scharfetter-Gummel is exact for a drift that varies
            // linearly across a cell, and a uniform one trivially is, so the first
            // moment is not an approximation converging with the mesh - it is the
            // scheme's own answer. A band wide enough for a discretisation error
            // would accept a term that is merely the right size.
            Assert.InRange(measured / speed, 0.999, 1.001);
        }
    }

    [Fact]
    public void GasAndFieldDriftsAdd()
    {
        // The two mechanisms are separate terms in one exponent, so the centroid
        // moves at their sum. Worth testing against the sum rather than against
        // either part: a scheme that used the gas velocity *instead* of the field
        // drift, or that double-counted it, passes the pure-advection test above.
        //
        // Run against the gas both ways round, because a sign error in the advection
        // term is invisible when the two push the same way.
        var grid = Grid2D.OverBox(-0.03, -0.02, 0.05, 0.02, 256, 128);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var still = Nitrogen(100.0);
        var mobility = Mobility.FromCrossSection(still, species);

        var strength = 200.0;
        var field = UniformField.Create(new Vec3(strength, 0.0, 0.0));
        var drift = mobility.ZeroFieldSi * strength;

        var edges = new DriftDiffusion.DomainEdges(
            Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

        var seconds = 1e-4;

        output.WriteLine($"mu E = {drift:F3} m/s");
        output.WriteLine("gas / m/s    expected / m/s    measured / m/s    ratio");

        foreach (var speed in new[] { 60.0, -60.0 })
        {
            var flowing = still with { DriftVelocitySi = new Vec3(speed, 0.0, 0.0) };

            var start = PointSource(grid, grid.CountX / 2, grid.CountY / 2);
            var (fromX, _) = start.Centroid();

            var result = DriftDiffusion.Run(
                start, field, flowing, mobility, species, seconds, edges);

            var (toX, _) = result.Density.Centroid();

            var measured = (toX - fromX) / seconds;
            var expected = drift + speed;

            output.WriteLine(
                $"{speed,9:F1}    {expected,14:F3}    {measured,14:F3}    {measured / expected,8:F6}");

            Assert.InRange(measured / expected, 0.999, 1.001);
        }
    }

    [Fact]
    public void ADriftWellPastTheUpwindLimitIsStillExact()
    {
        // The test the other advection checks could not do, because their cell
        // Peclet numbers were too small to reach the case that was broken.
        //
        // Scharfetter-Gummel's exponent P = v h / D is the ratio of drift to
        // diffusion across one cell, and the Bernoulli function it feeds handles a
        // large one exactly: zero above +40 and -x below -40 are the true limits,
        // not approximations. An earlier version clamped the argument to +/-40
        // before calling it, which protected nothing and capped the effective drift
        // at 40 D / h - so above a cell Peclet of 40 the density moved too slowly,
        // by exactly the ratio the clamp imposed.
        //
        // The existing checks ran at P = 16 and never saw it. What found it was an
        // example whose expected number was a division: a drift tube with a gas flow
        // came out 6.7% long against L / (mu E + v_gas).
        var grid = Grid2D.OverBox(-0.02, -0.02, 0.06, 0.02, 64, 32);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var still = Nitrogen(100.0);
        var mobility = Mobility.FromCrossSection(still, species);

        var diffusion = Mobility.DiffusionSi(
            still.TemperatureK, species.ChargeSi, mobility.ZeroFieldSi);

        var edges = new DriftDiffusion.DomainEdges(
            Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

        var seconds = 1e-4;

        output.WriteLine("gas / m/s   cell Peclet   measured / m/s     ratio");

        foreach (var speed in new[] { 200.0, 400.0 })
        {
            var peclet = speed * grid.SpacingX / diffusion;

            Assert.True(peclet > 60.0, $"a cell Peclet of {peclet:F1} does not reach the case");

            var flowing = still with { DriftVelocitySi = new Vec3(speed, 0.0, 0.0) };

            var start = PointSource(grid, grid.CountX / 4, grid.CountY / 2);
            var (fromX, _) = start.Centroid();

            var result = DriftDiffusion.Run(
                start, FieldFreeSpace.Instance, flowing, mobility, species, seconds, edges);

            var (toX, _) = result.Density.Centroid();
            var measured = (toX - fromX) / seconds;

            output.WriteLine(
                $"{speed,9:F1}   {peclet,11:F1}   {measured,14:F3}  {measured / speed,8:F6}");

            Assert.InRange(measured / speed, 0.999, 1.001);
        }
    }

    [Fact]
    public void AMovingGasStillConservesIons()
    {
        // The advection term is only conservative if the two cells sharing a face
        // compute the same crossing with opposite signs, which is why the gas is
        // averaged over the face rather than sampled at the cell asking. Getting
        // that wrong does not produce a visibly odd density - it produces one that
        // quietly gains or loses ions, which is exactly how the first version of the
        // *field* term failed while its conservation test passed on a uniform field.
        //
        // So this one runs the gas across a field that varies, where a cell-centred
        // sample and a face-averaged one differ.
        var grid = Grid2D.OverBox(-0.02, -0.005, 0.02, 0.005, 128, 32);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var gas = Nitrogen(100.0) with { DriftVelocitySi = new Vec3(80.0, 0.0, 0.0) };
        var mobility = Mobility.FromCrossSection(gas, species);

        var start = PointSource(grid, grid.CountX / 2, grid.CountY / 2);
        var launched = start.Population();

        var result = DriftDiffusion.Run(
            start, new WedgeField(500.0), gas, mobility, species, 2e-4,
            new DriftDiffusion.DomainEdges(
                Escape.Absorbing, Escape.Collecting, Escape.Absorbing, Escape.Absorbing));

        var total = result.Remaining + result.Collected + result.Lost.Values.Sum();

        output.WriteLine($"launched  {launched:E6}");
        output.WriteLine($"total     {total:E6}  ({total / launched:P4} of launched)");

        Assert.Equal(launched, total, 1e-3 * launched);
    }

    [Fact]
    public void IonsAreConservedUntilTheyLeave()
    {
        // Every ion is somewhere: still in the domain, collected, or absorbed on a
        // named wall. A drift-diffusion scheme that leaks is one whose transmission
        // figure is meaningless, and the leak is invisible without this sum.
        var grid = Grid2D.OverBox(-0.01, -0.005, 0.03, 0.005, 128, 32);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(300.0, 0.0, 0.0));

        var start = PointSource(grid, 4, grid.CountY / 2);
        var launched = start.Population();

        var result = DriftDiffusion.Run(
            start, field, gas, mobility, species, 2e-4,
            new DriftDiffusion.DomainEdges(
                Escape.Absorbing, Escape.Collecting, Escape.Absorbing, Escape.Absorbing));

        var lost = result.Lost.Values.Sum();
        var total = result.Remaining + result.Collected + lost;

        output.WriteLine($"launched     {launched:E6}");
        output.WriteLine($"remaining    {result.Remaining:E6}");
        output.WriteLine($"collected    {result.Collected:E6}  ({result.Collected / launched:P2})");

        foreach (var (where, ions) in result.Lost.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"lost on {where,-6} {ions:E6}");
        }

        output.WriteLine($"total        {total:E6}  ({total / launched:P4} of launched)");

        Assert.Equal(launched, total, 1e-3 * launched);

        // And ACC-5's rule survives the change of description: a loss is named by
        // where it went, not aggregated into a transmission figure.
        Assert.NotEmpty(result.Lost);
    }

    /// <summary>A wall of blocked cells across the channel, at one column.</summary>
    private static AbsorbingCells Barrier(Grid2D grid, int column, string name)
    {
        var owner = new int[grid.CountX * grid.CountY];

        Array.Fill(owner, -1);

        for (var j = 0; j < grid.CountY; j++)
        {
            owner[(j * grid.CountX) + column] = 0;
        }

        return new AbsorbingCells(owner, [name]);
    }

    [Fact]
    public void AnInteriorElectrodeStopsTheDensityAndIsNamedForIt()
    {
        // The control is the same run without the barrier. On its own, "almost
        // nothing was collected" is equally consistent with a solver that lost the
        // density somewhere, and this is a scheme whose whole point is not doing
        // that - so what is asserted is the difference the metal makes.
        var grid = Grid2D.OverBox(-0.01, -0.005, 0.03, 0.005, 128, 32);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var gas = Nitrogen(100.0);
        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(300.0, 0.0, 0.0));

        // Every wall but the exit is sealed, so the only two ways out are the
        // detector and the metal. Anything the barrier does then shows up as a
        // transfer between exactly those two columns.
        var edges = new DriftDiffusion.DomainEdges(
            Escape.Reflecting, Escape.Collecting, Escape.Reflecting, Escape.Reflecting);

        var open = DriftDiffusion.Run(
            PointSource(grid, 4, grid.CountY / 2), field, gas, mobility, species, 2e-3, edges);

        var blocked = DriftDiffusion.Run(
            PointSource(grid, 4, grid.CountY / 2), field, gas, mobility, species, 2e-3, edges,
            Barrier(grid, grid.CountX / 2, "skimmer"));

        var launched = PointSource(grid, 4, grid.CountY / 2).Population();

        output.WriteLine($"open      collected {open.Collected / launched:P2}");
        output.WriteLine($"blocked   collected {blocked.Collected / launched:P2}");

        foreach (var (where, ions) in blocked.Lost.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"          lost on {where,-10} {ions / launched:P2}");
        }

        Assert.True(
            open.Collected > 0.9 * launched,
            $"the control should transmit nearly everything, and transmitted {open.Collected / launched:P2}");

        Assert.True(
            blocked.Collected < 0.01 * launched,
            $"a wall across the channel let {blocked.Collected / launched:P2} through");

        // ACC-5: named by the surface the model author wrote, not aggregated.
        Assert.True(blocked.Lost.TryGetValue("skimmer", out var stopped));
        Assert.True(stopped > 0.9 * launched, $"only {stopped / launched:P2} landed on the skimmer");
    }

    [Fact]
    public void AnInteriorElectrodeAbsorbsForTheWholeRunNotOnlyTheSeed()
    {
        // The distinction that matters, and the one that was missing: emptying the
        // seed stops a source inside metal from starting there, and does nothing at
        // all about density that arrives later. With the electrode downstream of the
        // source there is nothing inside it to empty at t = 0, so a seed-only
        // treatment transmits everything and this test separates the two.
        var grid = Grid2D.OverBox(-0.01, -0.005, 0.03, 0.005, 128, 32);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var gas = Nitrogen(100.0);
        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(300.0, 0.0, 0.0));

        var start = PointSource(grid, 4, grid.CountY / 2);
        var absorbers = Barrier(grid, grid.CountX / 2, "ring");

        // Nothing of the seed is inside the barrier: it is 60 cells downstream.
        Assert.Equal(0.0, start[grid.CountX / 2, grid.CountY / 2]);

        var result = DriftDiffusion.Run(
            start, field, gas, mobility, species, 2e-3,
            new DriftDiffusion.DomainEdges(
                Escape.Reflecting, Escape.Collecting, Escape.Reflecting, Escape.Reflecting),
            absorbers);

        output.WriteLine($"landed on the ring {result.Lost.GetValueOrDefault("ring") / start.Population():P2}");

        Assert.True(result.Lost.GetValueOrDefault("ring") > 0.9 * start.Population());
    }

    [Fact]
    public void AnInteriorElectrodeNeverEmitsIons()
    {
        // The scheme gives this rather than a clamp giving it: with the density on
        // the far side of the face held at zero, the Scharfetter-Gummel flux reduces
        // to B(-P) n_here, which is non-negative whatever the potential drop across
        // the face is. So a conductor at any potential can only take.
        //
        // Driven the wrong way on purpose - the field pushes ions *back* out of the
        // barrier - which is the case a sign error would show up in.
        var grid = Grid2D.OverBox(-0.01, -0.005, 0.03, 0.005, 128, 32);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var gas = Nitrogen(100.0);
        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(-300.0, 0.0, 0.0));

        var start = PointSource(grid, 4, grid.CountY / 2);
        var launched = start.Population();

        var result = DriftDiffusion.Run(
            start, field, gas, mobility, species, 2e-3,
            new DriftDiffusion.DomainEdges(
                Escape.Collecting, Escape.Absorbing, Escape.Reflecting, Escape.Reflecting),
            Barrier(grid, grid.CountX / 2, "ring"));

        var total = result.Remaining + result.Collected + result.Lost.Values.Sum();

        output.WriteLine($"launched {launched:E6}");
        output.WriteLine($"total    {total:E6}  ({total / launched:P4})");

        // Never more than were launched, and the barrier is not a source.
        Assert.True(
            total <= launched * (1.0 + 1e-9),
            $"the run ended with {total / launched:P4} of what it started with");

        Assert.Equal(launched, total, 1e-3 * launched);
    }

    [Fact]
    public void TheRadialFaceWeightIsOneInThePlaneAndFourOnTheAxis()
    {
        // The number that discriminates, asserted exactly rather than through a
        // conservation figure that a wrong weight can still nearly pass. A face's
        // flux has to be scaled by its area over the cell's volume, and in a
        // cylindrical solve those differ: the outward weight is 1 + h/2r, the inward
        // one 1 - h/2r, and on the axis the cell is a disc rather than a ring so the
        // outward weight is 4 and the inward one 0.
        //
        // That four is the same factor the cylindrical Laplacian carries on the axis
        // - the field solver had it and this did not.
        var grid = Grid2D.OverBox(0.0, 0.0, 0.008, 0.008, 32, 32);

        var plane = new DensityField(grid);
        var axis = new DensityField(grid, cylindrical: true);

        // In the plane every face is the same size as every other.
        for (var j = 0; j < 4; j++)
        {
            Assert.Equal(1.0, plane.RadialFaceWeight(j, +1));
            Assert.Equal(1.0, plane.RadialFaceWeight(j, -1));
        }

        Assert.Equal(1.0, plane.LargestRadialWeight());

        // On the axis: no inward face at all, and an outward one four times the
        // plane's.
        Assert.Equal(0.0, axis.RadialFaceWeight(0, -1));
        Assert.Equal(4.0, axis.RadialFaceWeight(0, +1), 1e-12);
        Assert.Equal(4.0, axis.LargestRadialWeight(), 1e-12);

        // And 1 +/- h/2r away from it, which tends to one as the radius grows.
        for (var j = 1; j < 8; j++)
        {
            var radius = grid.Y(j);
            var expected = grid.SpacingY / (2.0 * radius);

            output.WriteLine(
                $"r = {radius * 1e3:F3} mm   outward {axis.RadialFaceWeight(j, +1):F6}   "
                + $"inward {axis.RadialFaceWeight(j, -1):F6}");

            Assert.Equal(1.0 + expected, axis.RadialFaceWeight(j, +1), 1e-12);
            Assert.Equal(1.0 - expected, axis.RadialFaceWeight(j, -1), 1e-12);
        }
    }

    [Fact]
    public void ACylindricalDensityConservesIonsUnderRadialTransport()
    {
        // Every conservation test above is Cartesian, where the two cells sharing a
        // face have the same volume. In a cylindrical solve a cell is a ring and its
        // volume grows with radius, so a flux that is conservative per unit area is
        // not conservative per cell unless the face areas are carried with it.
        var grid = Grid2D.OverBox(-0.005, 0.0, 0.005, 0.016, 32, 64);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var gas = Nitrogen(200.0);
        var mobility = Mobility.FromCrossSection(gas, species);

        var density = new DensityField(grid, cylindrical: true);

        // A blob off axis, so there is real radial transport in both directions.
        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = (grid.X(i) - 0.0) / 0.001;
                var dy = (grid.Y(j) - 0.008) / 0.001;

                density[i, j] = 1e12 * Math.Exp(-0.5 * ((dx * dx) + (dy * dy)));
            }
        }

        var launched = density.Population();

        // Everything sealed: nothing may leave, so the population must not move.
        var result = DriftDiffusion.Run(
            density, FieldFreeSpace.Instance, gas, mobility, species, 3e-3,
            new DriftDiffusion.DomainEdges(
                Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting));

        output.WriteLine($"launched  {launched:E8}");
        output.WriteLine($"remaining {result.Remaining:E8}  ({result.Remaining / launched:P4})");
        output.WriteLine($"steps     {result.Steps}");

        Assert.Equal(launched, result.Remaining, 1e-3 * launched);
    }

    [Fact]
    public void TheDensityNeverGoesNegative()
    {
        // What Scharfetter-Gummel is for. Centred differencing produces negative
        // densities as soon as the cell Peclet number passes two, which in a funnel
        // is everywhere - and a negative density is not a small error, it is a
        // quantity that has stopped meaning anything.
        var grid = Grid2D.OverBox(-0.01, -0.005, 0.03, 0.005, 64, 16);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);

        // A field strong enough that drift dominates diffusion by two orders of
        // magnitude across a cell, which is the regime that breaks a centred scheme.
        var field = UniformField.Create(new Vec3(20_000.0, 0.0, 0.0));

        var diffusion = Mobility.DiffusionSi(
            gas.TemperatureK, species.ChargeSi, mobility.ZeroFieldSi);

        var peclet = mobility.ZeroFieldSi * 20_000.0 * grid.SpacingX / diffusion;

        output.WriteLine($"cell Peclet number {peclet:F1}, well past the 2 a centred scheme survives");

        var result = DriftDiffusion.Run(
            PointSource(grid, 4, grid.CountY / 2), field, gas, mobility, species, 5e-5,
            new DriftDiffusion.DomainEdges(
                Escape.Absorbing, Escape.Collecting, Escape.Absorbing, Escape.Absorbing));

        var lowest = result.Density.Values.Min();

        output.WriteLine($"lowest density {lowest:E3}");

        Assert.True(peclet > 10.0, "this test is only meaningful where drift dominates");
        Assert.True(lowest >= 0.0, $"a density went to {lowest:E3}");
    }
}

/// <summary>A V-shaped potential: uniform field pointing inward from both sides.</summary>
/// <remarks>
/// A test fixture rather than a device. It exists because the Boltzmann check needs
/// a bound potential and the analytic field library has no well in it - which is
/// architecture invariant 2 working as intended, since a well is a device shape
/// rather than a field primitive.
/// </remarks>
internal sealed class WedgeField(double strengthSi) : IElectrostaticField
{
    public Vec3 ElectricFieldAt(in Vec3 position) =>
        new(position.X >= 0.0 ? -strengthSi : strengthSi, 0.0, 0.0);

    public double PotentialAt(in Vec3 position) => strengthSi * Math.Abs(position.X);

    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    public double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;

    public double ResolutionLength => double.PositiveInfinity;
}
