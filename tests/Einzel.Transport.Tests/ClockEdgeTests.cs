using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A flight's clock at the edges a sum can miss by an ulp: the flight-time ceiling and a
/// sequencer switch closer than the smallest step.
/// </summary>
/// <remarks>
/// Both used to end a flight that had done nothing wrong as <c>StepSizeUnderflow</c>: an
/// analytic drift whose run over speed lands an ulp short of the ceiling leaves less than the
/// smallest step to take, and so does a step that lands past a switch on the ceiling and is set
/// back onto the switch. Neither is a step that cannot be taken - there is nothing left to take.
/// </remarks>
public sealed class ClockEdgeTests(ITestOutputHelper output)
{
    private static IonSpecies Ion => IonSpecies.FromMassToCharge(622.0, 1);

    private static UniformField Uniform(double voltsPerMetre) => UniformField.Create(new Vec3(voltsPerMetre, 0.0, 0.0));

    /// <summary>A field-free drift to the ceiling reaches it, though run over speed falls an ulp short.</summary>
    /// <remarks>
    /// 0.3 m over 3000 m/s is 9.999999999999999e-5 s, 1.4e-20 s short of a 1e-4 s ceiling. Where
    /// the field-free region ends 50 fm past that point, too close for another drift, the
    /// integrator is left with a step under the smallest it takes.
    /// </remarks>
    [Fact]
    public void ADriftToTheCeilingReachesIt()
    {
        Assert.True(1e-4 * 3000.0 / 3000.0 < 1e-4, "the drift's own time falls short of the ceiling");

        var flight = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, new Vec3(3000.0, 0.0, 0.0)),
            Ion,
            new FreeUpTo(0.3 + 5e-14),
            new IntegrationSettings { MaximumFlightTime = 1e-4 });

        output.WriteLine($"{flight.Outcome} at {flight.FlightTimeSeconds:R} s");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.True(1e-4 - flight.FlightTimeSeconds < 1e-18, $"ended at {flight.FlightTimeSeconds:R} s");
    }

    /// <summary>
    /// A step that ends on a switch and on the ceiling, which are equal by difference from where it
    /// started and an ulp apart, ends on the ceiling.
    /// </summary>
    /// <remarks>
    /// From <c>x</c>, <c>S - x == M - x</c> while <c>M</c> is the next double above <c>S</c>, and
    /// <c>x + (S - x)</c> rounds to <c>M</c>. Set back onto the switch and not then onto the
    /// ceiling, the clock was an ulp short of it with less than the smallest step to go.
    /// </remarks>
    [Fact]
    public void ASwitchAndTheCeilingEqualByDifferenceEndOnTheCeiling()
    {
        const double first = 3.473353768160355e-08;
        const double second = 2.0880096382522576e-07;
        var ceiling = Math.BitIncrement(second);

        Assert.Equal(second - first, ceiling - first);
        Assert.True(first + (second - first) == ceiling, "the sum lands on the ceiling, past the switch");

        var field = new SequencedField([Uniform(0.0), Uniform(0.0), Uniform(0.0)], [first, second, 2 * second]);

        var flight = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, new Vec3(3000.0, 0.0, 0.0)),
            Ion,
            field,
            new IntegrationSettings
            {
                MaximumFlightTime = ceiling,
                UseAnalyticDrift = false,
                InitialStep = 1.0,
                MaximumStepGrowth = 100.0,
            });

        output.WriteLine($"{flight.Outcome} at {flight.FlightTimeSeconds:R} s after {flight.AcceptedSteps} steps");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.Equal(ceiling, flight.FlightTimeSeconds);
    }

    /// <summary>A switch closer than the smallest step is reached, and the flight goes on in the next phase.</summary>
    [Fact]
    public void ASwitchCloserThanTheSmallestStepIsReached()
    {
        const double first = 1e-19;
        const double ceiling = 5e-7;
        var field = new SequencedField([Uniform(1e3), Uniform(2e6)], [first, 1e-6]);

        var flight = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero), Ion, field, new IntegrationSettings { MaximumFlightTime = ceiling });

        var expected = Ion.ChargeSi / Ion.MassSi * 2e6 * ceiling;

        output.WriteLine($"{flight.Outcome} at {flight.FlightTimeSeconds:R} s, {flight.FinalState.Velocity.X:R} m/s");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.Equal(expected, flight.FinalState.Velocity.X, 1e-9 * expected);
    }

    /// <summary>
    /// A rectangular drive whose edge lies on a switch is landed on without a cascade: the step
    /// ending there is asked the level it held, not the level after the edge.
    /// </summary>
    /// <remarks>
    /// One megahertz at a half duty is high for the first half microsecond and the switch is at
    /// 0.5 us, on the edge (1e6 times 5e-7 is 0.5 exactly). Asked the level after it, the last
    /// stages saw the field reversed, the error estimate refused the step, and the next and the
    /// next toward the switch.
    /// </remarks>
    [Fact]
    public void ARectangularEdgeOnASwitchIsLandedOnWithoutACascade()
    {
        const double edge = 5e-7;
        var drive = OscillatingUniformField.Create(
            new Vec3(1e3, 0.0, 0.0), Core.Units.Quantity.From(1.0, "MHz"), new RfWaveform.Rectangular(0.5));
        var field = new SequencedField([drive, Uniform(0.0)], [edge, 2 * edge]);

        var flight = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero), Ion, field, new IntegrationSettings { MaximumFlightTime = 1.6 * edge });

        var expected = Ion.ChargeSi / Ion.MassSi * 1e3 * edge;

        output.WriteLine(
            $"{flight.Outcome}: {flight.AcceptedSteps} accepted, {flight.RejectedSteps} refused, {flight.FinalState.Velocity.X:R} m/s against {expected:R}");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.True(flight.RejectedSteps <= 4, $"{flight.RejectedSteps} steps refused: a cascade at the edge");
        Assert.Equal(expected, flight.FinalState.Velocity.X, 1e-9 * expected);
    }

    /// <summary>
    /// A step cut to a switch and then cut again by a collision ends where nothing changed, so the
    /// discontinuity measures read at its end are the next step's start.
    /// </summary>
    /// <remarks>
    /// Counted as the measures read twice running at one point: that happens only where the
    /// integrator forgot what it had read, which is right after a switch it landed on and after a
    /// collision that scattered the ion. Forgetting on every step once cut to a switch, as the
    /// flag that preceded <c>endsOnSwitch</c> did, adds one for each null collision that cut such
    /// a step.
    /// </remarks>
    [Fact]
    public void AStepCutShortOfASwitchKeepsItsMeasures()
    {
        const double phase = 1e-6;
        const int phases = 200;
        var ends = Enumerable.Range(1, phases + 1).Select(k => k * phase).ToArray();
        var field = new Measured(new SequencedField(ends.Select(_ => Uniform(0.0)).ToArray(), ends));
        var sampler = new CollisionSampler(
            new BackgroundGas
            {
                Model = CollisionModel.HardSphere,
                PressureSi = 1.0,
                TemperatureK = 300.0,
                MassSi = 28.0134 * 1.66053906660e-27,
                CrossSectionSi = 250e-20,
                PolarizabilitySi = 1.74e-30,
            },
            Ion.MassSi,
            Ion.ChargeSi,
            seed: 20261124);

        var flight = TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, new Vec3(300.0, 0.0, 0.0)),
            Ion,
            field,
            new IntegrationSettings { MaximumFlightTime = phases * phase, UseAnalyticDrift = false },
            collisions: sampler);

        var landed = phases - 1;

        output.WriteLine(
            $"{flight.Outcome}: {field.Repeats} repeated reads, {landed} switches landed, {sampler.Collisions} real collisions");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.True(sampler.Collisions > 10, "the gas has to scatter the ion for the count to mean anything");
        Assert.Equal(landed + sampler.Collisions, field.Repeats);
    }

    // No field anywhere, but a field-free region declared to end at a plane across x.
    private sealed class FreeUpTo(double planeX) : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 position) => Vec3.Zero;

        public double PotentialAt(in Vec3 position) => 0.0;

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            direction.X > 0.0 ? Math.Max(0.0, (planeX - position.X) / direction.X) : double.PositiveInfinity;
    }

    // A sequenced field reporting two discontinuity members, so the integrator keeps their
    // measures between steps, and counting reads repeated at one point.
    private sealed class Measured(ITimeVaryingField inner) : ITimeVaryingField
    {
        private Vec3? _last;

        public int Repeats { get; private set; }

        public int DiscontinuityMembers => 2;

        public double ResolutionLength => inner.ResolutionLength;

        public double OscillatingResolutionLength => inner.OscillatingResolutionLength;

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public void MemberDistancesToDiscontinuity(in Vec3 position, Span<double> distances)
        {
            if (_last is { } last && last == position)
            {
                Repeats++;
            }

            _last = position;
            distances.Fill(1.0);
        }

        public double SignedDistanceToDiscontinuity(in Vec3 position) => 1.0;

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) => inner.ElectricFieldAt(position, timeSeconds);

        public double PotentialAt(in Vec3 position, double timeSeconds) => inner.PotentialAt(position, timeSeconds);

        public Vec3 ElectricFieldBefore(in Vec3 position, double switchSeconds) => inner.ElectricFieldBefore(position, switchSeconds);

        public double PotentialBefore(in Vec3 position, double switchSeconds) => inner.PotentialBefore(position, switchSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(position);

        public double NextSwitchAfter(double timeSeconds) => inner.NextSwitchAfter(timeSeconds);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => inner.FieldFreeRunLength(position, direction);
    }
}
