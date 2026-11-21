using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;

namespace Einzel.Transport.Tests;

/// <summary>A flight that ends on a switch of a sequenced field ends there, exactly.</summary>
/// <remarks>
/// The step cut to land on a switch belongs to the phase that is ending, and its last stages
/// used to sample the one starting. With a large jump between them the error estimate refused
/// the step, then a shorter one, and so on toward the switch until a step underflowed about an
/// attosecond short of it - a trajectory leg of a sequenced run ending at a field change with
/// its ion still in flight.
/// </remarks>
public sealed class SwitchLandingTests
{
    private static UniformField Uniform(double voltsPerMetre) => UniformField.Create(new Vec3(voltsPerMetre, 0.0, 0.0));

    private static TrajectoryResult Fly(ITimeVaryingField field, double seconds) =>
        TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            IonSpecies.FromMassToCharge(622.0, 1),
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
    /// The same on a leg whose clock is shifted onto the instrument's, where a leg's instant a
    /// hair before the switch rounds back onto it.
    /// </summary>
    [Fact]
    public void AShiftedLegBoundedAtASwitchReachesIt()
    {
        // Instants for which the leg's switch, mapped back onto the instrument's clock, lands an
        // ulp past the instrument's own: (end - start) + start rounds above end.
        const double start = 1.4014174343982395e-06;
        const double end = 3.0015222446891656e-05;

        Assert.True(end - start + start > end, "these instants no longer round past the switch");

        var instrument = new SequencedField([Uniform(1e3), Uniform(1e3), Uniform(2e6)], [start, end, end + 1e-6]);
        var leg = new TimeShiftedField(instrument, start);

        Assert.Equal(1e3, leg.ElectricFieldBefore(Vec3.Zero, end - start).X);

        var flight = Fly(leg, end - start);

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flight.Outcome);
    }

    /// <summary>On a boundary, a sequenced field asked from before answers with the phase ending.</summary>
    [Fact]
    public void FromBeforeABoundaryIsThePhaseEnding()
    {
        var field = new SequencedField([Uniform(1e3), Uniform(2e6)], [1e-7, 2e-7]);

        Assert.Equal(2e6, field.ElectricFieldAt(Vec3.Zero, 1e-7).X);
        Assert.Equal(1e3, field.ElectricFieldBefore(Vec3.Zero, 1e-7).X);
        Assert.Equal(1e3, new TimeShiftedField(field, 3e-8).ElectricFieldBefore(Vec3.Zero, 1e-7 - 3e-8).X);
    }
}
