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
    public void RaisingTheDriveEventuallyEjectsTheIon()
    {
        // The filter filters. Reported rather than pinned to a number: at five cells
        // across r0 the field quality is nothing like the plane studies use, so
        // where the boundary sits is a property of this mesh as much as of the
        // geometry, and quoting it as a stability limit would be quoting the mesh.
        output.WriteLine("amplitude / V     q      outcome");

        var transmitted = new List<double>();

        foreach (var amplitude in new[] { 150.0, 300.0, 500.0 })
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
                transmitted.Add(amplitude);
            }

            output.WriteLine(
                $"{amplitude,13:F0}   {q,5:F3}   {(through ? "through" : "lost")}"
                + $"{(result.StruckSurface is null ? string.Empty : " on " + result.StruckSurface)}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("the ideal hyperbolic cut-off is q = 0.908; this mesh puts the boundary well");
        output.WriteLine("below it, which is field quality rather than physics until it is refined.");

        Assert.Contains(150.0, transmitted);
        Assert.DoesNotContain(500.0, transmitted);
    }
}
