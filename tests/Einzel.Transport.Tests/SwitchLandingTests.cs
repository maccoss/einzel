using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>A flight that meets a switch of a sequenced field lands on it, exactly, and goes on.</summary>
/// <remarks>
/// The step cut to land on a switch belongs to the phase that is ending, and its last stages
/// used to sample the one starting. With a large jump between them the error estimate refused
/// the step, then a shorter one, and so on toward the switch until a step underflowed about an
/// attosecond short of it. What it is asked must not depend on a sum hitting the switch: the
/// step's end <c>x + (b - x)</c> misses <c>b</c> for about one draw in twenty.
/// </remarks>
public sealed class SwitchLandingTests(ITestOutputHelper output)
{
    private static IonSpecies Ion => IonSpecies.FromMassToCharge(622.0, 1);

    private static UniformField Uniform(double voltsPerMetre) => UniformField.Create(new Vec3(voltsPerMetre, 0.0, 0.0));

    private static TrajectoryResult Fly(ITimeVaryingField field, double seconds) =>
        TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            Ion,
            field,
            new IntegrationSettings { MaximumFlightTime = seconds });

    /// <summary>A flight bounded at a switch, into a field two thousand times stronger, reaches it.</summary>
    /// <param name="phase">The length of each phase, in seconds.</param>
    [Theory]
    [InlineData(1e-7)]
    [InlineData(1e-6)]
    public void AFlightBoundedAtASwitchReachesIt(double phase)
    {
        var field = new SequencedField([Uniform(1e3), Uniform(2e6)], [phase, 2 * phase]);

        var flight = Fly(field, phase);

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.Equal(phase, flight.FlightTimeSeconds);
    }

    /// <summary>
    /// Over many random switch instants, a flight crosses a switch into a field two thousand
    /// times stronger and on: no underflow, no cascade of refusals at the switch, and a velocity
    /// at the end that is the closed form's.
    /// </summary>
    [Fact]
    public void RandomSwitchesAreLandedOnWithoutACascade()
    {
        var random = new Random(20261121);
        var worstRejected = 0;
        var worstExtra = 0;
        var worstVelocity = 0.0;

        for (var draw = 0; draw < 400; draw++)
        {
            var first = Math.Exp(random.NextDouble() * Math.Log(1e3)) * 1e-8;
            var second = first * (1.0 + random.NextDouble());
            var field = new SequencedField([Uniform(1e3), Uniform(2e6), Uniform(1e3)], [first, second, second * 2.0]);

            var flight = Fly(field, second * 1.5);

            Assert.True(
                flight.Outcome == TrajectoryOutcome.MaximumFlightTimeReached,
                $"draw {draw}: switches at {first:R} and {second:R} s: {flight.Outcome} at {flight.FlightTimeSeconds:R} s");

            // Against the same flight stitched from its phases flown apart: a cascade at a switch
            // adds dozens of steps the stitched legs never take.
            var stitched = Stitched([(Steady(Uniform(1e3)), first), (Steady(Uniform(2e6)), second - first), (Steady(Uniform(1e3)), second * 0.5)]);
            worstExtra = Math.Max(worstExtra, flight.AcceptedSteps + flight.RejectedSteps - stitched);

            var qm = Ion.ChargeSi / Ion.MassSi;
            var expected = qm * ((1e3 * first) + (2e6 * (second - first)) + (1e3 * ((second * 1.5) - second)));

            worstRejected = Math.Max(worstRejected, flight.RejectedSteps);
            worstVelocity = Math.Max(worstVelocity, Math.Abs((flight.FinalState.Velocity.X / expected) - 1.0));
        }

        output.WriteLine($"worst refusals in a flight {worstRejected}; worst steps beyond the stitched legs {worstExtra}; worst velocity {worstVelocity:G3} relative");

        Assert.True(worstExtra <= 4, $"a flight took {worstExtra} steps beyond its stitched legs, a cascade at a switch");
        Assert.True(worstVelocity < 1e-9, $"a velocity was {worstVelocity:G3} off the closed form");
    }

    /// <summary>
    /// Over many random ceilings, a flight cut to its ceiling ends there rather than an ulp short
    /// of it, offered a step of an ulp and ending as an underflow.
    /// </summary>
    [Fact]
    public void RandomCeilingsAreReached()
    {
        var random = new Random(611);
        var reached = 0;

        for (var draw = 0; draw < 2000; draw++)
        {
            var ceiling = Math.Exp(random.NextDouble() * Math.Log(1e4)) * 1e-9;
            var field = new SequencedField([Uniform(1e3), Uniform(2e6)], [ceiling * 0.37, ceiling * 3.0]);

            var flight = Fly(field, ceiling);

            Assert.True(
                flight.Outcome == TrajectoryOutcome.MaximumFlightTimeReached,
                $"draw {draw}: ceiling {ceiling:R} s: {flight.Outcome} at {flight.FlightTimeSeconds:R} s");
            reached++;
        }

        output.WriteLine($"{reached} ceilings reached");
    }

    /// <summary>
    /// A staged solved field, over random stage lengths, lands on its switches the same way.
    /// </summary>
    [Fact]
    public void RandomStagesOfASolvedFieldAreLandedOn()
    {
        var random = new Random(1121);

        for (var draw = 0; draw < 12; draw++)
        {
            var first = Math.Exp(random.NextDouble() * Math.Log(1e2)) * 1e-8;
            var second = first * (0.5 + random.NextDouble());
            var field = (ITimeVaryingField)GeometryBuilder.Build(Pusher(
                [new CompiledStage("hold", first, [Plate(1.0)]), new CompiledStage("push", second, [Plate(4000.0)])])).Field;

            var flight = Fly(field, first + (second * 0.5));
            var stitched = Stitched([
                ((ITimeVaryingField)Steady(GeometryBuilder.Build(Pusher([])).Field), first),
                (Steady(GeometryBuilder.Build(Pusher([]) with { Electrodes = [Plate(4000.0)] }).Field), second * 0.5),
            ]);
            var extra = flight.AcceptedSteps + flight.RejectedSteps - stitched;

            output.WriteLine($"{first:G4} + {second:G4} s: {flight.Outcome}, {extra} steps beyond the stitched legs");

            Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
            Assert.True(extra <= 4, $"{extra} steps beyond the stitched legs, a cascade at a switch");
        }
    }

    /// <summary>
    /// A leg whose clock is shifted onto the instrument's goes on past an interior switch whose
    /// reported instant, mapped back, would land an ulp past the instrument's - and its velocity
    /// afterwards is the closed form's, so the derivative handed on was the starting phase's.
    /// </summary>
    [Fact]
    public void AShiftedLegGoesOnPastAnInteriorSwitch()
    {
        const double start = 1.4014174343982395e-06;
        const double end = 3.0015222446891656e-05;

        Assert.True(end - start + start > end, "these instants no longer round past the switch");

        var instrument = new SequencedField([Uniform(1e3), Uniform(1e3), Uniform(2e5)], [start, end, end * 2.0]);
        var leg = new TimeShiftedField(instrument, start);
        var flown = (end - start) * 1.5;

        Assert.Equal(1e3, leg.ElectricFieldBefore(Vec3.Zero, leg.NextSwitchAfter(0.0)).X);
        Assert.Equal(2e5, leg.ElectricFieldAt(Vec3.Zero, leg.NextSwitchAfter(0.0)).X);

        var flight = Fly(leg, flown);

        var qm = Ion.ChargeSi / Ion.MassSi;
        var expected = qm * ((1e3 * (end - start)) + (2e5 * (flown - (end - start))));

        output.WriteLine($"{flight.Outcome} after {flight.AcceptedSteps} steps, {flight.RejectedSteps} refused");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.Equal(1.0, flight.FinalState.Velocity.X / expected, 9);
    }

    /// <summary>
    /// Two boundaries a few ulps apart are told apart by identity on a shifted leg: the first is
    /// approached from its own phase, starts the second, and the one after is the second.
    /// </summary>
    [Fact]
    public void TwoBoundariesAFewUlpsApartAreToldApart()
    {
        const double first = 3.0015222446891656e-05;
        var second = Math.BitIncrement(Math.BitIncrement(Math.BitIncrement(first)));
        var instrument = new SequencedField([Uniform(1.0), Uniform(2.0), Uniform(3.0)], [first, second, 1e-3]);
        var leg = new TimeShiftedField(instrument, 1.4014174343982395e-06);

        var one = leg.NextSwitchAfter(0.0);
        var two = leg.NextSwitchAfter(one);

        output.WriteLine($"leg switches at {one:R} and {two:R}");

        Assert.True(two > one, "the two boundaries were reported as one");
        Assert.Equal(1.0, leg.ElectricFieldBefore(Vec3.Zero, one).X);
        Assert.Equal(2.0, leg.ElectricFieldAt(Vec3.Zero, one).X);
        Assert.Equal(2.0, leg.ElectricFieldBefore(Vec3.Zero, two).X);
        Assert.Equal(3.0, leg.ElectricFieldAt(Vec3.Zero, two).X);
    }

    /// <summary>On a boundary, a sequenced field asked from before answers with the phase ending.</summary>
    [Fact]
    public void FromBeforeABoundaryIsThePhaseEnding()
    {
        var field = new SequencedField([Uniform(1e3), Uniform(2e6)], [1e-7, 2e-7]);

        Assert.Equal(2e6, field.ElectricFieldAt(Vec3.Zero, 1e-7).X);
        Assert.Equal(1e3, field.ElectricFieldBefore(Vec3.Zero, 1e-7).X);
    }

    /// <summary>
    /// Every evaluation of the field is counted, the one more a step that lands on a switch makes
    /// for the derivative it hands on included.
    /// </summary>
    [Fact]
    public void EveryEvaluationIsCounted()
    {
        var counted = new Counting(new SequencedField([Uniform(1e3), Uniform(2e6)], [1e-7, 2e-7]));

        var flight = Fly(counted, 1.5e-7);

        output.WriteLine($"{counted.Evaluations} evaluations made, {flight.FieldEvaluations} counted");

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
        Assert.Equal(counted.Evaluations, flight.FieldEvaluations);
    }

    /// <summary>The steps the same flight takes stitched from its phases, each flown apart from the last's end.</summary>
    /// <param name="legs">Each phase's field and how long it is flown.</param>
    /// <returns>Accepted and refused steps across the legs.</returns>
    private static int Stitched(IReadOnlyList<(ITimeVaryingField Field, double Seconds)> legs)
    {
        var state = new PhaseState(Vec3.Zero, Vec3.Zero);
        var steps = 0;

        foreach (var (field, seconds) in legs)
        {
            var leg = TrajectoryIntegrator.Integrate(state, Ion, field, new IntegrationSettings { MaximumFlightTime = seconds });

            state = leg.FinalState;
            steps += leg.AcceptedSteps + leg.RejectedSteps;
        }

        return steps;
    }

    /// <summary>A static field asked as a time-varying one, for a leg flown through it alone.</summary>
    private static SequencedField Steady(IElectrostaticField field) => new SequencedField([field], [double.MaxValue]);

    private static CompiledElectrode Plate(double volts) => new()
    {
        Name = "pusher",
        Shape = ElectrodeShape.Rectangle,
        MinX = -0.0105,
        MaxX = -0.010,
        MinY = -0.01,
        MaxY = 0.01,
        Potential = volts,
    };

    private static CompiledSolvedField Pusher(IReadOnlyList<CompiledStage> stages) => new()
    {
        MinX = -0.011,
        MinY = -0.011,
        MaxX = 0.011,
        MaxY = 0.011,
        CellSize = 0.011 / 16.0,
        Tolerance = 1e-10,
        Electrodes = [Plate(1.0)],
        Stages = stages,
    };

    /// <summary>A sequenced field that counts every evaluation asked of it: field, potential and discontinuity.</summary>
    private sealed class Counting(ITimeVaryingField inner) : ITimeVaryingField
    {
        public long Evaluations { get; private set; }

        public double ResolutionLength => inner.ResolutionLength;

        public double OscillatingResolutionLength => inner.OscillatingResolutionLength;

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
        {
            Evaluations++;
            return inner.ElectricFieldAt(position, timeSeconds);
        }

        public double PotentialAt(in Vec3 position, double timeSeconds)
        {
            Evaluations++;
            return inner.PotentialAt(position, timeSeconds);
        }

        public Vec3 ElectricFieldBefore(in Vec3 position, double switchSeconds)
        {
            Evaluations++;
            return inner.ElectricFieldBefore(position, switchSeconds);
        }

        public double PotentialBefore(in Vec3 position, double switchSeconds)
        {
            Evaluations++;
            return inner.PotentialBefore(position, switchSeconds);
        }

        public Vec3 ElectricFieldAt(in Vec3 position)
        {
            Evaluations++;
            return inner.ElectricFieldAt(position);
        }

        public double PotentialAt(in Vec3 position)
        {
            Evaluations++;
            return inner.PotentialAt(position);
        }

        public double NextSwitchAfter(double timeSeconds) => inner.NextSwitchAfter(timeSeconds);

        public double SignedDistanceToDiscontinuity(in Vec3 position)
        {
            // The integrator counts a query of the field's discontinuity as an evaluation too.
            Evaluations++;
            return inner.SignedDistanceToDiscontinuity(position);
        }

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => inner.FieldFreeRunLength(position, direction);
    }
}
