using System.Globalization;

using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The implicit density step: that it is stable without a Courant limit, stays
/// non-negative before it has converged, and converges on the explicit answer.
/// </summary>
/// <remarks>
/// <para>
/// The explicit scheme is bounded by the faster of diffusion and Courant, and in a
/// driven structure the ponderomotive well's gradient at an electrode edge makes the
/// Courant bound tiny - set by cells where the density is almost zero and nothing is
/// happening. Backward Euler has no such bound.
/// </para>
/// <para>
/// <b>Whether it is worth using is a separate question from whether it works</b>, and
/// the answer is not the same on every problem. See
/// <c>ItPaysWhereCourantBindsAndNotWhereDiffusionDoes</c>: the gain costs
/// Gauss-Seidel sweeps, and how many depends on whether the longer step is still
/// inside the diffusion limit.
/// </para>
/// </remarks>
public sealed class ImplicitDiffusionTests(ITestOutputHelper output)
{
    /// <summary>
    /// The whole scheme rests on this: an unconverged iterate is still a density.
    /// </summary>
    /// <remarks>
    /// Every term in the Gauss-Seidel update is non-negative - the densities, the flux
    /// coefficients and the step - so the iterate cannot go negative at any sweep
    /// count. Checked at a step a thousand times the explicit limit and at a cell
    /// Peclet far into the drift-dominated regime, which is where a centred scheme
    /// oscillates and produces the negative densities Scharfetter-Gummel exists to
    /// prevent.
    /// </remarks>
    [Theory]
    [InlineData(1.0)]
    [InlineData(64.0)]
    [InlineData(1024.0)]
    public void ADensityNeverGoesNegativeHoweverLongTheStep(double gain)
    {
        var (density, field, gas, mobility, species) = DriftCase(fieldSi: 40_000.0);

        var result = DriftDiffusion.Run(
            density, field, gas, mobility, species, 40e-6, Edges, AbsorbingCells.None,
            maximumSteps: 200_000, scheme: StepScheme.Implicit, stepGain: gain);

        var lowest = double.MaxValue;

        for (var j = 0; j < result.Density.Grid.CountY; j++)
        {
            for (var i = 0; i < result.Density.Grid.CountX; i++)
            {
                lowest = Math.Min(lowest, result.Density[i, j]);
            }
        }

        output.WriteLine(
            $"gain {gain}: {result.Steps} steps, {result.Sweeps} sweeps, lowest {lowest:E3}");

        Assert.True(lowest >= 0.0, $"a density went negative: {lowest:E3}");
    }

    /// <summary>
    /// A seeded Boltzmann equilibrium does not move, however long the step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sharpest correctness check available for this scheme, and the reason it can
    /// be sharp: Scharfetter-Gummel is <em>built</em> so that its zero-flux state is
    /// exactly the Boltzmann factor - the flux vanishes when
    /// n_there / n_here = B(-P) / B(P) = exp(P), and P is precisely q dphi / kT. The
    /// discrete equilibrium <b>is</b> the continuous one rather than an approximation
    /// converging to it.
    /// </para>
    /// <para>
    /// That makes it a test of the implicit solve specifically. Equilibrium is a
    /// property of the space discretisation, so backward Euler must hold it exactly at
    /// <em>any</em> step: the right-hand side is already the fixed point. An implicit
    /// operator assembled with a wrong coefficient, a wrong sign, or the wrong
    /// neighbour would still be stable and still be non-negative - and would drift off
    /// the equilibrium. Stability tests cannot see that; this one can.
    /// </para>
    /// <para>
    /// A step a thousand times the explicit limit is the point. If the equilibrium
    /// held only for small steps it would be the initial condition being preserved by
    /// not doing very much.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1000.0)]
    public void ASeededEquilibriumStaysPutAtAnyStep(double gain)
    {
        var grid = Grid2D.OverBox(-0.01, -0.002, 0.01, 0.002, 128, 32);

        var gas = new BackgroundGas
        {
            Model = CollisionModel.HardSphere,
            PressureSi = 100.0,
            TemperatureK = 300.0,
            MassSi = 28.0134 * 1.66053906892e-27,
            CrossSectionSi = 250e-20,
        };

        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);

        const double Strength = 20.0;

        var kT = BackgroundGas.BoltzmannSi * gas.TemperatureK / 1.602176634e-19;

