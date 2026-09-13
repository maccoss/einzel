using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;
using Einzel.Transport;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A quadrupole bent around an arc: the first device here whose geometry is invariant
/// under nothing.
/// </summary>
/// <remarks>
/// <para>
/// A cross-section assumes the geometry repeats along an axis and an axisymmetric solve
/// assumes it repeats all the way round. A curved axis does neither, so this needs a
/// genuine volume solve — and the rods are chains of overlapping spheres rather than
/// cylinders, because a cylinder in this format is axis-aligned and a bent rod is not.
/// That needed no new primitive: <c>repeat</c> binds an index and <c>cosPi</c>/<c>sinPi</c>
/// can place a bead anywhere.
/// </para>
/// <para>
/// <b>The claim is that the RF is what carries the ion round the bend</b>, and it needs
/// both halves to mean anything. An ion that follows the arc proves nothing on its own —
/// it might simply be going straight down a wide bore. The control is the same model with
/// the amplitude at zero, where the ion must leave the axis quadratically and hit
/// something.
/// </para>
/// </remarks>
public sealed class CTrapTests(ITestOutputHelper output)
{
    private static CompiledModel Compile(params (string Name, Quantity Value)[] overrides)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("c-trap"));

        var settings = overrides.ToDictionary(o => o.Name, o => o.Value, StringComparer.Ordinal);

        var validation = ModelValidator.Validate(document, settings.Count == 0 ? null : settings);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    private static (TrajectoryResult Result, IReadOnlyList<TrajectorySample> Samples) Fly(
        CompiledModel model)
    {
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var recorder = new TrajectoryRecorder(model.MaximumFlightTimeSi / 600.0, capacity: 4096);

        var result = TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = model.RelativeTolerance,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector,
            recorder);

        return (result, recorder.Samples);
    }

    /// <summary>Distance from the arc the trap axis follows, in metres.</summary>
    private static double OffAxis(in Vec3 p, double bendRadius) =>
        Math.Abs(Math.Sqrt((p.X * p.X) + (p.Y * p.Y)) - bendRadius);

    /// <summary>Four bent rods reduce to one basis solve.</summary>
    /// <remarks>
    /// <para>
    /// The in-plane pair and the out-of-plane pair are exact negatives, so the whole
    /// structure is one spatial pattern carrying one weight — however many beads each rod
    /// is built from, and whether or not it is bent. Exact negation is what does it, which
    /// is why the amplitudes are written as <c>rfAmplitude</c> and <c>-rfAmplitude</c>
    /// rather than as a cosine of a pole index: the second would be right to a rounding
    /// and would split into two channels.
    /// </para>
    /// <para>
    /// It matters more here than in a straight quadrupole. This is a volume solve, so a
    /// second channel is not a small cost — it is another pass over the whole grid.
    /// </para>
    /// </remarks>
    [Fact]
    public void FourBentRodsAreOneBasisSolve()
    {
        var model = Compile();
        var solve = model.Fields[0].Solve3D!;

        var channels = Fields.Solved.GeometryBuilder3D.SolveChannels(
            new Fields.Solved.Geometry3D(
                solve.MinX, solve.MinY, solve.MinZ,
                solve.MaxX, solve.MaxY, solve.MaxZ,
                solve.CellSize,
                solve.Electrodes,
                solve.Tolerance)
            {
                Drives = solve.Drives,
                Stages = solve.Stages,
            }).ToList();

        output.WriteLine(
            $"{solve.Electrodes.Count} electrode declarations, "
            + $"{channels.Count} basis channel(s)");

        Assert.Single(channels);
    }

    /// <summary>The rods are smooth along the arc: a swept profile does not scallop.</summary>
    /// <remarks>
    /// <para>
    /// <b>This test replaces one that had stopped measuring anything.</b> While the rods
    /// were chains of overlapping spheres it asserted that consecutive beads intersect -
    /// spaced further apart than their radius a rod is a string of pearls and the field
    /// reaches through the gaps, which would look like a working trap with a mysteriously
    /// poor acceptance. It grouped the electrodes by the stem of their names and compared
    /// consecutive members of each group. With the rods swept, each group has ONE member,
    /// the inner loop never runs, and the worst spacing stays at the zero it was
    /// initialized to. It passed, and a vacuous truth over an empty collection is a thing
    /// this project has now been caught by five times.
    /// </para>
    /// <para>
    /// <b>What the swept geometry has to be asserted about instead is that it is smooth</b>,
    /// which is the property the beads failed: thirteen spheres of 3.439 mm radius on a
    /// 3.459 mm pitch scalloped each rod by 13.6 percent of its own radius. So this walks
    /// the trap axis round the arc and asks how far the nearest metal is at each step. A
    /// swept profile gives the same answer everywhere by construction; a beaded one ripples
    /// at the bead pitch.
    /// </para>
    /// <para>
    /// Measured on the geometry rather than on the field, because the claim is geometric
    /// and exact - and the signed distance is the same function the solver's cut cells and
    /// the ion absorber both use, so it is the quantity that would have been wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSweptRodsAreSmoothAlongTheArc()
    {
        var model = Compile();

        var solve = model.Fields[0].Solve3D!;
        var electrodes = solve.Electrodes;

        Assert.Equal(5, electrodes.Count);

        var bend = model.Parameters["bendRadius"].SiValue;
        var arc = model.Parameters["arcHalfTurns"].SiValue;

        var nearest = new List<double>();

        // Along the trap axis - the circle of the bend radius, in the plane of the arc -
        // staying clear of the two ends, where the rods legitimately stop.
        for (var k = 0; k <= 200; k++)
        {
            var half = arc * (0.1 + (0.8 * k / 200.0));

            var x = bend * double.CosPi(half);
            var y = bend * double.SinPi(half);

            nearest.Add(electrodes.Min(e => e.SignedDistance(x, y, 0.0)));
        }

        var low = nearest.Min();
        var high = nearest.Max();
        var ripple = (high - low) / high;

        output.WriteLine(
            $"nearest metal from the trap axis: {low * 1e3:F6} to {high * 1e3:F6} mm, "
            + $"ripple {ripple * 100.0:E3} percent of the gap");

        // The beads rippled by 13.6 percent of a rod radius. A swept profile is the same
        // solid at every angle, so what is left is the arithmetic of turning an angle into
        // a point and back.
        Assert.True(
            ripple < 1.0e-9,
            $"the gap to the nearest rod varies {ripple * 100.0:F3} percent round the arc, "
            + "so the rods are not surfaces of revolution");

        // And the control, so a constant is not a constant nothing: the axis really is
        // inside the bore, at about the inscribed radius rather than at zero or at the
        // rod depth.
        Assert.Equal(model.Parameters["inscribedRadius"].SiValue, high, 5);
    }

    /// <summary>The RF carries an ion round the bend, and without it the ion is lost.</summary>
    /// <remarks>
    /// <para>
    /// <b>Both halves, because neither is worth anything alone.</b> An ion that reaches the
    /// end of the arc might be flying straight down a generous bore; an ion that is lost
    /// might have been badly launched. What says the RF is doing the work is that the same
    /// launch, in the same geometry, arrives with the drive on and dies on a rod with it
    /// off.
    /// </para>
    /// <para>
    /// The size of the effect is set by geometry rather than by tuning: over an arc length
    /// L a straight line departs from a circle of radius R by about L squared over 2R,
    /// which for this template is millimetres against an inscribed radius of three. So the
    /// unguided ion cannot reach the end whatever else is true.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDriveIsWhatCarriesTheIonRoundTheBend()
    {
        const double Bend = 20.0e-3;

        var (guided, guidedPath) = Fly(Compile());
        var (adrift, adriftPath) = Fly(Compile(("rfAmplitude", Quantity.From(0.0, "V"))));

        var guidedWorst = guidedPath.Max(s => OffAxis(s.Position, Bend));
        var adriftWorst = adriftPath.Max(s => OffAxis(s.Position, Bend));

        output.WriteLine(
            $"drive on : {guided.Outcome,-18} after {guided.FlightTimeSeconds * 1e6,7:F2} us, "
            + $"worst {guidedWorst * 1e6,8:F1} um off the arc");

        output.WriteLine(
            $"drive off: {adrift.Outcome,-18} after {adrift.FlightTimeSeconds * 1e6,7:F2} us, "
            + $"worst {adriftWorst * 1e6,8:F1} um off the arc");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, guided.Outcome);

        Assert.NotEqual(TrajectoryOutcome.StopConditionMet, adrift.Outcome);

        // Bounded against unbounded, not big against small. Comparing the two WORST
        // excursions was the first version of this assertion and it was comparing two
        // different kinds of quantity: the guided ion's is the amplitude of an
        // oscillation it returns from, and the unguided one's is how far it had got when
        // it hit something. They came out 586 um against 3004 um - a ratio of five, which
        // reads as "not very different" and is nothing of the sort.
        //
        // What separates them is that the guided ion COMES BACK. Its final distance from
        // the axis is a small fraction of its worst, because the RF keeps returning it;
        // the unguided one's final IS its worst, because it left and never turned round.
        // How close it comes back over the LATER part of the flight. Not its final
        // distance, which was the second wrong version of this: an oscillating quantity
        // sampled at one arbitrary instant is anywhere in its range, and the guided ion
        // happened to be caught at 61% of its amplitude. What distinguishes a bounded
        // oscillation is that it keeps returning, so the quantity to look at is the
        // closest approach after the motion has settled.
        static double ClosestLate(IReadOnlyList<TrajectorySample> path, double bend) =>
            path.Skip(path.Count / 2).Min(s => OffAxis(s.Position, bend));

        var guidedReturn = ClosestLate(guidedPath, Bend);
        var adriftReturn = ClosestLate(adriftPath, Bend);

        output.WriteLine(
            $"drive on : comes back to {guidedReturn * 1e6,8:F1} um, "
            + $"{guidedReturn / guidedWorst:P1} of its worst");

        output.WriteLine(
            $"drive off: comes back to {adriftReturn * 1e6,8:F1} um, "
            + $"{adriftReturn / adriftWorst:P1} of its worst");

        Assert.True(
            guidedReturn < 0.25 * guidedWorst,
            $"over the second half of its flight the guided ion never came closer than "
            + $"{guidedReturn * 1e6:F1} um to the axis, against a worst of "
            + $"{guidedWorst * 1e6:F1} - so it is drifting away rather than oscillating "
            + "about the axis, which is what confinement means");

        // Against the GUIDED ion rather than against its own worst, which was the third
        // wrong version of this assertion. The unguided ion strikes a rod at 25.9 us, so
        // the second half of its path begins when it is already most of a millimetre out
        // - its own worst is not a scale it ever returns from, it is simply where it
        // stopped. What means something is that one of these two comes back to the axis
        // and the other never approaches it.
        Assert.True(
            adriftReturn > 20.0 * guidedReturn,
            $"the unguided ion's closest approach over the second half of its flight is "
            + $"{adriftReturn * 1e6:F1} um against the guided one's "
            + $"{guidedReturn * 1e6:F1} um. Too close to say the drive is what returns the "
            + "ion to the axis");

        // And it never gets near the rods while guided: the inscribed radius is the
        // distance at which it would hit one.
        Assert.True(
            guidedWorst < 3.0e-3,
            $"the guided ion reached {guidedWorst * 1e3:F3} mm off axis, which is the "
            + "inscribed radius - it is not being held, it is being missed");
    }

    /// <summary>Flies one ion with no detector, recording finely.</summary>
    /// <remarks>
    /// The model's own flight ceiling is a HOLD time — a trap is asked to keep ions for as
    /// long as the instrument wants them. An ejection is over in microseconds, so the
    /// ceiling is supplied here instead; letting an ion that missed the slot rattle for the
    /// full hold was most of the cost of this measurement and none of its information.
    /// </remarks>
    private static IReadOnlyList<TrajectorySample> Eject(
        CompiledModel model,
        IElectrostaticField field,
        double stepUs,
        double maxUs)
    {
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        // No detector: an ejected ion flies inward toward the arc centre, which is where
        // the analyser would be and where this model has nothing at all. What is wanted is
        // the whole path, so the stop function never fires.
        var recorder = new TrajectoryRecorder(stepUs * 1e-6, capacity: 8192);

        TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = model.RelativeTolerance,
                MaximumFlightTime = maxUs * 1e-6,
            },
            (in PhaseState _) => 1.0,
            recorder);

        return recorder.Samples;
    }

    /// <summary>Position of one ion at a given time, linearly between samples.</summary>
    private static Vec3? At(IReadOnlyList<TrajectorySample> path, double t)
    {
        if (path.Count == 0 || t < path[0].TimeSeconds || t > path[^1].TimeSeconds)
        {
            return null;
        }

        for (var k = 1; k < path.Count; k++)
        {
            if (path[k].TimeSeconds < t)
            {
                continue;
            }

            var span = path[k].TimeSeconds - path[k - 1].TimeSeconds;
            var f = span <= 0.0 ? 0.0 : (t - path[k - 1].TimeSeconds) / span;

            return path[k - 1].Position + ((path[k].Position - path[k - 1].Position) * f);
        }

        return path[^1].Position;
    }

    private static Vec3 Centroid(Vec3[] points)
    {
        var sum = new Vec3(0.0, 0.0, 0.0);

        foreach (var p in points)
        {
            sum += p;
        }

        return sum * (1.0 / points.Length);
    }

    /// <summary>RMS distance of a set of points from their own centroid.</summary>
    private static double Extent(Vec3[] points)
    {
        var centre = Centroid(points);
        var sum = 0.0;

        foreach (var p in points)
        {
            var d = p - centre;
            sum += Vec3.Dot(d, d);
        }

        return Math.Sqrt(sum / points.Length);
    }

    /// <summary>What an ejected packet does: where it is narrowest and how narrow.</summary>
    /// <param name="LaunchExtent">RMS spread of the ions at launch, in metres.</param>
    /// <param name="Waist">RMS spread at its narrowest, in metres.</param>
    /// <param name="Travelled">Distance the packet centroid covered to get there.</param>
    /// <param name="WaistRadius">Where that is, as a radius from the arc centre.</param>
    private sealed record FocusResult(
        double LaunchExtent,
        double Waist,
        double Travelled,
        double WaistRadius)
    {
        /// <summary>How much narrower the packet is at its waist than at launch.</summary>
        /// <remarks>
        /// Exactly 1 for a straight trap, whatever the field does, because a parallel
        /// ejection is a rigid translation and a translation preserves every distance.
        /// So this needs no second run to compare against.
        /// </remarks>
        public double Convergence => LaunchExtent / Waist;
    }

    /// <summary>Ejects a spread of ions and finds the waist of the packet they make.</summary>
    private FocusResult MeasureFocus(
        double bendMm, double rfVolts, double phase, bool trace, double slotMm = 0.5)
    {
        const double Spread = 0.04;   // half turns either side of the slot centre
        const double EjectVolts = 60.0;

        var offsets = new[] { -Spread, -Spread / 2.0, 0.0, Spread / 2.0, Spread };

        var paths = new List<IReadOnlyList<TrajectorySample>>();

        // One solve for the whole spread. Where round the arc an ion starts changes the
        // launch and nothing about the geometry, so re-solving per ion would be five
        // passes over a volume to compute the same field five times.
        IElectrostaticField? field = null;

        foreach (var offset in offsets)
        {
            var model = Compile(
                ("bendRadius", Quantity.From(bendMm, "mm")),
                ("ejectVolts", Quantity.From(EjectVolts, "V")),
                ("rfAmplitude", Quantity.From(rfVolts, "V")),
                ("ejectPhase", Quantity.Number(phase)),
                ("slotHalfWidth", Quantity.From(slotMm, "mm")),
                // Cooled. An ion still running along the arc leaves at an angle to its own
                // radius, which is an aberration on the focus rather than a focus.
                ("launchVolts", Quantity.From(0.005, "V")),
                ("launchHalfTurns", Quantity.From(0.25 + offset, "1")));

            field ??= FieldAssembly.Build(model);

            paths.Add(Eject(model, field, stepUs: 0.01, maxUs: 24.0));
        }

        var launchExtent = Extent([.. paths.Select(p => p[0].Position)]);

        var last = paths.Min(p => p[^1].TimeSeconds);

        var waist = double.MaxValue;
        var waistTime = 0.0;

        if (trace)
        {
            output.WriteLine(
                $"bend radius {bendMm:F1} mm, RF {rfVolts:F0} V at phase {phase:F2}, "
                + $"slot half-width {slotMm:F2} mm, "
                + $"launch extent {launchExtent * 1e3:F3} mm");

            output.WriteLine("     t/us   centroid r/mm   packet extent/mm");
        }

        for (var k = 1; k <= 400; k++)
        {
            var t = last * k / 400.0;

            var points = paths.Select(p => At(p, t)).ToList();

            if (points.Any(q => q is null))
            {
                continue;
            }

            var here = points.Select(q => q!.Value).ToArray();
            var extent = Extent(here);

            if (extent < waist)
            {
                waist = extent;
                waistTime = t;
            }

            if (trace && k % 40 == 0)
            {
                var centre = Centroid(here);

                output.WriteLine(
                    $"  {t * 1e6,7:F3}   "
                    + $"{Math.Sqrt((centre.X * centre.X) + (centre.Y * centre.Y)) * 1e3,13:F3}"
                    + $"   {extent * 1e3,16:F4}");
            }
        }

        var atWaist = Centroid([.. paths.Select(p => At(p, waistTime)!.Value)]);
        var atLaunch = Centroid([.. paths.Select(p => p[0].Position)]);

        return new FocusResult(
            launchExtent,
            waist,
            Math.Sqrt(Vec3.Dot(atWaist - atLaunch, atWaist - atLaunch)),
            Math.Sqrt((atWaist.X * atWaist.X) + (atWaist.Y * atWaist.Y)));
    }

    /// <summary>A curved trap ejects a converging packet, and its waist is the arc center.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is what the curvature is for.</b> Every ion in a curved trap is pushed out
    /// along its own radius, so their velocities all point inward and the packet converges
    /// as it flies - it arrives at the analyzer spatially focused rather than as a line. A
    /// straight trap pushes every ion in the SAME direction, so whatever length of trap the
    /// ions occupied, they still occupy after the flight. The template has claimed this in
    /// its description since it was written and nothing measured it.
    /// </para>
    /// <para>
    /// The comparison against a straight trap needs no second run because it is arithmetic:
    /// a rigid translation preserves every distance, so a parallel ejection carries the
    /// launch extent through unchanged whatever the field does, and the convergence measured
    /// here is exactly 1 for one.
    /// </para>
    /// <para>
    /// <b>The waist sits at the center of curvature, and an earlier reading of this that
    /// said otherwise is withdrawn.</b> Velocities aimed along radii meet one bend radius
    /// away, and that is where they are found: the packet travels 1.011 bend radii at both
    /// 20 mm and 15 mm and ends 2.4 and 2.3 percent of a bend radius short of the center.
    /// The previous measurement put it at 1.73 and 1.92 bend radii and explained the excess
    /// as an aperture lens at the slot, with a thin-lens fit that then mispredicted the
    /// longer bend by 17 percent - a formula carrying an error dressed as a model, and it
    /// was recorded as one. It was the geometry. Those numbers were taken on rods built as
    /// chains of overlapping spheres, scalloped by 13.6 percent of their own radius, with
    /// a slot modeled as an angular gap that only let out an angular slice of the packet;
    /// that model no longer exists, so the shift is attributed to the geometry as a whole
    /// rather than to either half of it.
    /// </para>
    /// <para>
    /// <b>The slot is not the lens, which is now measured rather than argued</b> - see
    /// <see cref="TheFocusFollowsTheBendAndNotTheSlot"/>. What the slot does set is how
    /// much of the packet gets out.
    /// </para>
    /// <para>
    /// The control for "is it the curvature" is to change the bend radius and watch the
    /// focus follow, which is why this runs at two.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(20.0)]
    [InlineData(15.0)]
    public void CurvatureFocusesTheEjectedPacket(double bendMm)
    {
        var focus = MeasureFocus(bendMm, rfVolts: 0.0, phase: 0.0, trace: true);

        var bend = bendMm * 1e-3;

        output.WriteLine(
            $"waist {focus.Waist * 1e3:F4} mm, {focus.Travelled * 1e3:F3} mm from launch "
            + $"= {focus.Travelled / bend:F3} bend radii, "
            + $"{focus.WaistRadius * 1e3:F3} mm from the arc center "
            + $"= {focus.WaistRadius / bend:F3} of one");

        output.WriteLine(
            $"launch extent / waist = {focus.Convergence:F1}x; a straight trap would be 1.0x");

        // It focuses at all. A parallel ejection gives exactly 1.0 here whatever the field
        // does, so anything well above 1 is the curvature.
        Assert.True(
            focus.Convergence > 5.0,
            $"the packet went from {focus.LaunchExtent * 1e3:F3} mm at launch to "
            + $"{focus.Waist * 1e3:F3} mm at its narrowest, a factor of "
            + $"{focus.Convergence:F2}. That is not a focus");

        // And it focuses WHERE the geometry says, which is the sharp half: one bend radius
        // of travel, ending on the center of curvature. Both are asserted because either
        // alone is weak - a packet could travel the right distance in the wrong direction,
        // and a packet could end near the center by starting near it.
        Assert.Equal(1.0, focus.Travelled / bend, 1);

        Assert.True(
            focus.WaistRadius < 0.05 * bend,
            $"the waist is {focus.WaistRadius * 1e3:F3} mm from the arc center, "
            + $"{focus.WaistRadius / bend:F3} of a bend radius - so the velocities are not "
            + "meeting where radii meet");
    }

    /// <summary>The focus follows the bend radius and is indifferent to the slot's opening.</summary>
    /// <remarks>
    /// <para>
    /// <b>The control that retires the aperture-lens explanation.</b> The earlier account of
    /// this device had the slot acting as a lens - accelerated up to it, field-free after
    /// it - to explain a waist at 1.73 and 1.92 bend radii rather than at 1. A lens has a
    /// strength, and an aperture lens's strength depends on its opening, so widening the
    /// slot has to move the focus if that is what is happening.
    /// </para>
    /// <para>
    /// Over a fourfold range of slot width the waist stays at the center of curvature. So
    /// the slot is a hole rather than an element, and where the analyzer goes is set by the
    /// bend alone - which is the useful form of the finding for somebody placing one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFocusFollowsTheBendAndNotTheSlot()
    {
        const double BendMm = 20.0;

        var travelled = new List<double>();

        foreach (var slotMm in (double[])[0.25, 0.5, 1.0])
        {
            var focus = MeasureFocus(BendMm, rfVolts: 0.0, phase: 0.0, trace: false, slotMm: slotMm);

            travelled.Add(focus.Travelled);

            output.WriteLine(
                $"  slot half-width {slotMm:F2} mm: waist {focus.Waist * 1e3:F4} mm at "
                + $"{focus.Travelled * 1e3:F3} mm = {focus.Travelled / (BendMm * 1e-3):F3} "
                + $"bend radii, {focus.Convergence:F1}x");
        }

        var spread = travelled.Max() / travelled.Min();

        output.WriteLine($"a fourfold change of slot moves the focus {spread:F3}x");

        // A lens whose opening quadruples does not leave its focal distance where it was.
        Assert.True(
            spread < 1.05,
            $"the focal distance moved {spread:F3}x over a fourfold slot change, which is "
            + "an element rather than a hole");
    }

    /// <summary>Leaving the drive on costs the focus, and which phase it is at matters.</summary>
    /// <remarks>
    /// <para>
    /// A real C-trap switches its RF off to eject - the paper ramps it down over 100 to
    /// 200 ns before pulsing. With it left running the packet still converges, and it
    /// converges <b>an order of magnitude less well</b>: 45.9x quiet against 2.6 to 4.1x at
    /// four phases spread across one cycle. So what leaving the drive on costs is the focus
    /// itself, not principally where the focus is.
    /// </para>
    /// <para>
    /// <b>An earlier version of this test concluded the opposite about the mechanism, and
    /// the corrected geometry withdraws it.</b> On the beaded rods the drive moved the
    /// focal distance 3.14x while a whole cycle of phase moved it 1.10x, and the reading
    /// was that the packet is steered by the cycle average - the pseudopotential - with the
    /// phase washing out over the seventeen RF periods it crosses. On swept hyperbolic rods
    /// the two are the same size: the drive shifts the focus 1.26x and the phase spreads it
    /// 1.25x. An effect equal to the effect of the drive itself is not a residue of one
    /// partial cycle, so the instantaneous field is doing as much as its average and
    /// <b>no single number describes a driven ejection</b>.
    /// </para>
    /// <para>
    /// That is the assertion here, and it is the negation of what was asserted before. What
    /// survives from the old test is its reason for sweeping at all: one ejection with the
    /// drive running is a single sample of something periodic, and this project has already
    /// recorded once what comes of quoting one - an isolation-efficiency curve whose shape
    /// reversed at an amplitude nobody had swept. Had this been quoted at phase zero alone
    /// it would have read as a modest 1.26x shift with the mechanism unchanged.
    /// </para>
    /// </remarks>
    [Fact]
    public void LeavingTheDriveOnCostsTheFocusAndThePhaseMattersAsMuch()
    {
        double[] phases = [0.0, 0.25, 0.5, 0.75];

        var quiet = MeasureFocus(20.0, rfVolts: 0.0, phase: 0.0, trace: false);

        output.WriteLine(
            $"  drive off       : {quiet.Convergence,6:F1}x at "
            + $"{quiet.Travelled * 1e3,7:F2} mm");

        var travelled = new List<double>();
        var convergence = new List<double>();

        foreach (var phase in phases)
        {
            var driven = MeasureFocus(20.0, rfVolts: 500.0, phase: phase, trace: false);

            travelled.Add(driven.Travelled);
            convergence.Add(driven.Convergence);

            output.WriteLine(
                $"  drive on, {phase:F2} ht: {driven.Convergence,6:F1}x at "
                + $"{driven.Travelled * 1e3,7:F2} mm");
        }

        var phaseSpread = travelled.Max() / travelled.Min();
        var driveShift = quiet.Travelled / travelled.Max();

        output.WriteLine(
            $"the drive moves the focus {driveShift:F2}x; over a whole RF cycle the phase "
            + $"moves it {phaseSpread:F2}x - "
            + $"effects of {driveShift - 1.0:F2} against {phaseSpread - 1.0:F2}");

        output.WriteLine(
            $"the drive costs {quiet.Convergence / convergence.Max():F1}x of convergence at "
            + $"its best phase and {quiet.Convergence / convergence.Min():F1}x at its worst");

        // What the drive costs is the focus. Every phase, so this is not a phase that
        // happens to be unlucky.
        Assert.True(
            convergence.Max() < quiet.Convergence / 5.0,
            $"the best driven ejection converged {convergence.Max():F1}x against "
            + $"{quiet.Convergence:F1}x with the drive off, so there is a phase at which "
            + "leaving the drive running costs little");

        // And the phase is not a detail. Compared as EXCESS OVER ONE, not as the ratios
        // themselves: a ratio that says "no variation" is 1 rather than 0, so the size of
        // an effect measured as a ratio is its distance from 1.
        Assert.True(
            phaseSpread - 1.0 > (driveShift - 1.0) / 4.0,
            $"the focal distance moved {phaseSpread:F2}x across one RF cycle against the "
            + $"drive's own {driveShift:F2}x. A phase spread that small would mean the "
            + "cycle average is what steers the packet, which is what the beaded geometry "
            + "reported and what this geometry does not");

        // The drive does move the focus as well, so the two effects being comparable is
        // not both of them being nothing.
        Assert.True(
            driveShift > 1.1,
            $"the quiet ejection focused at {quiet.Travelled * 1e3:F2} mm and the driven "
            + $"one at {travelled.Max() * 1e3:F2} mm, {driveShift:F2}x apart");
    }

    /// <summary>What the C-trap hands an orbital analyser, in the analyser's own currency.</summary>
    /// <remarks>
    /// <para>
    /// <b>The two instruments cannot be composed into one document</b> — an exact analytic
    /// field fills all space, and the quadro-logarithmic potential grows as z squared, so
    /// an orbital trap declared beside its C-trap puts an enormous field across it. SPEC.md
    /// Amendment 32. What can be done is the handover: measure what one delivers, measure
    /// what the other needs, and compare them in a currency both share.
    /// </para>
    /// <para>
    /// <b>That currency turns out to be time, not space</b>, and the reason is the
    /// analyser's defining property. In a quadro-logarithmic field the axial frequency
    /// depends on nothing but m/q — not on the orbit radius, not on the axial amplitude,
    /// not on the energy — which `QuadroLogarithmicFieldTests` pins directly. So an
    /// analyser is indifferent to almost everything an injected packet varies in. Two ions
    /// at the same frequency still cancel if they start at different <b>phases</b>, and
    /// phase is set by when an ion arrived.
    /// </para>
    /// <para>
    /// So the injection specification is a single ratio: the packet's spread in arrival
    /// time over the analyser's axial period. The image current is the sum of each ion's
    /// oscillation, so its amplitude is the modulus of the mean of exp(i omega t) — 1 for a
    /// packet that arrived together, 0 for one smeared over a whole cycle.
    /// </para>
    /// <para>
    /// Both numbers come from the shipped templates rather than from constants written
    /// here: the spread from ejecting the C-trap, the period from compiling `orbital-trap`
    /// and reading its own declared parameter. That is what makes this a comparison between
    /// two instruments rather than between two of my assumptions.
    /// </para>
    /// <para>
    /// <b>What this does NOT show is that the curvature delivers the coherence.</b> Every
    /// ion sits the same distance from the rods whether the trap is bent or straight, so
    /// they fall through the same potential either way and a straight trap would arrive
    /// just as together. The 60 ns measured here is the <i>slot's</i> doing — ions nearer
    /// its edge see a different fringe than ions at its centre. The curvature buys
    /// something else, measured separately in
    /// <see cref="CurvatureFocusesTheEjectedPacket"/>: a packet 20.8 times narrower in
    /// space, which is about passing an entrance aperture rather than about frequency.
    /// </para>
    /// <para>
    /// That split is worth stating because it says where design effort goes. In this
    /// field the axial frequency is exactly amplitude-independent, so a spatially broad
    /// packet is not a dephased one — ions launched from different axial offsets oscillate
    /// at one frequency and stay in step, and only their amplitudes differ. A real
    /// analyser's field imperfections make the frequency weakly amplitude-dependent and
    /// give spatial compactness a second job; this model has no such imperfection and
    /// should not be read as if it did.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEjectedPacketArrivesInsideTheAnalysersAxialPeriod()
    {
        // The analyser's half, from its own template.
        var analyser = ModelJson.Parse(DeviceTemplates.Read("orbital-trap"));
        var analyserModel = ModelValidator.Validate(analyser).Model!;

        var axialPeriod = analyserModel.Parameters["axialPeriod"].SiValue;

        Assert.True(axialPeriod > 0.0, "the orbital trap declares an axial period");

        // The C-trap's half.
        var quiet = MeasureEjection(bendMm: 20.0, rfVolts: 0.0);
        var driven = MeasureEjection(bendMm: 20.0, rfVolts: 500.0);

        output.WriteLine($"analyser axial period          {axialPeriod * 1e6,9:F4} us");

        foreach (var (name, run) in new[] { ("drive off", quiet), ("drive on ", driven) })
        {
            var phase = 2.0 * Math.PI * run.Spread / axialPeriod;

            // The image current is the sum of each ion's oscillation. For a spread that is
            // small against the period this is close to 1 - (omega sigma)^2 / 2; it is
            // computed rather than approximated because the driven case is not small.
            var real = 0.0;
            var imaginary = 0.0;

            foreach (var t in run.Times)
            {
                real += Math.Cos(2.0 * Math.PI * t / axialPeriod);
                imaginary += Math.Sin(2.0 * Math.PI * t / axialPeriod);
            }

            var coherence = Math.Sqrt((real * real) + (imaginary * imaginary)) / run.Times.Count;

            output.WriteLine(
                $"{name}: arrival spread {run.Spread * 1e9,8:F2} ns "
                + $"= {run.Spread / axialPeriod,7:P2} of a period, "
                + $"phase {phase,6:F3} rad, coherence {coherence,6:F4}");
        }

        // The packet arrives well inside one axial period, so every ion begins its
        // oscillation at essentially the same phase and the image current does not cancel.
        // This is the whole content of "the C-trap can inject this analyser".
        Assert.True(
            quiet.Spread < 0.1 * axialPeriod,
            $"the ejected packet is spread over {quiet.Spread * 1e9:F1} ns against an axial "
            + $"period of {axialPeriod * 1e6:F3} us, which is "
            + $"{quiet.Spread / axialPeriod:P1} of a cycle - the ions would start their "
            + "oscillations at different phases and the image current would cancel");
    }

    /// <summary>An ejection, reported in the currency an analyser cares about.</summary>
    private static (double Spread, List<double> Times) MeasureEjection(double bendMm, double rfVolts)
    {
        const double Spread = 0.04;
        const double EjectVolts = 60.0;

        var offsets = new[] { -Spread, -Spread / 2.0, 0.0, Spread / 2.0, Spread };

        var paths = new List<IReadOnlyList<TrajectorySample>>();

        IElectrostaticField? field = null;

        foreach (var offset in offsets)
        {
            var model = Compile(
                ("bendRadius", Quantity.From(bendMm, "mm")),
                ("ejectVolts", Quantity.From(EjectVolts, "V")),
                ("rfAmplitude", Quantity.From(rfVolts, "V")),
                ("launchVolts", Quantity.From(0.005, "V")),
                ("launchHalfTurns", Quantity.From(0.25 + offset, "1")));

            field ??= FieldAssembly.Build(model);

            paths.Add(Eject(model, field, stepUs: 0.005, maxUs: 24.0));
        }

        // Find the waist the same way the focusing measurement does.
        var last = paths.Min(p => p[^1].TimeSeconds);
        var waist = double.MaxValue;
        var waistTime = 0.0;

        for (var k = 1; k <= 400; k++)
        {
            var t = last * k / 400.0;
            var points = paths.Select(p => At(p, t)).ToList();

            if (points.Any(q => q is null))
            {
                continue;
            }

            var extent = Extent([.. points.Select(q => q!.Value)]);

            if (extent < waist)
            {
                waist = extent;
                waistTime = t;
            }
        }

        var plane = Centroid([.. paths.Select(p => At(p, waistTime)!.Value)]);
        var earlier = Centroid([.. paths.Select(p => At(p, waistTime * 0.9)!.Value)]);
        var heading = plane - earlier;
        var normal = heading * (1.0 / Math.Sqrt(Vec3.Dot(heading, heading)));

        var times = new List<double>();

        foreach (var path in paths)
        {
            for (var k = 1; k < path.Count; k++)
            {
                var before = Vec3.Dot(path[k - 1].Position - plane, normal);
                var after = Vec3.Dot(path[k].Position - plane, normal);

                if (before > 0.0 || after < 0.0)
                {
                    continue;
                }

                var span = after - before;
                var f = Math.Abs(span) < double.Epsilon ? 0.0 : -before / span;

                times.Add(
                    path[k - 1].TimeSeconds
                    + (f * (path[k].TimeSeconds - path[k - 1].TimeSeconds)));

                break;
            }
        }

        return (times.Count < 2 ? double.NaN : times.Max() - times.Min(), times);
    }

    /// <summary>An in-plane launch stays in the plane, exactly.</summary>
    /// <remarks>
    /// The geometry is symmetric about the plane of the arc — the out-of-plane rods are a
    /// mirror pair — so an ion launched in that plane with no velocity out of it has no
    /// force out of it either. Any excursion is an asymmetry in the solved field, which
    /// for a symmetric geometry is a defect rather than physics. A cheap exact check on a
    /// solve with no symmetry of its own to lean on.
    /// </remarks>
    [Fact]
    public void AnInPlaneLaunchStaysInThePlane()
    {
        var (_, path) = Fly(Compile());

        var worst = path.Max(s => Math.Abs(s.Position.Z));

        output.WriteLine($"worst out-of-plane excursion: {worst * 1e9:F3} nm");

        Assert.True(worst < 1e-9, $"the ion left the plane by {worst * 1e6:F3} um");
    }
}
