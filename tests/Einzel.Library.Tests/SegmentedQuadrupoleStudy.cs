using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A quadrupole cut into three axial sections, solved in three dimensions.
/// </summary>
/// <remarks>
/// <para>
/// The first device here that a cross-section cannot express at any resolution.
/// What makes a segmented filter a segmented filter is that the field changes
/// <em>along the axis</em>, and that is precisely the direction a translational
/// solve is invariant in - so this is not a more accurate version of the
/// two-dimensional quadrupole, it is a different instrument.
/// </para>
/// <para>
/// The checks are chosen to be robust at a resolution three dimensions can afford.
/// A 2D solve at sixteen cells across r0 is 128 by 128 nodes; a 3D one is two
/// million, so the mesh here is far coarser than any of the plane studies use, and
/// anything sensitive to field quality is reported rather than asserted.
/// </para>
/// </remarks>
public sealed class SegmentedQuadrupoleStudy(ITestOutputHelper output)
{
    private static ModelDocument Template() =>
        Io.ModelJson.Parse(DeviceTemplates.Read("segmented-quadrupole"));

    private static ModelDocument With(ModelDocument document, params (string Name, double Value)[] overrides)
    {
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        return validation.Model!;
    }

    private static DrivenSolvedField Driven(IElectrostaticField field) =>
        field as DrivenSolvedField ?? (DrivenSolvedField)((SuperposedField)field).Elements[0];

