using Einzel.Core.Geometry;
using Einzel.Core.Model;
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
public sealed class MirrorGapFlightTests(Xunit.Abstractions.ITestOutputHelper output)
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
        // A box ending short of the plane is continuous there - nothing on either side - as
        // GeometryBuilder.ReflectionOf marks it.
        var field = new Reflection(Plane, ImageAbove: true, EdgeIsContinuous: true).Sum(new FreeBox());

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

    // A declared half whose box ends on the plane, its field not zero there: the union's field
    // jumps from +S to -S across the plane, a real discontinuity both halves see a face at.
    private sealed class Faced : IElectrostaticField
    {
        private static bool Inside(in Vec3 p) => p.X >= 0.0 && p.X <= Plane;

        public Vec3 ElectricFieldAt(in Vec3 position) => Inside(in position) ? new Vec3(Strength, 0.0, 0.0) : default;

        public double PotentialAt(in Vec3 position) => Inside(in position) ? -Strength * position.X : 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            Inside(in position) ? Math.Min(position.X, Plane - position.X) : -Math.Max(-position.X, position.X - Plane);
    }

    // The same field written whole, as one field with its three surfaces: +S over the half, -S
    // over where the image is, nothing outside.
    private sealed class Whole : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 p) =>
            p.X < 0.0 || p.X > 2.0 * Plane ? default : new Vec3(p.X <= Plane ? Strength : -Strength, 0.0, 0.0);

        public double PotentialAt(in Vec3 p) =>
            p.X < 0.0 || p.X > 2.0 * Plane ? 0.0 : p.X <= Plane ? -Strength * p.X : -Strength * ((2.0 * Plane) - p.X);

        public double SignedDistanceToDiscontinuity(in Vec3 p)
        {
            var d = Math.Min(Math.Abs(p.X), Math.Min(Math.Abs(p.X - Plane), Math.Abs(p.X - (2.0 * Plane))));
            var crossed = (p.X > 0.0 ? 1 : 0) + (p.X > Plane ? 1 : 0) + (p.X > 2.0 * Plane ? 1 : 0);
            return crossed % 2 == 0 ? d : -d;
        }
    }

    // The faced half as a solve that declares none of its own surfaces - a volume solve, or a
    // cross-section with boundaryIsDiscontinuous false - so its measure is no surface anywhere.
    private sealed class Silent : IElectrostaticField
    {
        private readonly Faced _faced = new();

        public Vec3 ElectricFieldAt(in Vec3 position) => _faced.ElectricFieldAt(in position);

        public double PotentialAt(in Vec3 position) => _faced.PotentialAt(in position);

        public double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;
    }

    // The union of the silent half written whole: the same field, with the plane its one surface.
    private sealed class WholePlaneOnly : IElectrostaticField
    {
        private readonly Whole _whole = new();

        public Vec3 ElectricFieldAt(in Vec3 p) => _whole.ElectricFieldAt(in p);

        public double PotentialAt(in Vec3 p) => _whole.PotentialAt(in p);

        public double SignedDistanceToDiscontinuity(in Vec3 p) => Plane - p.X;
    }

    /// <summary>
    /// A half that declares no surfaces of its own, on an edge that is not a mirror, still gets the
    /// plane as a surface - the reflection declares it - and an ion crossing it flies exactly as
    /// through the same field written whole with the plane landed on. On a mirror edge it gets none.
    /// </summary>
    /// <remarks>
    /// The plane was declared only where the half's own measure had a face there, so a volume solve
    /// (which declares no surfaces) and a cross-section with <c>boundaryIsDiscontinuous: false</c>
    /// never declared it, whatever their edge: a grounded face on the plane, the union's normal
    /// field going from +E to -E, and nothing saying so.
    /// </remarks>
    [Fact]
    public void AHalfDeclaringNoSurfacesStillGetsThePlaneUnlessItIsAMirror()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var union = new Reflection(Plane, ImageAbove: true).Sum(new Silent());
        var mirror = new Reflection(Plane, ImageAbove: true, EdgeIsContinuous: true).Sum(new Silent());

        double Sign(IElectrostaticField f, double x) => Math.Sign(f.SignedDistanceToDiscontinuity(new Vec3(x, 0.0, 0.0)));

        Assert.NotEqual(Sign(union, Plane - 0.0005), Sign(union, Plane + 0.0005));
        Assert.Equal(Sign(mirror, Plane - 0.0005), Sign(mirror, Plane + 0.0005));

        var v0 = species.SpeedAfterAcceleration(Quantity.From(100.0, "V")).SiValue;
        var launch = new PhaseState(new Vec3(-0.001, 0.0, 0.0), new Vec3(v0, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3, RelativeTolerance = 1e-13 };
        TrajectoryStopFunction detector = (in PhaseState s) => (2.0 * Plane) + 0.001 - s.Position.X;

        var flown = TrajectoryIntegrator.Integrate(launch, species, union, settings, detector);
        var whole = TrajectoryIntegrator.Integrate(launch, species, new WholePlaneOnly(), settings, detector);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, flown.Outcome);
        Assert.Equal(whole.FlightTimeSeconds, flown.FlightTimeSeconds);
        Assert.Equal(whole.MaximumRelativeEnergyDrift, flown.MaximumRelativeEnergyDrift);
    }

    // A silent half with field only for |y| at most a millimetre, and free elsewhere along x, as a
    // solved field says where it is free.
    private sealed class Patch : IElectrostaticField
    {
        private const double HalfWidth = 0.001;

        private static bool In(in Vec3 p) => p.X >= 0.0 && p.X <= Plane && Math.Abs(p.Y) <= HalfWidth;

        public Vec3 ElectricFieldAt(in Vec3 position) => In(in position) ? new Vec3(Strength, 0.0, 0.0) : default;

        public double PotentialAt(in Vec3 position) => In(in position) ? -Strength * position.X : 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            Math.Abs(position.Y) > HalfWidth && direction.Y == 0.0 ? double.PositiveInfinity : 0.0;
    }

    /// <summary>
    /// On a grounded plane the plane is one surface wherever an ion crosses it: over the box's part,
    /// where the field jumps and the flight is that through the same field written whole, and
    /// beyond it, where nothing jumps and the one unavoidable change of sign is put - the drift cut
    /// there and the flight two analytic advances with no step between them.
    /// </summary>
    /// <remarks>
    /// The change beyond the box had been put on the image box's faces instead, which the declared
    /// side does not have; the union declared what its mirror did not.
    /// </remarks>
    [Fact]
    public void ThePlaneIsOneSurfaceInsideAndBeyondTheBox()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var union = new Reflection(Plane, ImageAbove: true).Sum(new Patch());

        double Sign(double x, double y) => Math.Sign(union.SignedDistanceToDiscontinuity(new Vec3(x, y, 0.0)));

        Assert.NotEqual(Sign(Plane - 0.0005, 0.0), Sign(Plane + 0.0005, 0.0));
        Assert.NotEqual(Sign(Plane - 0.0005, 0.005), Sign(Plane + 0.0005, 0.005));

        // Across the rim, on either side, nothing: no face of the image box.
        Assert.Equal(Sign(Plane + 0.002, 0.0), Sign(Plane + 0.002, 0.005));
        Assert.Equal(Sign(Plane - 0.002, 0.0), Sign(Plane - 0.002, 0.005));

        var v0 = species.SpeedAfterAcceleration(Quantity.From(100.0, "V")).SiValue;
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3, RelativeTolerance = 1e-13 };
        TrajectoryStopFunction detector = (in PhaseState s) => (2.0 * Plane) + 0.001 - s.Position.X;

        // Beyond the box: the drift is cut at the plane, lands there in one step, and resumes.
        var outside = new PhaseState(new Vec3(-0.001, 0.005, 0.0), new Vec3(v0, 0.0, 0.0));
        Assert.Equal(Plane + 0.001, union.FieldFreeRunLength(outside.Position, new Vec3(1.0, 0.0, 0.0)), 15);

        var flown = TrajectoryIntegrator.Integrate(outside, species, union, settings, detector);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, flown.Outcome);
        Assert.Equal(1, flown.AcceptedSteps);
        Assert.Equal(((2.0 * Plane) + 0.002) / v0, flown.FlightTimeSeconds, 15);

        // Inside it: the drift through the patch is cut at the plane, the plane is landed on, and
        // the flight is that through the same field written whole with the plane its one surface.
        var inside = new PhaseState(new Vec3(-0.001, 0.0, 0.0), new Vec3(v0, 0.0, 0.0));
        var landed = TrajectoryIntegrator.Integrate(inside, species, union, settings, detector);
        var whole = TrajectoryIntegrator.Integrate(inside, species, new WholePlaneOnly(), settings, detector);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, landed.Outcome);
        Assert.Equal(whole.FlightTimeSeconds, landed.FlightTimeSeconds);
    }

    // A neighbouring element with a real jump at the plane's x everywhere: a field along x past it,
    // nothing before it, the plane its one surface.
    private sealed class Neighbour : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 p) => p.X > Plane ? new Vec3(Strength, 0.0, 0.0) : default;

        public double PotentialAt(in Vec3 p) => p.X > Plane ? -Strength * (p.X - Plane) : 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 p) => Plane - p.X;
    }

    /// <summary>
    /// The plane continued beyond the box, where the union's field does not jump, costs at most a
    /// landing: a neighbouring element's real surface lying there is landed on exactly as it is
    /// with no union beside it.
    /// </summary>
    /// <remarks>
    /// Combined into one sign, the union's plane and the neighbour's surface at the same place
    /// cancelled, and the ion crossed the neighbour's jump with no landing. Tracked as members, two
    /// changes at one place are one landing.
    /// </remarks>
    [Fact]
    public void ThePlaneBeyondTheBoxNeverCancelsANeighbour()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var union = new Reflection(Plane, ImageAbove: true).Sum(new Patch());
        var together = new SuperposedField([union, new Neighbour()]);

        // Combined, the two cancel beyond the box: the sign is the same on both sides.
        Assert.Equal(
            Math.Sign(together.SignedDistanceToDiscontinuity(new Vec3(Plane - 0.0005, 0.005, 0.0))),
            Math.Sign(together.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.0005, 0.005, 0.0))));

        var v0 = species.SpeedAfterAcceleration(Quantity.From(100.0, "V")).SiValue;
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3, RelativeTolerance = 1e-13 };
        TrajectoryStopFunction detector = (in PhaseState s) => (2.0 * Plane) + 0.001 - s.Position.X;
        var launch = new PhaseState(new Vec3(-0.001, 0.005, 0.0), new Vec3(v0, 0.0, 0.0));

        var beside = TrajectoryIntegrator.Integrate(launch, species, together, settings, detector);
        var alone = TrajectoryIntegrator.Integrate(launch, species, new Neighbour(), settings, detector);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, beside.Outcome);
        Assert.Equal(alone.FlightTimeSeconds, beside.FlightTimeSeconds);
        Assert.Equal(alone.MaximumRelativeEnergyDrift, beside.MaximumRelativeEnergyDrift);
    }

    // The first landing a flight made: the first sample forced by a landing, after the launch.
    private static double FirstLandingX(PhaseState launch, IElectrostaticField field, double step, double detectorX)
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var recorder = new TrajectoryRecorder(1e9);
        var settings = new IntegrationSettings { MaximumFlightTime = 100.0, InitialStep = step, MaximumStep = step };
        TrajectoryStopFunction detector = (in PhaseState s) => detectorX - s.Position.X;

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, recorder);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        return recorder.Samples.Skip(1).First().Position.X;
    }

    /// <summary>
    /// A step that crosses a bounded element's own plane and leaves its box together lands on the
    /// plane, the earlier of the two.
    /// </summary>
    /// <remarks>
    /// The inner field's member was infinite outside the box and a member whose end was infinite
    /// left the step's test, so the step from 9.9 to 10.6 mm - the plane at 10, the box's face at
    /// 10.5 - landed on the face and crossed the plane with no landing.
    /// </remarks>
    [Fact]
    public void AnInnerPlaneCrossedOnTheWayOutOfItsBoxIsLandedOn()
    {
        var region = new FieldRegion(0.0, 0.0105, -0.01, 0.01, -0.01, 0.01);
        var inner = HalfSpaceUniformField.Create(new Vec3(0.010, 0.0, 0.0), new Vec3(1.0, 0.0, 0.0), Quantity.From(0.0, "V/m"));
        var bounded = Einzel.Fields.Analytic.BoundedField.Around(inner, region);

        // 1000 m/s, one step of 0.7 us: 9.9 mm to 10.6 mm.
        var launch = new PhaseState(new Vec3(0.0099, 0.0, 0.0), new Vec3(1000.0, 0.0, 0.0));
        var first = FirstLandingX(launch, bounded, 7e-7, 0.02);

        Assert.Equal(0.010, first, 9);

        // The inner member is continuous across the box face - its own sign on both sides - so a
        // probe that bows outside the box reads that sign, and not an infinity counted as a crossing.
        var insideFace = new Vec3(0.0104, 0.0, 0.0);
        var outsideFace = new Vec3(0.0106, 0.0, 0.0);
        Assert.Equal(
            Math.Sign(bounded.MemberDistanceToDiscontinuity(1, in insideFace)),
            Math.Sign(bounded.MemberDistanceToDiscontinuity(1, in outsideFace)));
        Assert.True(double.IsFinite(bounded.MemberDistanceToDiscontinuity(1, in outsideFace)));
    }

    // Two planes, the second reading exactly zero over a band around it - so a probe there reads as
    // on it - and no field.
    private sealed class TwoPlanes(double first, double second) : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 position) => default;

        public double PotentialAt(in Vec3 position) => 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            DiscontinuityDistance.Combine(MemberDistanceToDiscontinuity(0, in position), MemberDistanceToDiscontinuity(1, in position));

        public int DiscontinuityMembers => 2;

        // The later surface is member 0, so a probe asked "on?" before "past?" meets it first.
        public double MemberDistanceToDiscontinuity(int member, in Vec3 position) =>
            member == 1 ? first - position.X
                : Math.Abs(second - position.X) < 1e-6 ? 0.0
                : second - position.X;
    }

    /// <summary>
    /// A bisection probe exactly on a later member's surface, with an earlier member already
    /// passed, is past: the flight lands on the earlier surface.
    /// </summary>
    /// <remarks>
    /// The probe returned "on a surface" at the first member reading exactly zero, before asking
    /// whether another had already changed sign, and the flight landed on the later surface,
    /// straddling the earlier one.
    /// </remarks>
    [Fact]
    public void AProbeOnALaterSurfaceDoesNotStraddleAnEarlierOne()
    {
        // 1 m/s, one step of 1 s from 0 to 1 m: the first probe, at half a step, is at 0.5 m - on the
        // second plane - with the first, at 0.25 m, already crossed.
        var launch = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(1.0, 0.0, 0.0));
        var first = FirstLandingX(launch, new TwoPlanes(0.25, 0.5), 1.0, 2.0);

        Assert.Equal(0.25, first, 9);
    }

    // Planes, one member each, counting how many times all their measures are read in one pass.
    private sealed class CountingPlanes(params double[] planes) : IElectrostaticField
    {
        public int Passes { get; private set; }

        public Vec3 ElectricFieldAt(in Vec3 position) => default;

        public double PotentialAt(in Vec3 position) => 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) => planes[0] - position.X;

        public int DiscontinuityMembers => planes.Length;

        public double MemberDistanceToDiscontinuity(int member, in Vec3 position) => planes[member] - position.X;

        public void MemberDistancesToDiscontinuity(in Vec3 position, Span<double> distances)
        {
            Passes++;

            for (var k = 0; k < planes.Length; k++)
            {
                distances[k] = planes[k] - position.X;
            }
        }
    }

    // A member negative before a surface and infinite past it - a sign like any other - beside a
    // member that never changes.
    private sealed class InfinitePast(double surface) : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 position) => default;

        public double PotentialAt(in Vec3 position) => 0.0;

        public int DiscontinuityMembers => 2;

        public double MemberDistanceToDiscontinuity(int member, in Vec3 position) =>
            member == 1 ? 1.0 : position.X < surface ? position.X - surface : double.PositiveInfinity;
    }

    /// <summary>
    /// A member whose measure is infinite at one end of a step and finite, of the other sign, at
    /// the other has changed sign: the flight lands there.
    /// </summary>
    /// <remarks>
    /// A member with an infinite end left the step's test, so its surface was crossed with no landing.
    /// </remarks>
    [Fact]
    public void AnInfiniteMeasureIsASign()
    {
        var launch = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(1.0, 0.0, 0.0));

        Assert.Equal(0.5, FirstLandingX(launch, new InfinitePast(0.5), 1.0, 2.0), 9);
    }

    /// <summary>
    /// A step crossing several members' surfaces is one bisection, to the earliest: two landings
    /// in a flight cost two bisections' probes, not one per member crossed.
    /// </summary>
    /// <remarks>
    /// A bisection per changing member, then the earliest, was the landing work times the number of
    /// members crossing in the step - three at a grounded plane, where the half's face, the plane and
    /// the image's face all change together.
    /// </remarks>
    [Fact]
    public void ALandingIsOneBisectionHoweverManyMembersCross()
    {
        // Three members changing sign at one place, against one member changing there beside one
        // that never does: the same flight, the same landing, and - one bisection either way - the
        // same number of passes.
        var launch = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(1.0, 0.0, 0.0));
        var three = new CountingPlanes(0.25, 0.25, 0.25);
        var one = new CountingPlanes(0.25, 100.0);

        Assert.Equal(0.25, FirstLandingX(launch, three, 1.0, 2.0), 9);
        Assert.Equal(0.25, FirstLandingX(launch, one, 1.0, 2.0), 9);

        output.WriteLine($"{three.Passes} passes with three members crossing together, {one.Passes} with one");
        Assert.Equal(one.Passes, three.Passes);
    }

    /// <summary>
    /// Where the union's field jumps at the plane, the sum declares one surface there - its sign
    /// changes across it - and an ion crossing it flies exactly as through the same field written
    /// whole, with the plane landed on.
    /// </summary>
    /// <remarks>
    /// Both halves have a face on the plane and each declared a change of sign there; the sum
    /// multiplies its members' signs, so the two cancelled and the composite reported no surface -
    /// Stage 4's paired-surface failure, at the one plane a reflected solve always has. Measured at
    /// a relative tolerance of 1e-13: 1.79e-10 against the closed form, twice the 8.88e-11 of the
    /// same field written whole, and an energy drift of 1.9e-9 against 9.7e-10. The declared half
    /// owns the plane now; the union flies the whole field's flight to the last digit.
    /// </remarks>
    [Fact]
    public void AJumpAtThePlaneIsOneSurfaceAndIsLandedOn()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var union = new Reflection(Plane, ImageAbove: true).Sum(new Faced());

        double Sign(double x) => Math.Sign(union.SignedDistanceToDiscontinuity(new Vec3(x, 0.0, 0.0)));

        Assert.NotEqual(Sign(Plane - 0.0005), Sign(Plane + 0.0005));

        var v0 = species.SpeedAfterAcceleration(Quantity.From(100.0, "V")).SiValue;
        var acceleration = species.ChargeSi * Strength / species.MassSi;
        var v1 = Math.Sqrt((v0 * v0) + (2.0 * acceleration * Plane));

        // Free 1 mm, accelerated over the half, decelerated over its image, free 1 mm.
        var exact = (2.0 * 0.001 / v0) + (2.0 * (v1 - v0) / acceleration);

        var launch = new PhaseState(new Vec3(-0.001, 0.0, 0.0), new Vec3(v0, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3, RelativeTolerance = 1e-13 };
        TrajectoryStopFunction detector = (in PhaseState s) => (2.0 * Plane) + 0.001 - s.Position.X;

        var flown = TrajectoryIntegrator.Integrate(launch, species, union, settings, detector);
        var whole = TrajectoryIntegrator.Integrate(launch, species, new Whole(), settings, detector);
        var relative = Math.Abs(flown.FlightTimeSeconds - exact) / exact;

        Assert.Equal(TrajectoryOutcome.StopConditionMet, flown.Outcome);
        Assert.Equal(whole.FlightTimeSeconds, flown.FlightTimeSeconds);
        Assert.Equal(whole.MaximumRelativeEnergyDrift, flown.MaximumRelativeEnergyDrift);

        // No more work than the whole field: a step's start is read from the last step's end where
        // nothing moved it, so the union does less (the count of bisections is checked apart, in
        // ALandingIsOneBisectionHoweverManyMembersCross).
        output.WriteLine($"{flown.FieldEvaluations} field evaluations for the union, {whole.FieldEvaluations} for the whole field");
        Assert.True(flown.FieldEvaluations <= whole.FieldEvaluations, $"{flown.FieldEvaluations} against {whole.FieldEvaluations}");
        Assert.True(relative < 1e-10, $"flight across the plane {flown.FlightTimeSeconds:R} s against {exact:R} s, relative {relative:E3}");
    }

    [Fact]
    public void AFlightThroughTheGapMatchesTheClosedForm()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var field = new Reflection(Plane, ImageAbove: true, EdgeIsContinuous: true).Sum(new Box());

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
