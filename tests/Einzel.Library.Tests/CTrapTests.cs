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