    private static TrajectoryResult Fly(ModelDocument document)
    {
        var model = Compile(document);
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

        return TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-9, MaximumFlightTime = model.MaximumFlightTimeSi },
            detector);
    }

    [Fact]
    public void TwelveRodSegmentsReduceToOneBasisSolve()
    {
        // Three sections of four rods, and one supply behind all of them. The two
        // pairs within a section are exact negatives, and the sections are tapped
        // off the same generator in a fixed ratio at the same phase - so the whole
        // structure is a single spatial pattern with a single weight, and the twelve
        // segments cost one solve.
        //
        // The decomposition that finds this is the same code the plane uses.
        // Nothing about it is dimensional: what makes a channel a channel is how
        // the electrodes are wired, not where they are.
        var model = Compile(Template());
        var driven = Driven(FieldAssembly.Build(model));

        var electrodes = model.Fields[0].Solve3D!.Electrodes;

        output.WriteLine($"{electrodes.Count} rod segments reduced to {driven.ChannelCount} basis solve(s)");
        output.WriteLine($"drive {driven.FrequencyHz / 1e6:F3} MHz");

        Assert.Equal(12, electrodes.Count);
        Assert.Equal(1, driven.ChannelCount);
    }

    [Fact]
    public void ResolvingTheDcSeparatelyCostsASecondSolve()
    {
        // What decides the channel count is proportionality, and this is the case
        // that breaks it. The coupling is a capacitor: it passes the RF and blocks
        // the DC, so with the analysing DC switched on the prefilter sees the drive
        // and not the offset - the two supplies no longer reach the electrodes in
        // the same proportions, and they stop being one pattern.
        //
        // That is also the physics the prefilter exists for. Ions meet a confining
        // field before they meet the analysing one, instead of crossing the DC
        // fringe on the way in.
        var capacitive = Driven(FieldAssembly.Build(Compile(With(Template(), ("mainDc", 40.0)))));

        // A resistive tap instead, passing the DC in the same ratio as the RF, puts
        // everything back into one pattern - the proportions match again.
        var resistive = Driven(FieldAssembly.Build(Compile(With(
            Template(),
            ("mainDc", 40.0),
            ("prefilterDcRatio", 0.85)))));

        output.WriteLine($"capacitive coupling (DC blocked)  {capacitive.ChannelCount} solve(s)");
        output.WriteLine($"resistive tap (DC in proportion)  {resistive.ChannelCount} solve(s)");

        Assert.Equal(2, capacitive.ChannelCount);
        Assert.Equal(1, resistive.ChannelCount);
    }

    [Fact]
    public void ThePrefilterSitsAtItsOwnWorkingPoint()
    {
        // The defining property, measured. A segmented filter is one whose sections
        // see different amplitudes, and the coupling ratio is what sets the
        // difference - so the transverse field near the axis in the prefilter must
        // be that fraction of the field in the main section.
        //
        // Measured from the field rather than from the applied voltages, which is
        // the point: the applied voltages are what the document says, and this is
        // what the solve produced from them.
        var model = Compile(Template());
        var field = FieldAssembly.Build(model);

        var coupling = model.Parameters["couplingRatio"].In("1");
        var probe = 0.5 * model.Parameters["inscribedRadius"].In("m");

        // Mid-section on each side, well away from the joins.
        var preZ = 0.5 * model.Parameters["preEnd"].In("m");

        var mainZ = 0.5 * (model.Parameters["mainStart"].In("m") + model.Parameters["mainEnd"].In("m"));

        var pre = new Vec3(probe, 0.0, preZ);
        var main = new Vec3(probe, 0.0, mainZ);

        var preField = Math.Abs(field.ElectricFieldAt(in pre).X);
        var mainField = Math.Abs(field.ElectricFieldAt(in main).X);

        var ratio = preField / mainField;

        output.WriteLine($"transverse field at r0/2, at t = 0:");
        output.WriteLine($"  prefilter    {preField / 1e3,10:F3} kV/m  (z = {preZ * 1e3:F1} mm)");
        output.WriteLine($"  main section {mainField / 1e3,10:F3} kV/m  (z = {mainZ * 1e3:F1} mm)");
        output.WriteLine($"  ratio        {ratio:F4} against a declared coupling of {coupling:F4}");

        // A few per cent, because each section is short enough that its middle is
        // not entirely free of its own ends - which is a real property of a 22 mm
        // section at r0 = 4 mm and not a numerical one.
        Assert.InRange(ratio, coupling * 0.9, coupling * 1.1);
    }

    [Fact]
    public void AnIonTraversesAllThreeSections()
    {
        // End to end: a three-dimensional driven solve, an ion tracked through it in
        // the time domain, and rods that stop it if it strays. Nothing in this
        // sentence was possible before the last two commits.
        var document = With(Template(), ("mainAmplitude", 300.0));
        var result = Fly(document);

        var model = Compile(document);
        var detector = model.DetectorPoint.Z;

        output.WriteLine($"outcome    {result.Outcome}"
            + $"{(result.StruckSurface is null ? string.Empty : " on " + result.StruckSurface)}");
        output.WriteLine($"flight     {result.FlightTimeSeconds * 1e6:F2} us in {result.AcceptedSteps} steps");
        output.WriteLine($"arrived at z = {result.FinalState.Position.Z * 1e3:F2} mm "
            + $"of a detector at {detector * 1e3:F2} mm");
        output.WriteLine($"radially   {Math.Sqrt(
            (result.FinalState.Position.X * result.FinalState.Position.X)
            + (result.FinalState.Position.Y * result.FinalState.Position.Y)) * 1e3:F3} mm off axis");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
    }

    [Fact]
    public void TheFieldConvergesWithTheMesh()
    {
        // The check the transmission boundary rests on. Three dimensions cost the
        // cube of the resolution, so the question is not whether a finer mesh would
        // be better but whether this one is close enough for the number being
        // quoted - and that is answered by refining and watching, not by asserting
        // it in a comment.
        //
        // Labelled by the mesh that came out, not by the parameter that was asked
        // for. OverBox rounds each axis up to a power of two independently, so
        // cellsPerRadius 4, 5 and 8 give three different grids and none of them has
        // the requested number of cells across r0 - the shipped 5 is 8.5 across,
        // which a study labelled by its request would have reported as 5.
        //
        // Measured on the field rather than on a trajectory, because a solve is
        // seconds and a flight is tens of them, and it is the field that was in
        // doubt.
        // Two probes, because one axis at a time is what actually refines here.
        // Mid-section is a transverse question and is blind to the axial mesh; the
        // segment gap is the opposite, and it is the feature this template exists
        // for - the whole point of a segmented filter is that the working point
        // changes across a 1 mm join.
        output.WriteLine("asked   grid            cells/r0   mid-section       gap");

        var previousMid = 0.0;
        var previousGap = 0.0;
        var worstMid = 0.0;
        var worstGap = 0.0;

        foreach (var cells in new[] { 4.0, 5.0, 8.0 })
        {
            var model = Compile(With(Template(), ("cellsPerRadius", cells)));
            var field = FieldAssembly.Build(model);

            var r0 = model.Parameters["inscribedRadius"].In("m");
            var probe = 0.5 * r0;

            var mainZ = 0.5
                * (model.Parameters["mainStart"].In("m") + model.Parameters["mainEnd"].In("m"));

            // Halfway across the prefilter-to-main gap, where the field is changing
            // from one section's amplitude to the other's.
            var gapZ = 0.5 * (model.Parameters["preEnd"].In("m") + model.Parameters["mainStart"].In("m"));

            var midPoint = new Vec3(probe, 0.0, mainZ);
            var gapPoint = new Vec3(probe, 0.0, gapZ);

            var mid = Math.Abs(field.ElectricFieldAt(in midPoint).X);
            var gap = Math.Abs(field.ElectricFieldAt(in gapPoint).X);

            var solve = model.Fields[0].Solve3D!;

            var grid = Grid3D.OverBox(
                solve.MinX, solve.MinY, solve.MinZ, solve.MaxX, solve.MaxY, solve.MaxZ, solve.CellSize);

            var midChange = previousMid > 0.0 ? Math.Abs(mid - previousMid) / previousMid : double.NaN;
            var gapChange = previousGap > 0.0 ? Math.Abs(gap - previousGap) / previousGap : double.NaN;

            if (!double.IsNaN(midChange))
            {
                worstMid = Math.Max(worstMid, midChange);
                worstGap = Math.Max(worstGap, gapChange);
            }

            static string Pct(double v) => double.IsNaN(v) ? string.Empty : v.ToString("P3");

            output.WriteLine(
                $"{cells,5:F0}   {grid.CountX}x{grid.CountY}x{grid.CountZ,-8}  {r0 / grid.SpacingX,7:F2}"
                + $"   {mid / 1e3,7:F2} kV/m {Pct(midChange),8}"
                + $"   {gap / 1e3,7:F2} kV/m {Pct(gapChange),8}");

            previousMid = mid;
            previousGap = gap;
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"mid-section: worst step-to-step change {worstMid:P3} - converged.");
        output.WriteLine($"segment gap: worst step-to-step change {worstGap:P3} - NOT converged, and");
        output.WriteLine("still moving at the finest mesh tested. No claim on this page rests on the");
        output.WriteLine("field inside a gap; the ion is lost mid-section, where the field is settled.");

        Assert.True(previousMid > 0.0 && previousGap > 0.0);

        // Asserted, not narrated. The first version of this test computed the
        // change, printed a conclusion about it, and asserted only that the field
        // was non-zero - so a regression that moved the field by 30% would have
        // passed while still printing "a hundredth of a per cent", and this page's
        // documentation quotes that line.
        // The mid-section is converged, and that is what the transmission boundary
        // below rests on - the ion is lost at z = 38.7 mm, in the middle of a 24 mm
        // section, nowhere near a join.
        Assert.True(
            worstMid < 5e-3,
            $"the mid-section transverse field moved {worstMid:P3} under refinement; at that size the "
            + "transmission boundary below is a property of the grid rather than of the geometry");

        // The gap is not, and this asserts the bound the measurement supports rather
        // than the one that would be convenient. At 1 mm it is one to two cells
        // across, and a point probe in a steep axial gradient is the most
        // mesh-sensitive thing this geometry has. So: nothing here claims what the
        // gaps *do*. That needs a mesh this template cannot afford, or a measure
        // integrated along a trajectory rather than sampled at a point.
        Assert.True(
            worstGap < 5e-2,
            $"the field across a segment gap moved {worstGap:P3} under refinement, which is past even "
            + "the loose bound this quantity is held to; the gaps have stopped being represented at all");
    }

    [Fact]
    public void TheCutOffBracketsTheIdealMathieuBoundary()
    {
        // The filter filters, and it filters in the right place. The low-mass
        // cut-off of the ideal Mathieu equation is q = 0.90804, and this geometry -
        // round rods, cut into three sections, with gaps and end fringes, solved on
        // a mesh three dimensions can afford - transmits at 0.855 and loses the ion
        // at 0.910.
        //
        // That is not where it started. Before the coarse levels were made
        // node-aligned, the ion was lost at q = 0.611, and the first explanation
        // written down for that was field quality. It was not: refining the mesh
        // moves the field by a hundredth of a per cent. It was an under-converged
        // solve, and fixing the multigrid moved the boundary from 0.611 to the
        // right answer. A wrong number with a plausible explanation attached is the
        // expensive kind.
        const double IdealCutOff = 0.90804;

        output.WriteLine("amplitude / V     q      outcome");

        double? lastThrough = null;
        double? firstLost = null;
        string? struckIn = null;
        var lostAt = 0.0;

        foreach (var amplitude in new[] { 300.0, 700.0, 745.0 })
        {
            var document = With(Template(), ("mainAmplitude", amplitude));
            var model = Compile(document);

            var species = IonSpecies.FromModel(model);
            var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].In("Hz");
            var r0 = model.Parameters["inscribedRadius"].In("m");

            var q = 4.0 * Math.Abs(species.ChargeSi) * amplitude
                / (species.MassSi * omega * omega * r0 * r0);

            var result = Fly(document);
            var through = result.Outcome == TrajectoryOutcome.StopConditionMet;

            if (through)
            {
                lastThrough = q;
            }
            else if (firstLost is null)
            {
                firstLost = q;
                struckIn = result.StruckSurface;
                lostAt = result.FinalState.Position.Z * 1e3;
            }

            output.WriteLine(
                $"{amplitude,13:F0}   {q,5:F3}   {(through ? "through" : "lost")}"
                + $"{(result.StruckSurface is null ? string.Empty : " on " + result.StruckSurface)}"
                + $"{(through ? string.Empty : $" at z = {result.FinalState.Position.Z * 1e3:F1} mm")}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"transmits to q = {lastThrough:F3}, lost by q = {firstLost:F3}");
        output.WriteLine($"the ideal Mathieu cut-off is q = {IdealCutOff:F5}, which that brackets");

        Assert.NotNull(lastThrough);
        Assert.NotNull(firstLost);

        Assert.True(
            lastThrough < IdealCutOff && firstLost > IdealCutOff,
            $"the boundary between {lastThrough:F3} and {firstLost:F3} does not bracket {IdealCutOff:F5}");

        // And it is lost in the *main* section, not the prefilter. That is the
        // segmentation doing its job: the entrance sits at 85% of the main
        // amplitude, so its q is 0.85 of the main one and it stays stable while the
        // analysing section ejects. A filter that lost ions in its prefilter would
        // be a filter with an expensive decoration on the front.
        var mainStart = Compile(Template()).Parameters["mainStart"].In("mm");
        var mainEnd = Compile(Template()).Parameters["mainEnd"].In("mm");

        output.WriteLine(
            $"lost on {struckIn} at z = {lostAt:F1} mm, inside the main section "
            + $"({mainStart:F0} to {mainEnd:F0} mm)");

        // Asserted before the prefix, because StruckSurface is null unless a
        // surface was actually struck. A loss by flight-time ceiling or by leaving
        // the domain would otherwise fail with a complaint about "main" instead of
        // saying that the ion was never absorbed at all.
        Assert.NotNull(struckIn);
        Assert.StartsWith("main", struckIn, StringComparison.Ordinal);
        Assert.InRange(lostAt, mainStart, mainEnd);
    }
}
