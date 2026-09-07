using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The density solver can step through a field that changes with time, which is what an
/// elution ramp is.
/// </summary>
/// <remarks>
/// <para>
/// The stepper assembled its face operator once, because the field did not change during a
/// run - the comment where it did so said a sequenced run "would need this inside the loop,
/// and does not exist yet". A trapped-ion-mobility analyser's scan is that run: the axial
/// gradient is walked down while the density is held, and ions elute in mobility order as
/// the field can no longer hold them.
/// </para>
/// <para>
/// Three things are asserted. Handing the solver a field <em>function</em> that returns the
/// same field every time must be <b>bit-identical</b> to the fixed-field path, or the new
/// path has changed the old one's answer. A field that reverses sign half way must bring the
/// packet back, which the fixed path cannot do and which is what makes the first assertion a
/// test of the wiring rather than of nothing. And a field that <em>rises</em> must keep the
/// step stable, because the stability limit tightens as the drift grows and a step chosen at
/// the start would otherwise be too long - a falling ramp only ever loosens it, which is how
/// that would have gone unnoticed on the first ramp tried.
/// </para>
/// </remarks>
public sealed class TimeVaryingDiffusionTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;

    private static BackgroundGas Nitrogen(double pressurePa) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
    };

    private static DensityField PointSource(Grid2D grid, int i, int j)
    {
        var density = new DensityField(grid);
        density[i, j] = 1.0 / (grid.SpacingX * grid.SpacingY);
        return density;
    }

    private static readonly DriftDiffusion.DomainEdges Reflecting = new(
        Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

    /// <summary>
    /// A field function that always returns the same field changes nothing, to the bit.
    /// </summary>
    [Fact]
    public void AConstantFieldFunctionIsBitIdenticalToTheFixedPath()
    {
        var grid = Grid2D.OverBox(-0.05, -0.02, 0.05, 0.02, 128, 64);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(200.0, 0.0, 0.0));

        var fixedRun = DriftDiffusion.Run(
            PointSource(grid, grid.CountX / 4, grid.CountY / 2),
            field, gas, mobility, species, 1e-4, Reflecting);

        var functionRun = DriftDiffusion.Run(
            PointSource(grid, grid.CountX / 4, grid.CountY / 2),
            field, gas, mobility, species, 1e-4, Reflecting,
            fieldAt: _ => field);

        Assert.Equal(fixedRun.Steps, functionRun.Steps);
        Assert.Equal(fixedRun.Collected, functionRun.Collected);

        var differing = 0;
        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (fixedRun.Density[i, j] != functionRun.Density[i, j])
                {
                    differing++;
                }
            }
        }

        output.WriteLine($"{fixedRun.Steps} steps, {differing} of {grid.CountX * grid.CountY} nodes differ");
        Assert.Equal(0, differing);
    }

    /// <summary>
    /// A field that reverses half way brings the packet back, which is the control that
    /// says the function is being consulted rather than sampled once.
    /// </summary>
    [Fact]
    public void AReversingFieldBringsThePacketBack()
    {
        // Fine enough that the reversal lands close to half way. The step is set by the
        // stability limit, so on a coarse grid a run this short is a handful of steps and
        // "half way" falls a whole step off - a first version on 128 by 64 took five steps
        // and came back 27 per cent displaced, which is the quantisation and not the
        // solver. Here it is a few hundred steps.
        var grid = Grid2D.OverBox(-0.05, -0.02, 0.05, 0.02, 512, 256);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);

        var forward = UniformField.Create(new Vec3(200.0, 0.0, 0.0));
        var backward = UniformField.Create(new Vec3(-200.0, 0.0, 0.0));

        const double Seconds = 4e-4;
        var start = PointSource(grid, grid.CountX / 2, grid.CountY / 2);
        var (x0, _) = start.Centroid();

        var held = DriftDiffusion.Run(
            start.Clone(), forward, gas, mobility, species, Seconds, Reflecting);

        var reversed = DriftDiffusion.Run(
            start.Clone(), forward, gas, mobility, species, Seconds, Reflecting,
            fieldAt: t => t < 0.5 * Seconds ? forward : backward);

        var (xHeld, _) = held.Density.Centroid();
        var (xReversed, _) = reversed.Density.Centroid();

        var advanced = xHeld - x0;
        var returned = xReversed - x0;

        output.WriteLine($"held: moved {advanced * 1e3:F4} mm; reversed: net {returned * 1e3:F4} mm "
            + $"over {reversed.Steps} steps");

        Assert.True(advanced > 1e-3, "the held field did not move the packet, so the control is empty");

        // Back to within a few per cent of where it started. Not exactly, because the
        // reversal lands on a step boundary rather than at exactly half the run.
        Assert.True(
            Math.Abs(returned) < 0.05 * advanced,
            $"the reversed field left the packet {returned * 1e3:F3} mm displaced against "
            + $"{advanced * 1e3:F3} mm for the held one");
    }

    /// <summary>
    /// A rising field keeps the step stable, so the density stays non-negative and the
    /// ions stay counted.
    /// </summary>
    /// <remarks>
    /// The explicit scheme's step is bounded by the drift, and a field that grows tenfold
    /// during the run tightens that bound tenfold. Taking the step from the field at the
    /// start and never revisiting it would run unstable from the moment the drift outgrew
    /// it - which shows up as negative densities and a population that stops adding up.
    /// </remarks>
    [Fact]
    public void ARisingFieldKeepsTheStepStable()
    {
        var grid = Grid2D.OverBox(-0.05, -0.02, 0.05, 0.02, 128, 64);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);

        const double Seconds = 1e-4;
        var start = PointSource(grid, grid.CountX / 4, grid.CountY / 2);
        var population = start.Population();

        var rising = DriftDiffusion.Run(
            start, UniformField.Create(new Vec3(100.0, 0.0, 0.0)), gas, mobility, species,
            Seconds, Reflecting,
            fieldAt: t => UniformField.Create(new Vec3(100.0 + (900.0 * t / Seconds), 0.0, 0.0)));

        var lowest = double.MaxValue;
        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                lowest = Math.Min(lowest, rising.Density[i, j]);
            }
        }

        output.WriteLine($"{rising.Steps} steps; lowest density {lowest:E3}; "
            + $"population {rising.Density.Population() / population:F6} of initial");

        Assert.True(lowest >= 0.0, $"a density went negative: {lowest:E3}");
        Assert.Equal(1.0, rising.Density.Population() / population, 1e-6);
    }

    /// <summary>A stand-in for a field that varies in time with no drive: a DC ramp.</summary>
    private sealed class RampOnly : ITimeVaryingField
    {
        public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

        public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
            new(100.0 * (1.0 + timeSeconds), 0.0, 0.0);

        public double PotentialAt(in Vec3 position, double timeSeconds) =>
            -100.0 * (1.0 + timeSeconds) * position.X;

        // No oscillation anywhere in it, so no shortest period.
        public double ShortestPeriodSeconds => double.PositiveInfinity;
    }

    /// <summary>
    /// A pseudopotential averages over a cycle, and a DC ramp has none - so it is refused,
    /// not averaged over infinity.
    /// </summary>
    /// <remarks>
    /// The guard was <c>ThrowIfNegativeOrZero</c>, which infinity passes: the angular
    /// frequency became zero, and the cycle average sampled at infinity times the sample
    /// index over the count, which is NaN at index zero. A ramped diffusive phase would have
    /// reached it, silently. This is the defect class this project has met five times -
    /// a time-varying quantity through an interface that assumes a particular kind of
    /// variation - in a new form: two kinds of time dependence behind one interface.
    /// </remarks>
    [Fact]
    public void APseudopotentialRefusesAFieldWithNoDrive()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var rate = PonderomotiveField.CollisionRateFromMobility(species.ChargeSi, species.MassSi, 0.04);

        var refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PonderomotiveField(new RampOnly(), species.ChargeSi, species.MassSi, rate));

        output.WriteLine(refused.Message);
        Assert.Contains("no drive", refused.Message, StringComparison.Ordinal);
    }
}
