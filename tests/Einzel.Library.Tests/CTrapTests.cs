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

    /// <summary>The rods are continuous: the beads overlap rather than leaving gaps.</summary>
    /// <remarks>
    /// A chain of spheres is a rod only if consecutive beads intersect. Spaced further
    /// apart than their radius it is a string of pearls, and the field reaches through the
    /// gaps — which would look like a working trap with a mysteriously poor acceptance.
    /// Checked as arithmetic on the declared geometry rather than by sampling the field,
    /// because the condition is geometric and exact.
    /// </remarks>
    [Fact]
    public void TheBeadsOverlapAlongEachRod()
    {
        var model = Compile();

        var electrodes = model.Fields[0].Solve3D!.Electrodes;

        // Beads of one rod, in order: every repeat of one declaration shares a name stem.
        var byRod = electrodes
            .GroupBy(e => new string([.. e.Name.TakeWhile(c => !char.IsDigit(c) && c != '-')]))
            .ToList();

        Assert.True(byRod.Count >= 4, $"expected four rods, found {byRod.Count} groups");

        var worst = 0.0;

        foreach (var rod in byRod)
        {
            var beads = rod.ToList();

            for (var k = 1; k < beads.Count; k++)
            {
                var step = Math.Sqrt(
                    Math.Pow(beads[k].CentreX - beads[k - 1].CentreX, 2)
                    + Math.Pow(beads[k].CentreY - beads[k - 1].CentreY, 2)
                    + Math.Pow(beads[k].CentreZ - beads[k - 1].CentreZ, 2));

                var radius = beads[k].Radius;

                worst = Math.Max(worst, step / radius);
            }
        }

        output.WriteLine(
            $"{byRod.Count} rods, worst bead spacing {worst:F3} of a rod radius");

        // Under 2 is contact; comfortably under is a smooth rod. Above 2 the beads do not
        // touch at all.
        Assert.True(
            worst < 1.5,
            $"beads are {worst:F2} radii apart, so the rod has gaps in it");
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
    private FocusResult MeasureFocus(double bendMm, double rfVolts, double phase, bool trace)
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

    /// <summary>A curved trap ejects a converging packet; a straight one cannot.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is what the curvature is for.</b> Every ion in a curved trap is pushed out
    /// along its own radius, so their velocities all point inward and the packet converges
    /// as it flies — it arrives at the analyser spatially focused rather than as a line. A
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
    /// <b>The focus is not at the arc centre, which is the part a design has to know.</b>
    /// Aiming every velocity along a radius would put it there, one bend radius away; it is
    /// measured at 1.73 and 1.92 bend radii, so the packet crosses the centre still
    /// converging and reaches its waist well beyond. The slot is a lens as well as a hole —
    /// the ion is accelerated up to it and drifts field-free after it, which is an aperture
    /// lens by construction. What is NOT claimed is a strength for it: a thin-lens fit to
    /// the shorter bend predicts 46.0 mm for the longer one against a measured 38.4, so the
    /// two are not one fixed lens and one variable one. The slot's own opening scales with
    /// the bend as well, since it is declared as an angle.
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

        output.WriteLine(
            $"waist {focus.Waist * 1e3:F4} mm, {focus.Travelled * 1e3:F3} mm from launch "
            + $"= {focus.Travelled / (bendMm * 1e-3):F3} bend radii, "
            + $"{focus.WaistRadius * 1e3:F3} mm from the arc centre");

        output.WriteLine(
            $"launch extent / waist = {focus.Convergence:F1}x; a straight trap would be 1.0x");

        // It focuses at all. A parallel ejection gives exactly 1.0 here whatever the field
        // does, so anything well above 1 is the curvature.
        Assert.True(
            focus.Convergence > 5.0,
            $"the packet went from {focus.LaunchExtent * 1e3:F3} mm at launch to "
            + $"{focus.Waist * 1e3:F3} mm at its narrowest, a factor of "
            + $"{focus.Convergence:F2}. That is not a focus");

        // And the curvature is what sets the distance: the focus lands within a factor of
        // two of one bend radius at both radii, which a fixed-length lens would not do.
        var inRadii = focus.Travelled / (bendMm * 1e-3);

        Assert.InRange(inRadii, 1.2, 2.5);
    }

    /// <summary>Leaving the drive on refocuses the ejection, and it does it by cycle average.</summary>
    /// <remarks>
    /// <para>
    /// A real C-trap switches its RF off to eject. With it left running the packet still
    /// converges, but it converges <b>three times sooner and two and a half times less
    /// well</b> — so an analyser placed where the quiet ejection focuses would be in
    /// entirely the wrong place, and one placed at the driven focus would receive a poorer
    /// packet. Whether the drive is on at the instant of ejection is a design decision
    /// about where the analyser goes, not a detail of the hold.
    /// </para>
    /// <para>
    /// <b>The phase sweep is the half that says what mechanism it is</b>, and it refuted
    /// the guess that prompted it. An ejection into a field reversing at three megahertz
    /// looks like it should depend on where in the cycle the push arrived — every ion in
    /// the packet sees the same phase, so a kick would aim the whole packet somewhere
    /// different. It does not: over a whole cycle the focal distance moves by about a
    /// tenth, against the threefold shift the drive itself causes. So what acts on the
    /// packet is the <b>cycle-averaged</b> force — the pseudopotential — and not the
    /// instantaneous field. The ion crosses about seventeen RF periods on its way to the
    /// waist, which is why the phase it started at washes out, and the tenth that remains
    /// is the one partial cycle at the beginning.
    /// </para>
    /// <para>
    /// Sweeping it at all is the point: one ejection with the drive running is a single
    /// sample of something periodic, and this project has already recorded once what comes
    /// of quoting one — an isolation-efficiency curve whose shape reversed at an amplitude
    /// nobody had swept.
    /// </para>
    /// </remarks>
    [Fact]
    public void LeavingTheDriveOnMovesTheFocusThroughItsCycleAverage()
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

        // The drive matters a great deal to WHERE the packet focuses.
        Assert.True(
            driveShift > 2.0,
            $"the quiet ejection focused at {quiet.Travelled * 1e3:F2} mm and the driven "
            + $"one at {travelled.Max() * 1e3:F2} mm, only {driveShift:F2}x apart - so "
            + "leaving the drive running would not change where an analyser goes");

        // And it does it through the cycle average, not through the phase. This is the
        // discriminating half: a kick would put the phase spread at the same scale as the
        // drive shift, and instead it is an order smaller.
        // Compared as EXCESS OVER ONE, not as the ratios themselves. A ratio that says
        // "no variation" is 1 rather than 0, so the size of an effect measured as a ratio
        // is its distance from 1 - and a first version of this assertion compared 1.10
        // against 3.14/4, which no phase spread could ever satisfy however flat it was.
        Assert.True(
            phaseSpread - 1.0 < (driveShift - 1.0) / 4.0,
            $"the focal distance moved {phaseSpread:F2}x across one RF cycle against the "
            + $"drive's own {driveShift:F2}x. Those are comparable, so the packet is being "
            + "kicked by the instantaneous field rather than steered by its cycle average, "
            + "and no single number describes a driven ejection");

        // Every phase is worse than switching off, which is the reason to switch off
        // rather than to pick a phase.
        Assert.True(
            convergence.Max() < quiet.Convergence,
            $"the best driven ejection converged {convergence.Max():F1}x against "
            + $"{quiet.Convergence:F1}x with the drive off, so there is a phase at which "
            + "leaving the drive running costs nothing");
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
