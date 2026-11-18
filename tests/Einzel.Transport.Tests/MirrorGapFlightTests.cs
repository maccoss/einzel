using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Transport.Integration;

namespace Einzel.Transport.Tests;

/// <summary>
/// A reflected solve whose declared box stops short of the mirror plane is flown through its gap
/// to its closed form, and no surface is declared at the plane, where nothing is discontinuous.
/// </summary>
/// <remarks>
/// With the box ending at 9 mm and the plane at 10 mm, the image answered nothing on the declared
/// side and its measure there was infinite, beside the inner field's negative outside value just
/// beyond the plane: a change of sign at 10 mm with nothing discontinuous there, between the real
/// edges at 9 and 11 mm - the paired-surface layout that once cost an ion 2.6e-4 of its energy.
/// </remarks>
public sealed class MirrorGapFlightTests
{
    private const double Edge = 0.009;
    private const double Plane = 0.010;
    private const double Strength = 2.0e5;

    // A declared half: a uniform field along +x inside x in [0, 9 mm], nothing outside, its box
    // edge declared as a discontinuity the way a solved field declares it - positive inside.
    private sealed class Box : IElectrostaticField
    {
        private static bool Inside(in Vec3 p) => p.X >= 0.0 && p.X <= Edge;

        public Vec3 ElectricFieldAt(in Vec3 position) => Inside(in position) ? new Vec3(Strength, 0.0, 0.0) : default;

        public double PotentialAt(in Vec3 position) => Inside(in position) ? -Strength * position.X : 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            Inside(in position) ? Math.Min(position.X, Edge - position.X) : -Math.Max(-position.X, position.X - Edge);
    }

    // The same box, saying where it is free: nothing inside, up to its edge from outside, without
    // bound along a ray that never reaches it - as a solved field answers.
    private sealed class FreeBox : IElectrostaticField
    {
        private readonly Box _box = new();

        public Vec3 ElectricFieldAt(in Vec3 position) => _box.ElectricFieldAt(in position);

        public double PotentialAt(in Vec3 position) => _box.PotentialAt(in position);

        public double SignedDistanceToDiscontinuity(in Vec3 position) => _box.SignedDistanceToDiscontinuity(in position);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
        {
            if (position.X >= 0.0 && position.X <= Edge)
            {
                return 0.0;
            }

            var target = position.X > Edge ? Edge : 0.0;
            var along = (target - position.X) / direction.X;
            return along > 0.0 ? along : double.PositiveInfinity;
        }
    }

    [Theory]
    [InlineData(0.0095)]
    [InlineData(Plane)]
    public void AFieldFreeDriftAcrossThePlaneIsOneAnalyticAdvance(double startX)
    {
        // From the gap, or standing on the plane itself, to a detector in the image's gap short of
        // its box: field-free the whole way, so the flight is one analytic advance and no step.
        // The image's run was the distance to the plane alone, zero on the plane, so the drift
        // stopped there and an integrated step crossed the rest.
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var field = new Reflection(Plane, ImageAbove: true).Sum(new FreeBox());

        var v0 = species.SpeedAfterAcceleration(Quantity.From(100.0, "V")).SiValue;
        var launch = new PhaseState(new Vec3(startX, 0.0, 0.0), new Vec3(v0, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3 };
        const double detectorX = 0.0109;
        TrajectoryStopFunction detector = (in PhaseState s) => detectorX - s.Position.X;

        Assert.Equal(0.0011, field.FieldFreeRunLength(new Vec3(0.0099, 0.0, 0.0), new Vec3(1.0, 0.0, 0.0)), 15);
        Assert.Equal(0.001, field.FieldFreeRunLength(new Vec3(Plane, 0.0, 0.0), new Vec3(1.0, 0.0, 0.0)), 15);

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.Equal(0, result.AcceptedSteps);
        Assert.Equal(detectorX - startX, result.AnalyticDriftDistance, 15);
        Assert.Equal((detectorX - startX) / v0, result.FlightTimeSeconds, 15);
    }

    [Fact]
    public void AFlightThroughTheGapMatchesTheClosedForm()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var field = new Reflection(Plane, ImageAbove: true).Sum(new Box());

        var v0 = species.SpeedAfterAcceleration(Quantity.From(100.0, "V")).SiValue;
        var acceleration = species.ChargeSi * Strength / species.MassSi;
        var v1 = Math.Sqrt((v0 * v0) + (2.0 * acceleration * Edge));
        var accelerating = (v1 - v0) / acceleration;

        // Free 1 mm, accelerated over the box, free across the 2 mm gap, decelerated over the
        // image's box, free 1 mm to the detector.
        var exact = (2.0 * 0.001 / v0) + (2.0 * accelerating) + (2.0 * (Plane - Edge) / v1);

        var launch = new PhaseState(new Vec3(-0.001, 0.0, 0.0), new Vec3(v0, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3 };
        TrajectoryStopFunction detector = (in PhaseState s) => (2.0 * Plane) + 0.001 - s.Position.X;

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        // Measured 5.0e-9. Either half flown alone is 1e-10 to 1e-11; the rest is the sum of two
        // members that both declare edges, and it is the same with the plane rule removed - so
        // this pins the flight to its closed form, and the measure below pins the plane.
        Assert.True(relative < 1e-8, $"flight through the gap {result.FlightTimeSeconds:R} s against {exact:R} s, relative {relative:E3}");

        // No change of sign at the plane, where nothing is discontinuous: the gap side and the
        // image side of 10 mm agree in sign, and the real edges at 9 and 11 mm are the only ones.
        double Sign(double x) => Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(x, 0.0, 0.0)));

        Assert.Equal(Sign(Plane - 0.0005), Sign(Plane + 0.0005));
        Assert.NotEqual(Sign(Edge - 0.0005), Sign(Edge + 0.0005));
        Assert.NotEqual(Sign((2.0 * Plane) - Edge - 0.0005), Sign((2.0 * Plane) - Edge + 0.0005));
    }
}