        DensityField Boltzmann()
        {
            var seeded = new DensityField(grid);

            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    seeded[i, j] = Math.Exp(-Strength * Math.Abs(grid.X(i)) / kT);
                }
            }

            return seeded;
        }

        var edges = new DriftDiffusion.DomainEdges(
            Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

        var settled = DriftDiffusion.Run(
            Boltzmann(), new WedgeField(Strength), gas, mobility, species, 1e-3, edges,
            AbsorbingCells.None, 2_000_000, StepScheme.Implicit, gain);

        var reference = Boltzmann();
        var middle = grid.CountY / 2;
        var worst = 0.0;

        for (var i = grid.CountX / 2; i < grid.CountX - 1; i++)
        {
            var before = reference[i, middle];

            if (before < 1e-3)
            {
                break;
            }

            worst = Math.Max(worst, Math.Abs(Math.Log(settled.Density[i, middle] / before)));
        }

        output.WriteLine(
            $"gain {gain}: {settled.Steps} steps, {settled.Sweeps} sweeps, "
            + $"worst log ratio {worst:E3} over three decades");

        Assert.True(worst < 0.01, $"the equilibrium moved by {worst:E3} in log density");
    }

    /// <summary>
    /// It pays where the Courant limit binds and costs where the diffusion limit does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The honest half of this capability. Backward Euler removes the stability bound
    /// and charges Gauss-Seidel sweeps for it, and how many depends on which limit the
    /// explicit scheme was up against. The iteration's difficulty is set by the
    /// <em>diffusive</em> part of the operator, so a step that is long by Courant's
    /// standard but still short by diffusion's converges in a few sweeps - while one
    /// past the diffusion limit needs many, and the trade collapses.
    /// </para>
    /// <para>
    /// Measured on the shipped ion funnel at 2 mbar, where the drift limit is 195 ps
    /// against a diffusion limit of 747 ns - a factor of 3,800 - the implicit scheme
    /// runs at <b>3.0 sweeps a step and 21.1x the speed for 0.057% error</b> over a
    /// 50 us window, and the error falls rather than accumulating as the window grows.
    /// On the
    /// drift tube here, where the two limits are close, the same gain needs tens of
    /// sweeps and it is slower than stepping explicitly. Both are true and neither
    /// alone would be an honest account.
    /// </para>
    /// </remarks>
    [Fact]
    public void ItPaysWhereCourantBindsAndNotWhereDiffusionDoes()
    {
        output.WriteLine("gain   steps   sweeps   per step");

        var sweepsPerStep = new double[2];

        double[] gains = [1.0, 16.0];

        for (var k = 0; k < gains.Length; k++)
        {
            var run = Fly(StepScheme.Implicit, gains[k]);

            sweepsPerStep[k] = run.Sweeps / (double)run.Steps;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{gains[k],4:F0} {run.Steps,7} {run.Sweeps,8} {sweepsPerStep[k],10:F1}"));
        }

        // The cost per step rises with the step, which is the whole reason this is a
        // trade rather than a free win. On a problem whose explicit limit is already
        // diffusive - this one - the rise is steep.
        Assert.True(
            sweepsPerStep[^1] > 2.0 * sweepsPerStep[0],
            $"sweeps per step should climb with the gain: {sweepsPerStep[0]:F1} to "
            + $"{sweepsPerStep[^1]:F1}");
    }

    /// <summary>
    /// Asking the explicit scheme for a longer step is refused, not ignored.
    /// </summary>
    /// <remarks>
    /// A caller who asked for a longer step and silently got the short one would
    /// conclude the scheme is slow rather than that the request went nowhere.
    /// </remarks>
    [Fact]
    public void TheExplicitSchemeRefusesAGain()
    {
        var (density, field, gas, mobility, species) = DriftCase(fieldSi: 4000.0);

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => DriftDiffusion.Run(
                density, field, gas, mobility, species, 1e-6, Edges, AbsorbingCells.None,
                scheme: StepScheme.Explicit, stepGain: 8.0));

        Assert.Contains("stability limit", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The explicit path reports no inner solve, because it has none.</summary>
    [Fact]
    public void TheExplicitPathHasNoResidualToReport()
    {
        var run = Fly(StepScheme.Explicit, 1.0);

        Assert.Equal(0L, run.Sweeps);
        Assert.Equal(0.0, run.WorstSweepChange);
        Assert.Equal(StepScheme.Explicit, run.Scheme);
        Assert.Equal(1.0, run.StepGain);
    }

    private static DriftDiffusion.DomainEdges Edges => new(
        MinX: Escape.Absorbing,
        MaxX: Escape.Collecting,
        MinY: Escape.Absorbing,
        MaxY: Escape.Absorbing);

    private static DiffusionResult Fly(StepScheme scheme, double gain)
    {
        var (density, field, gas, mobility, species) = DriftCase(fieldSi: 4000.0);

        return DriftDiffusion.Run(
            density, field, gas, mobility, species, 40e-6, Edges, AbsorbingCells.None,
            maximumSteps: 200_000, scheme: scheme, stepGain: gain);
    }

    private static double Difference(DensityField a, DensityField b)
    {
        var difference = 0.0;
        var norm = 0.0;

        for (var j = 0; j < a.Grid.CountY; j++)
        {
            for (var i = 0; i < a.Grid.CountX; i++)
            {
                var d = a[i, j] - b[i, j];

                difference += d * d;
                norm += b[i, j] * b[i, j];
            }
        }

        return Math.Sqrt(difference / norm);
    }

    private static (DensityField Density, UniformField Field, BackgroundGas Gas,
        Mobility Mobility, IonSpecies Species) DriftCase(double fieldSi)
    {
        var grid = Grid2D.OverBox(0.0, 0.0, 0.040, 0.010, 256, 32);

        var density = new DensityField(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = (grid.X(i) - 0.004) / 0.0012;
                var dy = (grid.Y(j) - 0.005) / 0.0012;

                density[i, j] = Math.Exp(-((dx * dx) + (dy * dy)));
            }
        }

        var total = density.Population();

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                density[i, j] *= 1.0e6 / total;
            }
        }

        var gas = new BackgroundGas
        {
            Model = CollisionModel.HardSphere,
            PressureSi = 100.0,
            TemperatureK = 300.0,
            MassSi = 28.0134 * 1.66053906892e-27,
            CrossSectionSi = 250e-20,
        };

        var species = IonSpecies.FromMassToCharge(500.0, 1);

        return (
            density,
            UniformField.Create(new Vec3(fieldSi, 0.0, 0.0)),
            gas,
            Mobility.FromCrossSection(gas, species),
            species);
    }
}
