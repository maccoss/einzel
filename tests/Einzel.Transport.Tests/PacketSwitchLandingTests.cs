using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A packet meeting a switch of a sequenced field lands on it, as a single trajectory does: the
/// shared step is cut to the switch, its last stages ask the phase that is ending, and the clock
/// is set onto the switch rather than left where a sum put it.
/// </summary>
/// <remarks>
/// Without the cut, a step spanning the switch sampled two fields as one, the error estimate
/// refused it, then a shorter one, and so on down to the smallest step, which was accepted
/// across the jump - a cascade of refusals at every switch, and a step integrated across a field
/// change.
/// </remarks>
public sealed class PacketSwitchLandingTests(ITestOutputHelper output)
{
    private static IonSpecies Ion => IonSpecies.FromMassToCharge(622.0, 1);

    private static TrajectoryStopFunction Never => (in PhaseState _) => 1.0;

    private static UniformField Uniform(double voltsPerMetre) => UniformField.Create(new Vec3(voltsPerMetre, 0.0, 0.0));

    /// <summary>
    /// A packet crossing a switch into a field two thousand times stronger, and a packet whose
    /// flight ends exactly at the switch, take no cascade of refusals and end with the closed-form
    /// velocity.
    /// </summary>
    /// <param name="phase">The length of the first phase, in seconds.</param>
    /// <param name="ceilingInPhases">Where the flight ends, in phases.</param>
    [Theory]
    [InlineData(1e-7, 1.5)]
    [InlineData(3.7e-7, 1.5)]
    [InlineData(1e-6, 1.0)]
    [InlineData(2.9e-7, 1.0)]
    public void APacketLandsOnASwitchWithoutACascade(double phase, double ceilingInPhases)
    {
        var field = new SequencedField([Uniform(1e3), Uniform(2e6)], [phase, 2 * phase]);
        var ceiling = phase * ceilingInPhases;
        var launch = Enumerable.Range(0, 4)
            .Select(k => new PhaseState(new Vec3(0.0, 1e-3 * k, 0.0), new Vec3(10.0 * k, 0.0, 0.0)))
            .ToArray();

        var result = PacketIntegrator.Fly(
            launch, Ion, field, interaction: null, new IntegrationSettings { MaximumFlightTime = ceiling }, Never);

        var qm = Ion.ChargeSi / Ion.MassSi;
        var gained = qm * ((1e3 * phase) + (2e6 * (ceiling - phase)));

        output.WriteLine($"{result.Steps} steps, {result.RejectedSteps} refused");

        Assert.True(result.RejectedSteps <= 4, $"{result.RejectedSteps} steps refused: a cascade at the switch");

        for (var k = 0; k < launch.Length; k++)
        {
            var member = result.Members[k];
            Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, member.Outcome);
            Assert.Equal(ceiling, member.FlightTimeSeconds);

            var expected = launch[k].Velocity.X + gained;
            Assert.True(
                Math.Abs(member.FinalState.Velocity.X - expected) <= 1e-9 * Math.Abs(expected),
                $"member {k}: {member.FinalState.Velocity.X:R} against {expected:R} m/s");
        }
    }

    /// <summary>
    /// Over many random switch instants, a packet's clock is set onto the switch it stepped to:
    /// the field is never asked for its ordinary value an ulp short of a switch, which is the
    /// phase that has ended answering for the step that starts the next.
    /// </summary>
    /// <remarks>
    /// The step's end <c>x + (b - x)</c> misses <c>b</c> for about one draw in twenty. Left
    /// there, the derivatives the next step starts from are built at an instant the field
    /// still answers with the old phase.
    /// </remarks>
    [Fact]
    public void APacketsClockIsSetOntoTheSwitch()
    {
        var random = new Random(20261124);
        var missed = 0;
        var landedShort = 0;

        for (var draw = 0; draw < 400; draw++)
        {
            var first = Math.Exp(random.NextDouble() * Math.Log(1e3)) * 1e-8;
            var second = first * (1.0 + random.NextDouble());
            var field = new Recording(
                new SequencedField([Uniform(1e3), Uniform(2e6), Uniform(1e3)], [first, second, second * 2.0]),
                [first, second]);
            var launch = new[] { new PhaseState(Vec3.Zero, Vec3.Zero), new PhaseState(new Vec3(0.0, 1e-3, 0.0), new Vec3(5.0, 0.0, 0.0)) };

            var result = PacketIntegrator.Fly(
                launch, Ion, field, interaction: null, new IntegrationSettings { MaximumFlightTime = second * 1.5 }, Never);

            Assert.All(result.Members, m => Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, m.Outcome));
            missed += field.AskedJustShort;
            landedShort += field.AskedJustShort > 0 ? 1 : 0;
        }

        output.WriteLine($"{landedShort} of 400 draws asked the field an ulp short of a switch ({missed} evaluations)");

        Assert.Equal(0, missed);
    }

    /// <summary>
    /// A member that strikes metal on the step that lands on a switch records the switch, as a
    /// member still in flight is set onto it, not the ulp short of it the sum of the steps gives.
    /// </summary>
    /// <remarks>
    /// The wall appears only once the field has been asked from before a switch - which only the
    /// step cut to a switch does - so every member strikes on exactly that step.
    /// </remarks>
    [Fact]
    public void AMemberStruckOnTheSwitchStepRecordsTheSwitch()
    {
        var random = new Random(20261124);
        var missed = 0;

        for (var draw = 0; draw < 400; draw++)
        {
            var first = Math.Exp(random.NextDouble() * Math.Log(1e3)) * 1e-8;
            var second = first * (1.0 + random.NextDouble());
            var field = new WallAfterSwitch(
                new SequencedField([Uniform(1e3), Uniform(2e6), Uniform(1e3)], [first, second, second * 2.0]),
                first);
            var launch = new[] { new PhaseState(Vec3.Zero, Vec3.Zero), new PhaseState(new Vec3(0.0, 1e-3, 0.0), new Vec3(5.0, 0.0, 0.0)) };

            var result = PacketIntegrator.Fly(
                launch, Ion, field, interaction: null, new IntegrationSettings { MaximumFlightTime = second * 1.5 }, Never);

            foreach (var member in result.Members)
            {
                Assert.Equal(TrajectoryOutcome.StruckElectrode, member.Outcome);
                missed += member.FlightTimeSeconds == first ? 0 : 1;
            }
        }

        output.WriteLine($"{missed} members struck at another instant than the switch");

        Assert.Equal(0, missed);
    }

    // A sequenced field whose metal fills space once it has been asked from before a given switch.
    private sealed class WallAfterSwitch(ITimeVaryingField inner, double wallSwitch) : ITimeVaryingField, IConductorBounded
    {
        private bool _armed;

        public double ResolutionLength => inner.ResolutionLength;

        public double OscillatingResolutionLength => inner.OscillatingResolutionLength;

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public double SignedDistanceToConductor(in Vec3 position) => _armed ? -1.0 : 1.0;

        public string? ConductorAt(in Vec3 position) => _armed ? "wall" : null;

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) => inner.ElectricFieldAt(position, timeSeconds);

        public double PotentialAt(in Vec3 position, double timeSeconds) => inner.PotentialAt(position, timeSeconds);

        public Vec3 ElectricFieldBefore(in Vec3 position, double switchSeconds)
        {
            _armed |= switchSeconds == wallSwitch;
            return inner.ElectricFieldBefore(position, switchSeconds);
        }

        public double PotentialBefore(in Vec3 position, double switchSeconds) => inner.PotentialBefore(position, switchSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(position);

        public double NextSwitchAfter(double timeSeconds) => inner.NextSwitchAfter(timeSeconds);
    }

    // Counts the ordinary evaluations made within a femtosecond short of a declared switch.
    private sealed class Recording(ITimeVaryingField inner, double[] switches) : ITimeVaryingField
    {
        public int AskedJustShort { get; private set; }

        public double ResolutionLength => inner.ResolutionLength;

        public double OscillatingResolutionLength => inner.OscillatingResolutionLength;

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
        {
            foreach (var at in switches)
            {
                if (timeSeconds < at && at - timeSeconds < 1e-15)
                {
                    AskedJustShort++;
                }
            }

            return inner.ElectricFieldAt(position, timeSeconds);
        }

        public double PotentialAt(in Vec3 position, double timeSeconds) => inner.PotentialAt(position, timeSeconds);

        public Vec3 ElectricFieldBefore(in Vec3 position, double switchSeconds) => inner.ElectricFieldBefore(position, switchSeconds);

        public double PotentialBefore(in Vec3 position, double switchSeconds) => inner.PotentialBefore(position, switchSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(position);

        public double NextSwitchAfter(double timeSeconds) => inner.NextSwitchAfter(timeSeconds);

        public double SignedDistanceToDiscontinuity(in Vec3 position) => inner.SignedDistanceToDiscontinuity(position);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => inner.FieldFreeRunLength(position, direction);
    }
}
