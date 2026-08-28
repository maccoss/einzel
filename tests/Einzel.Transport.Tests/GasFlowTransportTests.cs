using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A gas that moves carries the ion with it, in the event-driven mode.
/// </summary>
/// <remarks>
/// <para>
/// GAS-1 asks for an imported neutral velocity field. The diffusive mode has been
/// able to see one for some time; the event-driven models <em>refused</em> it, and
/// refusing was right at the time — a collision was drawn from a time and a velocity
/// with no place to evaluate the flow at, so the alternative would have been a run
/// that used the uniform drift and said nothing, flying an ion through a declared jet
/// as though the gas stood still.
/// </para>
/// <para>
/// The position is now carried into the draw. That is the whole change: a collision
/// samples a Maxwellian about the bulk velocity <em>where the ion is</em>, rather than
/// about one declared drift.
/// </para>
/// <para>
/// It is checkable against a closed form the engine has no part in. In a gas moving at
/// <c>u</c> with a field <c>E</c>, an ion's steady drift is <c>u + μE</c> — the flow
/// carries it and the field pushes it, and the two simply add, because the mobility is
/// defined in the frame the gas is at rest in. That means the flow's contribution can
/// be measured as a <em>difference</em> between two runs, which cancels every property
/// of the collision model.
/// </para>
/// </remarks>
public sealed class GasFlowTransportTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>Nitrogen at a pressure where an ion collides thousands of times.</summary>
    private const double Dalton = 1.66053906892e-27;

    private static BackgroundGas Nitrogen(Vec3 driftSi, IGasFlow? flow = null) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = 100.0,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
        DriftVelocitySi = driftSi,
        Flow = flow,
    };

    /// <summary>
    /// The mean velocity an ion settles to, over the last half of a long flight.
    /// </summary>
    private static Vec3 Drift(BackgroundGas gas, Vec3 fieldSi, int seed, double seconds = 6.0e-4)
    {
        var species = Peptide;
        var field = UniformField.Create(fieldSi);

        var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, seed);
        var recorder = new TrajectoryRecorder(seconds / 400.0);

        TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            species,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-8, MaximumFlightTime = seconds },
            (in PhaseState s) => 1.0,
            recorder,
            sampler);

        var samples = recorder.Samples;
        var half = samples.Count / 2;

        // From the displacement over the second half rather than by averaging the
        // sampled velocities: an ion between collisions is accelerating, and a mean
        // of instantaneous velocities weights whatever the recorder happened to
        // catch. A displacement over a known interval is the drift by definition.
        var span = samples[^1].TimeSeconds - samples[half].TimeSeconds;

        return (samples[^1].Position - samples[half].Position) * (1.0 / span);
    }

    [Fact]
    public void AUniformFlowIsCarriedByTheEventDrivenModel()
    {
        // The closed form: steady drift is u + mu E, so the DIFFERENCE between a
        // moving gas and a still one is exactly u, whatever the mobility is. That
        // cancels the collision model, the cross section and the temperature, and it
        // is why this is a check rather than a plausibility argument.
        var flowSi = new Vec3(120.0, 0.0, 0.0);
        var fieldSi = new Vec3(0.0, 4.0e3, 0.0);

        var still = Drift(Nitrogen(Vec3.Zero), fieldSi, seed: 20260828);
        var moving = Drift(Nitrogen(Vec3.Zero, new UniformGasFlow(flowSi)), fieldSi, seed: 20260828);

        var carried = moving - still;

        output.WriteLine($"still   {still.X,10:F3} {still.Y,10:F3} m/s");
        output.WriteLine($"moving  {moving.X,10:F3} {moving.Y,10:F3} m/s");
        output.WriteLine($"carried {carried.X,10:F3} {carried.Y,10:F3} m/s, declared flow {flowSi.X:F1}");

        // Along the flow, the difference is the flow. Ten per cent, on a single ion
        // whose drift is a random walk about its mean.
        Assert.Equal(flowSi.X, carried.X, 0.10 * flowSi.X);

        // Across it, nothing: a flow along x cannot change the drift along y, and a
        // difference there would mean the bulk term had leaked into the Maxwellian
        // draw or the field response.
        Assert.True(
            Math.Abs(carried.Y) < 0.10 * flowSi.X,
            $"a flow along x moved the y drift by {carried.Y:F3} m/s");
    }

    [Fact]
    public void AFlowFieldAndAUniformDriftAgreeWhereTheyDescribeTheSameGas()
    {
        // The control that makes the previous test mean something. A UniformGasFlow
        // and a declared driftVelocity are the same gas said two ways, so they must
        // give the same trajectory - not merely the same average - given the same
        // seed. Anything else means the flow path and the drift path disagree about
        // what the neutral velocity is.
        var flowSi = new Vec3(90.0, 40.0, 0.0);
        var fieldSi = new Vec3(0.0, 0.0, 2.0e3);

        var declared = Drift(Nitrogen(flowSi), fieldSi, seed: 7);
        var sampled = Drift(Nitrogen(Vec3.Zero, new UniformGasFlow(flowSi)), fieldSi, seed: 7);

        output.WriteLine($"driftVelocity  {declared.X,10:F4} {declared.Y,10:F4} {declared.Z,10:F4}");
        output.WriteLine($"flow field     {sampled.X,10:F4} {sampled.Y,10:F4} {sampled.Z,10:F4}");

        Assert.Equal(declared.X, sampled.X, 1e-9);
        Assert.Equal(declared.Y, sampled.Y, 1e-9);
        Assert.Equal(declared.Z, sampled.Z, 1e-9);
    }

    [Fact]
    public void AFlowThatVariesWithPositionIsSeenWhereTheIonIs()
    {
        // The thing a uniform drift structurally cannot express, and the reason the
        // refusal was there. The gas stands still for x below the midpoint and moves
        // beyond it, so an ion crossing the step must accelerate at the step and not
        // before - a sampler evaluating the flow at one convenient point would give
        // either the whole flow from the start or none of it at all.
        // Far enough along that the ion has reached its steady drift before it. A
        // first version put the step at 3 mm, which the ion crosses in six
        // microseconds - so the "before" average was taken over three samples of an
        // ion still accelerating from rest, and read 308 m/s against a steady drift
        // of about 500. The difference then came out at 361 against a declared 200
        // and looked like a physics discrepancy. It was a transient.
        var fast = new Vec3(200.0, 0.0, 0.0);
        var step = 0.25;

        var flow = new SteppedFlow(step, fast);
        var gas = Nitrogen(Vec3.Zero, flow);

        var species = Peptide;
        var field = UniformField.Create(new Vec3(2.0e3, 0.0, 0.0));

        var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, 11);
        var recorder = new TrajectoryRecorder(2.0e-6);

        TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            species,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-8, MaximumFlightTime = 1.2e-3 },
            (in PhaseState s) => 1.0,
            recorder,
            sampler);

        // Mean speed over the samples on each side of the step.
        double Mean(bool beyond)
        {
            var total = 0.0;
            var count = 0;

            foreach (var sample in recorder.Samples)
            {
                // Past the launch transient, which is a ramp from rest and not a
                // drift at all.
                if (sample.TimeSeconds > 5.0e-5 && sample.Position.X > step == beyond)
                {
                    total += sample.Velocity.X;
                    count++;
                }
            }

            return count == 0 ? double.NaN : total / count;
        }

        var before = Mean(false);
        var after = Mean(true);

        output.WriteLine($"before the step  {before,10:F2} m/s over the still gas");
        output.WriteLine($"after it         {after,10:F2} m/s over the moving gas");
        output.WriteLine($"difference       {after - before,10:F2}, declared flow {fast.X:F0}");

        Assert.True(after > before, "the ion should be faster in the moving half");

        // Most of the flow, not all: the ion needs a few collisions to equilibrate
        // with the new gas, and the samples just past the step are still catching up.
        Assert.InRange(after - before, 0.5 * fast.X, 1.3 * fast.X);
    }

    [Fact]
    public void SamplingOutsideAnImportedFieldIsReported()
    {
        // A sampled flow clamps to its edge value outside its box, which is a choice
        // rather than a measurement. An ion that spends its flight out there was
        // flown through a gas nobody computed, and the sampler says so.
        var flow = new SteppedFlow(0.0, new Vec3(50.0, 0.0, 0.0)) { Extent = 1.0e-3 };
        var gas = Nitrogen(Vec3.Zero, flow);

        var species = Peptide;
        var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, 3);

        Assert.False(sampler.SampledOutsideFlow);

        var velocity = new Vec3(100.0, 0.0, 0.0);
        var inside = new Vec3(0.5e-3, 0.0, 0.0);

        sampler.Start(0.0, velocity.Length);
        sampler.Collide(sampler.NextEventSeconds, in inside, ref velocity);

        Assert.False(sampler.SampledOutsideFlow);

        var outside = new Vec3(5.0e-3, 0.0, 0.0);

        sampler.Collide(sampler.NextEventSeconds, in outside, ref velocity);

        output.WriteLine($"outside the imported extent: {sampler.SampledOutsideFlow}");

        Assert.True(sampler.SampledOutsideFlow);
    }

    /// <summary>A flow that is still below a plane and moving above it.</summary>
    /// <remarks>
    /// Deliberately a step rather than a smooth ramp: what is being tested is that
    /// the flow is evaluated at the ion's own position, and a step makes "where" a
    /// visible property of the trajectory rather than a small correction to it.
    /// </remarks>
    private sealed class SteppedFlow(double atX, Vec3 beyondSi) : IGasFlow
    {
        /// <summary>How far the field is defined, or infinity for everywhere.</summary>
        public double Extent { get; init; } = double.PositiveInfinity;

        public bool IsMoving => beyondSi.LengthSquared > 0.0;

        public double FastestSpeedSi => beyondSi.Length;

        public Vec3 VelocityAt(in Vec3 point) => point.X > atX ? beyondSi : Vec3.Zero;

        public bool Covers(in Vec3 point) => Math.Abs(point.X) <= Extent;
    }
}
