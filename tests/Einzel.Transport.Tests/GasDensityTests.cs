using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A gas whose density varies from place to place.
/// </summary>
/// <remarks>
/// <para>
/// The last quantity about a gas this engine held as a single number for a whole
/// model. GAS-1's velocity field landed and the ions moved with the jet; the
/// <em>density</em> stayed uniform, so an imported flow gave the neutrals a
/// velocity everywhere and the same number of them everywhere. That is not a
/// differentially pumped instrument, which is what every device this platform is
/// aimed at above 10^-2 mbar actually is.
/// </para>
/// <para>
/// Three things have to be true and each is checked against arithmetic this engine
/// had no part in: mobility goes as the reciprocal of density, a collision rate
/// goes as the density where the ion is, and a model that declares no field is
/// untouched to the last bit.
/// </para>
/// </remarks>
public sealed class GasDensityTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>A density field of one value everywhere, over a metre cube.</summary>
    private static SampledGasDensity Flat(double numberDensitySi) =>
        new(new SampledGrid(
            1, 2, 2, 2, Vec3.Zero, new Vec3(1.0, 1.0, 1.0),
            [.. Enumerable.Repeat(numberDensitySi, 8)]));

    /// <summary>
    /// A density that steps from one value to another across x = 0, over +/- 1 m.
    /// </summary>
    /// <remarks>
    /// Two nodes on x, so the interpolation between them is a ramp rather than a
    /// step - a genuine step is not representable on a grid and pretending otherwise
    /// would make the closed forms below wrong rather than approximate. What is
    /// asserted is the value at each end, where the samples are the data.
    /// </remarks>
    private static SampledGasDensity Ramp(double lowSi, double highSi) =>
        new(new SampledGrid(
            1, 2, 1, 1, new Vec3(-1.0, 0.0, 0.0), new Vec3(2.0, 0.0, 0.0),
            [lowSi, highSi]));

    private static BackgroundGas Nitrogen(
        double pressurePa,
        IGasDensity? density = null,
        CollisionModel model = CollisionModel.Langevin) => new()
    {
        Model = model,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
        Density = density,
    };

    // ------------------------------------------------------------------ mobility ---

    /// <summary>Mobility scales as the reciprocal of density, which nothing here did.</summary>
    /// <remarks>
    /// An ion drifts further between collisions in a thinner gas, so it is <c>mu N</c>
    /// that is constant - which is why the literature tabulates <em>reduced</em>
    /// mobility rather than mobility. Reading one declared mobility at every point of
    /// a graded gas would put the drift at the wrong speed everywhere except where
    /// the pressure happened to match.
    /// </remarks>
    [Fact]
    public void MobilityGoesAsTheReciprocalOfDensity()
    {
        var mobility = new Mobility(2.0e-4);
        var reference = 2.4e22;

        var thin = mobility.At(0.0, 0.5 * reference, reference);
        var declared = mobility.At(0.0, reference, reference);
        var thick = mobility.At(0.0, 2.0 * reference, reference);

        output.WriteLine($"half density {thin:E6} m^2/Vs");
        output.WriteLine($"declared     {declared:E6}");
        output.WriteLine($"twice        {thick:E6}");

        Assert.Equal(2.0 * declared, thin, 1e-15 * declared);
        Assert.Equal(0.5 * declared, thick, 1e-15 * declared);
    }

    /// <summary>At the declared density the scaled form is the unscaled one, bit for bit.</summary>
    /// <remarks>
    /// The control that says a model with no pressure field is untouched. The ratio
    /// is exactly 1.0 and multiplying by it changes nothing, so this is an equality
    /// rather than a tolerance - and if it were ever not, every diffusive number
    /// this engine has published would have moved.
    /// </remarks>
    [Fact]
    public void MobilityAtTheDeclaredDensityIsUnchangedToTheLastBit()
    {
        var mobility = new Mobility(2.0e-4, Alpha: 0.11, ValidToTownsend: 60.0);
        var reference = 2.4e22;

        foreach (var field in new[] { 0.0, 1.0e2, 4.0e3, 2.5e5 })
        {
            Assert.Equal(mobility.At(field, reference), mobility.At(field, reference, reference));
        }
    }

    // ------------------------------------------------------------------- the field ---

    /// <summary>A constant pressure field is the gas a single declared pressure means.</summary>
    [Fact]
    public void AConstantPressureFieldIsTheDeclaredGas()
    {
        const double pascals = 100.0;
        const double kelvin = 300.0;

        var expected = pascals / (BackgroundGas.BoltzmannSi * kelvin);

        var density = SampledGasDensity.FromPressure(
            new SampledGrid(
                1, 3, 3, 1, Vec3.Zero, new Vec3(0.01, 0.01, 0.0),
                [.. Enumerable.Repeat(pascals, 9)]),
            kelvin);

        output.WriteLine($"declared {expected:E9} /m^3");
        output.WriteLine($"imported {density.NumberDensityAt(new Vec3(0.013, 0.007, 0.0)):E9}");

        Assert.True(density.IsUniform);
        Assert.Equal(expected, density.HighestNumberDensitySi, 1e-15 * expected);

        // Two ulps rather than bit-identical, and the reason is worth knowing:
        // interpolating a constant returns that constant only to rounding, because
        // 30(1-f) + 30f is 29.999999999999996 for plenty of f. Inherent to sampling.
        Assert.Equal(expected, density.NumberDensityAt(new Vec3(0.013, 0.007, 0.0)), 1e-15 * expected);
    }

    /// <summary>A pressure of zero is refused, and the refusal says which mode to use.</summary>
    /// <remarks>
    /// Refused rather than clamped, because mobility goes as 1/n: a zero is an
    /// infinite drift and a stability limit of zero, so a run does not answer wrongly,
    /// it never finishes. AGT-3 wants the recovery instruction, and here it is that a
    /// collisionless region is described by trajectory integration rather than by
    /// diffusion.
    /// </remarks>
    [Fact]
    public void ANonPositiveSampleIsRefused()
    {
        var error = Assert.Throws<EinzelException>(() => new SampledGasDensity(
            new SampledGrid(1, 2, 1, 1, Vec3.Zero, new Vec3(1.0, 0.0, 0.0), [2.4e22, 0.0])));

        output.WriteLine(error.Error.Constraint);
        output.WriteLine(error.Error.Suggestion);

        Assert.Contains("positive", error.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("trajectory", error.Error.Suggestion, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- event-driven ---

    /// <summary>
    /// An imported field at twice the declared pressure is the same gas as declaring
    /// twice the pressure - to the last bit of the trajectory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sharpest check available that the field is honoured everywhere on the
    /// event-driven path rather than in most places. Two entirely separate routes to
    /// the same gas: one through <c>pressure</c>, one through a constant imported
    /// field over a model whose declared pressure is half of it. Every scheduled
    /// rate, every null-collision bound and every rejection has to read the field
    /// rather than the declared scalar for these to agree, and any one that did not
    /// would consume a different random draw and diverge visibly.
    /// </para>
    /// <para>
    /// Bit-identical rather than close: the seeds are the same and the arithmetic is
    /// the same arithmetic. A tolerance here would hide exactly the defect being
    /// looked for.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(CollisionModel.HardSphere)]
    [InlineData(CollisionModel.Langevin)]
    public void AnImportedFieldIsTheSameGasAsDeclaringItsPressure(CollisionModel model)
    {
        var doubled = Nitrogen(200.0, model: model);
        var declaredHalf = Nitrogen(100.0, Flat(doubled.NumberDensitySi), model);

        var byPressure = Fly(doubled, seed: 20260829);
        var byField = Fly(declaredHalf, seed: 20260829);

        output.WriteLine($"{model}");
        output.WriteLine($"  declared 200 Pa  {byPressure.Collisions} collisions, "
            + $"ends at {byPressure.End.X:E17}");
        output.WriteLine($"  100 Pa + field   {byField.Collisions} collisions, "
            + $"ends at {byField.End.X:E17}");

        Assert.Equal(byPressure.Collisions, byField.Collisions);
        Assert.Equal(byPressure.End.X, byField.End.X);
        Assert.Equal(byPressure.End.Y, byField.End.Y);
        Assert.Equal(byPressure.End.Z, byField.End.Z);
    }

    /// <summary>A graded gas collides where the gas is, and reversing it says so.</summary>
    /// <remarks>
    /// <para>
    /// The control for the equivalence test above, which by construction cannot tell
    /// a field that is read at a point from one that is uniform. Here an ion crosses a
    /// fourfold ramp, and the same ramp is then <em>reversed</em> so that the ion runs
    /// from the dense end to the thin one instead.
    /// </para>
    /// <para>
    /// <b>The reversal is the sharp half.</b> Both ramps hold the same densities over
    /// the same box and differ only in where each one is, so any reading that is blind
    /// to position - a local density taken from the declared scalar, a bound taken
    /// without a lookup - gives the two ramps an identical count. A bracket between
    /// the two uniform gases does not have that property: with the density read at
    /// the wrong place the count lands close to the thin gas and a bare "more than
    /// the thin one" still passes.
    /// </para>
    /// <para>
    /// Hard spheres rather than Langevin, for the same reason. The Langevin branch
    /// short-circuits its thinning where the density is uniform, so a flat imported
    /// field never reads a position at all - correct, and no test of the read.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGradedGasCollidesWhereTheGasIs()
    {
        const CollisionModel spheres = CollisionModel.HardSphere;

        var thinSi = Nitrogen(100.0).NumberDensitySi;
        var denseSi = Nitrogen(400.0).NumberDensitySi;

        var atThin = Fly(Nitrogen(100.0, model: spheres), seed: 7).Collisions;
        var atDense = Fly(Nitrogen(400.0, model: spheres), seed: 7).Collisions;

        var intoTheDense = Fly(
            Nitrogen(100.0, Ramp(thinSi, denseSi), spheres), seed: 7).Collisions;
        var outOfTheDense = Fly(
            Nitrogen(100.0, Ramp(denseSi, thinSi), spheres), seed: 7).Collisions;

        output.WriteLine($"uniform thin      {atThin}");
        output.WriteLine($"ramp, thin first  {intoTheDense}");
        output.WriteLine($"ramp, dense first {outOfTheDense}");
        output.WriteLine($"uniform dense     {atDense}");

        // Same densities, same box, opposite arrangement. Equal counts mean the
        // density was not read where the ion was.
        Assert.True(
            Math.Abs(intoTheDense - outOfTheDense) > 0.2 * intoTheDense,
            $"a ramp and its reverse collided {intoTheDense} and {outOfTheDense} times - "
            + "the density is not being read at the ion");

        // And the ion starts in the thin half and runs toward the dense one, so it
        // spends its early, fastest travel in the thin gas: running the ramp the other
        // way must collide more.
        Assert.True(
            outOfTheDense > intoTheDense,
            $"launched into the dense end the ion collided {outOfTheDense} times against "
            + $"{intoTheDense} launched into the thin end");

        Assert.InRange(intoTheDense, atThin, atDense);
        Assert.InRange(outOfTheDense, atThin, atDense);
    }

    /// <summary>
    /// The Langevin thinning is exact: the accepted fraction is the density ratio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Langevin rate does not contain the speed, so in a uniform gas every
    /// scheduled event is a real one and there is no rejection step at all. A graded
    /// gas makes the rate position-dependent, and the null-collision method turns
    /// that into a constant scheduled rate plus a thinning - so the fraction accepted
    /// at a point must be exactly the local density over the highest anywhere.
    /// </para>
    /// <para>
    /// Held still at one place, so the only thing varying is the density there. That
    /// makes it a check on the thinning rather than on the dynamics.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(-1.0, 0.25)]
    [InlineData(0.0, 0.625)]
    [InlineData(1.0, 1.0)]
    public void TheLangevinThinningAcceptsTheLocalDensityFraction(double x, double expected)
    {
        var thin = Nitrogen(100.0);
        var dense = Nitrogen(400.0);
        var gas = Nitrogen(100.0, Ramp(thin.NumberDensitySi, dense.NumberDensitySi));

        var species = Peptide;
        var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, seed: 11);
        var at = new Vec3(x, 0.0, 0.0);
        var velocity = new Vec3(300.0, 0.0, 0.0);

        const int attempts = 40_000;

        for (var i = 0; i < attempts; i++)
        {
            // The ion is put back each time: what is being measured is the acceptance
            // at one density, not a flight through several.
            velocity = new Vec3(300.0, 0.0, 0.0);
            sampler.Collide(0.0, in at, ref velocity);
        }

        var accepted = sampler.Collisions / (double)attempts;

        output.WriteLine($"x = {x,5:F1}  accepted {accepted:F4}, expected {expected:F4}");

        // Binomial, so the standard error at 40,000 draws is under 0.0025. Four
        // standard errors.
        Assert.Equal(expected, accepted, 0.01);
    }

    // ------------------------------------------------------------------ diffusive ---

    /// <summary>
    /// Halving the gas doubles the drift speed, in the diffusive mode, through the
    /// mobility alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic this engine has no part in: the steady drift is <c>mu E</c> and
    /// <c>mu</c> goes as <c>1/n</c>, so a gas at half the density drifts at twice the
    /// speed in the same field and covers the same distance in half the time. Taken as
    /// a <em>ratio</em> between two runs so the mobility, the field and the length all
    /// cancel.
    /// </para>
    /// <para>
    /// Run through <see cref="Mobility"/> directly rather than through a whole
    /// drift-diffusion solve, because a solve would fold the scaling together with the
    /// stability limit, the packet spread and the boundaries. What varies here is one
    /// thing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDiffusiveMobilityHalvesWhenTheGasDoubles()
    {
        var reference = Nitrogen(100.0);
        var mobility = Mobility.FromCrossSection(reference, Peptide);

        var declared = mobility.At(0.0, reference.NumberDensitySi, reference.NumberDensitySi);
        var thinner = mobility.At(
            0.0, 0.5 * reference.NumberDensitySi, reference.NumberDensitySi);

        output.WriteLine($"mobility at 100 Pa {declared:E6} m^2/Vs");
        output.WriteLine($"at 50 Pa           {thinner:E6}");
        output.WriteLine($"ratio              {thinner / declared:F9}");

        Assert.Equal(2.0, thinner / declared, 1e-12);

        // And the diffusion coefficient follows it, because the Einstein relation is
        // linear in the mobility - so a graded gas grades D as well, which is what the
        // face averaging in the flux needs.
        var diffusionDeclared = Mobility.DiffusionSi(300.0, Peptide.ChargeSi, declared);
        var diffusionThinner = Mobility.DiffusionSi(300.0, Peptide.ChargeSi, thinner);

        Assert.Equal(2.0, diffusionThinner / diffusionDeclared, 1e-12);
    }

    /// <summary>Flies an ion through a gas with no applied field and reports where it got to.</summary>
    private static (int Collisions, Vec3 End) Fly(BackgroundGas gas, int seed)
    {
        var species = Peptide;
        var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, seed);
        var recorder = new TrajectoryRecorder(1.0e-6);

        TrajectoryIntegrator.Integrate(
            new PhaseState(new Vec3(-0.5, 0.0, 0.0), new Vec3(400.0, 0.0, 0.0)),
            species,
            UniformField.Create(new Vec3(4.0e3, 0.0, 0.0)),
            new IntegrationSettings { RelativeTolerance = 1e-8, MaximumFlightTime = 2.0e-4 },
            (in PhaseState s) => 1.0,
            recorder,
            sampler);

        return (sampler.Collisions, recorder.Samples[^1].Position);
    }
}
