using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The two transport modes, on the same physics, in the band where both apply.
/// </summary>
/// <remarks>
/// <para>
/// REG-3: in the overlap band both modes run on the same model and the comparison
/// is a supported operation with its own report. Spec figure 4 marks that band the
/// dangerous one - both descriptions run there, neither is obviously right, and the
/// engine must run both and report the disagreement rather than silently choosing.
/// </para>
/// <para>
/// For the comparison to mean anything the two modes have to describe the
/// <em>same gas</em>, which is why the event-driven side uses hard-sphere
/// scattering off a declared cross section and the diffusive side takes its
/// mobility from that same cross section through Mason-Schamp. Comparing Langevin
/// capture against a mobility fitted to something else would be comparing two
/// different instruments and calling the difference a numerical disagreement.
/// </para>
/// </remarks>
public sealed class CrossModeTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;

    private static BackgroundGas Nitrogen(double pressurePa) => new()
    {
        Model = CollisionModel.HardSphere,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        CrossSectionSi = 250e-20,
        PolarizabilitySi = 1.74e-30,
    };

    /// <summary>Mean drift and its standard error, by flying ions and colliding them.</summary>
    private static (double Drift, double StandardError, int Ions) ByTrajectory(
        BackgroundGas gas, IonSpecies species, double strength, double seconds, int ions)
    {
        var field = UniformField.Create(new Vec3(strength, 0.0, 0.0));
        var displacements = new double[ions];

        for (var i = 0; i < ions; i++)
        {
            var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, 5150 + i);

            var result = TrajectoryIntegrator.Integrate(
                new PhaseState(Vec3.Zero, Vec3.Zero),
                species,
                field,
                new IntegrationSettings { MaximumFlightTime = seconds, RelativeTolerance = 1e-6 },
                collisions: sampler);

            displacements[i] = result.FinalState.Position.X;
        }

        var mean = displacements.Average();
        var variance = displacements.Sum(d => (d - mean) * (d - mean)) / (ions - 1.0);

        return (mean / seconds, Math.Sqrt(variance / ions) / seconds, ions);
    }

    /// <summary>Mean drift, by evolving a density and watching its centroid.</summary>
    private static (double Drift, int Steps) ByDiffusion(
        BackgroundGas gas,
        IonSpecies species,
        Mobility mobility,
        double strength,
        double seconds,
        double halfWidth)
    {
        var field = UniformField.Create(new Vec3(strength, 0.0, 0.0));

        var grid = Grid2D.OverBox(-halfWidth, -0.25 * halfWidth, halfWidth, 0.25 * halfWidth, 256, 64);
        var start = new DensityField(grid);

        start[grid.CountX / 2, grid.CountY / 2] = 1.0 / (grid.SpacingX * grid.SpacingY);

        var (fromX, _) = start.Centroid();

        var evolved = DriftDiffusion.Run(
            start, field, gas, mobility, species, seconds,
            new DriftDiffusion.DomainEdges(
                Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting));

        var (toX, _) = evolved.Density.Centroid();

        return ((toX - fromX) / seconds, evolved.Steps);
    }

    [Fact]
    public void BothModesAgreeOnTheDriftVelocityInTheOverlapBand()
    {
        // 1e-2 mbar: the top of where trajectory integration is valid and inside
        // where the diffusive description starts to be. Neither is obviously right,
        // which is exactly why the comparison exists.
        //
        // The field has to be chosen against E/N rather than picked. At this
        // pressure the gas is thin, the mobility is 9.2 square metres per volt
        // second, and 40 V/m is 166 townsend - deep into where the ion is heated by
        // the field and a low-field mobility does not describe it. Six townsend
        // keeps both descriptions inside their own validity, at the cost of a drift
        // smaller than the diffusive spread, which is why this needs two hundred
        // ions to resolve.
        var gas = Nitrogen(1.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);

        var strength = 1.5;
        var seconds = 1e-3;

        var townsend = strength / (gas.NumberDensitySi * Mobility.Townsend);

        var (byTrajectory, standardError, ions) = ByTrajectory(gas, species, strength, seconds, 200);
        var (byDiffusion, steps) = ByDiffusion(gas, species, mobility, strength, seconds, 0.08);

        var difference = Math.Abs(byTrajectory - byDiffusion);

        output.WriteLine($"gas               {gas.PressureSi / 1e2:G3} mbar of nitrogen, 250 A^2");
        output.WriteLine($"field             {strength:F2} V/m = {townsend:F1} townsend");
        output.WriteLine($"within the fit    {mobility.IsWithinFit(strength, gas.NumberDensitySi)}");
        output.WriteLine(string.Empty);
        output.WriteLine($"trajectory        {byTrajectory:F4} +/- {standardError:F4} m/s ({ions} ions)");
        output.WriteLine($"diffusion         {byDiffusion:F4} m/s ({steps} steps)");
        output.WriteLine($"mu E              {mobility.ZeroFieldSi * strength:F4} m/s");
        output.WriteLine(string.Empty);
        output.WriteLine($"disagreement      {difference:F4} m/s, {difference / standardError:F2} standard errors");

        // The two descriptions share only a cross section. One samples exponential
        // waiting times and rotates velocity vectors; the other pushes a density
        // through Bernoulli-weighted faces. Agreeing at all is the statement;
        // agreeing to within the ensemble's own error is the measurement.
        Assert.True(
            difference < 3.0 * standardError,
            $"the modes disagree by {difference:F3} m/s, which is "
            + $"{difference / standardError:F1} standard errors");
    }

    [Fact]
    public void AtHighReducedFieldTheLowFieldMobilityOverstatesTheDrift()
    {
        // The other half of REG-3, and the more useful half: where the two
        // descriptions disagree, and why. At 166 townsend the ion is heated by the
        // field, its collision rate rises, and it drifts more slowly than a mobility
        // fitted at thermal energies says it will.
        //
        // The event-driven mode gets this right without being told - it is colliding
        // an ion that is genuinely moving faster - while the diffusive mode is only
        // as good as the mobility it was handed. That is what TRN-1 means by making
        // mobility an explicit input with stated field dependence: the number has a
        // range, and outside it the answer is the input's rather than the solver's.
        var gas = Nitrogen(1.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var mobility = Mobility.FromCrossSection(gas, species);

        var strength = 40.0;
        var townsend = strength / (gas.NumberDensitySi * Mobility.Townsend);

        var (byTrajectory, standardError, ions) = ByTrajectory(gas, species, strength, 2e-4, 120);
        var lowField = mobility.ZeroFieldSi * strength;

        output.WriteLine($"field             {strength:F0} V/m = {townsend:F0} townsend");
        output.WriteLine($"within the fit    {mobility.IsWithinFit(strength, gas.NumberDensitySi)}");
        output.WriteLine(string.Empty);
        output.WriteLine($"trajectory        {byTrajectory:F1} +/- {standardError:F1} m/s ({ions} ions)");
        output.WriteLine($"low-field mu E    {lowField:F1} m/s");
        output.WriteLine($"overstated by     {lowField / byTrajectory:F3}x");

        // Outside the fitted range, and the mobility says so rather than the caller
        // having to know.
        Assert.False(mobility.IsWithinFit(strength, gas.NumberDensitySi));

        // And the direction is not arbitrary: field heating always slows an ion
        // relative to its thermal mobility, so the low-field value is an upper bound
        // rather than merely a different number.
        Assert.True(
            lowField > byTrajectory + (3.0 * standardError),
            "the low-field mobility did not overstate the drift, so field heating is not "
            + "being modelled");
    }

    [Fact]
    public void TheDiffusiveModeIsAvailableAndSaysWhatItProduces()
    {
        // REG-1 makes the two peers, and TRN-2 and RND-8 need something to ask
        // whether a mode produces trajectories at all - a renderer must not draw
        // lines through a region that never had any.
        var trajectory = TransportModes.Resolve("trajectory");
        var diffusion = TransportModes.Resolve("diffusion");

        output.WriteLine(
            $"{trajectory.Name,-11} valid to {trajectory.UpperPressureMbar:G3} mbar, "
            + $"trajectories: {trajectory.ProducesTrajectories}");

        output.WriteLine(
            $"{diffusion.Name,-11} valid from {diffusion.LowerPressureMbar:G3} mbar, "
            + $"trajectories: {diffusion.ProducesTrajectories}");

        Assert.True(diffusion.IsAvailable);
        Assert.False(diffusion.ProducesTrajectories);
        Assert.True(trajectory.ProducesTrajectories);

        // And they overlap, which is what makes REG-3 a band rather than a line.
        Assert.True(
            diffusion.LowerPressureMbar < trajectory.UpperPressureMbar,
            "the modes do not overlap, so there is nowhere to compare them");
    }
}
