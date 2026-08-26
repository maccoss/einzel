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
