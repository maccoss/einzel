using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A stacked-ring funnel: the device SYM-1 is argued from.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 22 calls cylindrical symmetry load-bearing for funnels, and SYM-1
/// says why in one line: "A 200-ring funnel driven in two RF phases needs two RF
/// basis fields plus a DC gradient, not 200 basis solutions." This is that device,
/// and the first check here is that claim, measured.
/// </para>
/// <para>
/// It also needed the format to be able to say the same thing once. A stack of
/// rings written out ring by ring is not a template - nobody can read it and a
/// sweep cannot perturb it - so an electrode may declare a repeat, binding an index
/// its expressions name.
/// </para>
/// <para>
/// What is not here is the gas. A real funnel runs at around a millibar and the
/// collisions are half the mechanism: they damp the radial motion so ions settle
/// onto the axis instead of ringing about it. Everything below is the field and the
/// confinement without the cooling, and the acceptance it measures is a lower bound
/// on the real one for that reason.
/// </para>
/// </remarks>
public sealed class IonFunnelStudy(ITestOutputHelper output)
{
    private static ModelDocument Template() => Io.ModelJson.Parse(DeviceTemplates.Read("ion-funnel"));

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

    /// <summary>Flies one ion in and reports where it ended.</summary>
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
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector);
    }

    [Fact]
    public void TheSolveCountDoesNotGrowWithTheRingCount()
    {
        // The claim SYM-1 makes, measured. What makes a supply one supply is that
        // its electrodes move together, not that they move to the same place - so a
        // resistor chain holding twenty-four different voltages is still one solve.
        output.WriteLine("rings    electrodes    basis solves");

        var counts = new List<int>();

        foreach (var rings in new[] { 8.0, 24.0, 48.0 })
        {
            var model = Compile(With(Template(), ("ringCount", rings)));
            var driven = Driven(FieldAssembly.Build(model));

            var electrodes = model.Fields[0].Solve!.Electrodes.Count;
            counts.Add(driven.ChannelCount);

            output.WriteLine($"{rings,5:F0}    {electrodes,10}    {driven.ChannelCount,12}");

            Assert.Equal((int)rings, electrodes);
        }

        output.WriteLine(string.Empty);
        output.WriteLine(
            "two, not three: the two RF phases are exact negatives of one another, so they are one");
        output.WriteLine(
            "spatial pattern with one weight. Three phases that were not negatives would be three.");

        // Flat. Six times the rings for the same cost is the whole argument.
        Assert.All(counts, c => Assert.Equal(counts[0], c));
        Assert.True(counts[0] <= 3, $"the stack reduced to {counts[0]} solves, which is not a small number");
    }

    [Fact]
    public void TheRepeatBindsAnIndexEveryExpressionCanSee()
    {
        // One ring written once becomes a stack, and every placement stays an
        // expression - so the geometry still moves when a parameter does, which is
        // what the whole parametric format rests on.
        var model = Compile(Template());
        var electrodes = model.Fields[0].Solve!.Electrodes;

        var pitch = model.Parameters["ringPitch"].In("m");
        var entrance = model.Parameters["entranceRadius"].In("m");
        var exit = model.Parameters["exitRadius"].In("m");

        output.WriteLine("ring        x / mm    aperture / mm    DC / V    RF / V");

        foreach (var k in new[] { 0, 1, 2, 23 })
        {
            var ring = electrodes[k];

            output.WriteLine(
                $"{ring.Name,-8}  {ring.MinX * 1e3,8:F2}    {ring.MinY * 1e3,13:F3}    "
                + $"{ring.Potential,6:F2}    {ring.DriveAmplitude,6:F1}");
        }

        // Named by position, so a loss itemisation says which ring.
        Assert.Equal("ring-0", electrodes[0].Name);
        Assert.Equal("ring-23", electrodes[^1].Name);

        // Placed by the index.
        Assert.Equal(0.0, electrodes[0].MinX, 12);
        Assert.Equal(pitch, electrodes[1].MinX, 12);

        // Tapered by it, from the entrance aperture to the exit one.
        Assert.Equal(entrance, electrodes[0].MinY, 12);
        Assert.Equal(exit, electrodes[^1].MinY, 12);

        // And driven in antiphase by it: mod(ring, 2) alternates the sign.
        Assert.True(electrodes[0].DriveAmplitude > 0.0);
        Assert.True(electrodes[1].DriveAmplitude < 0.0);
        Assert.Equal(electrodes[0].DriveAmplitude, -electrodes[1].DriveAmplitude, 9);

        // The DC chain starts at the grounded entrance and falls to the exit.
        Assert.Equal(0.0, electrodes[0].Potential, 12);
        Assert.True(electrodes[^1].Potential < electrodes[0].Potential);
    }

    [Fact]
    public void ItFunnels()
    {
        // An ion entering half way out to the wall arrives at the exit, and arrives
        // near the axis. That is the whole job: take a plume much wider than the
        // exit aperture and deliver a beam that fits through it.
        var result = Fly(Template());

        var model = Compile(Template());
        var exitRadius = model.Parameters["exitRadius"].In("m");
        var launchRadius = model.Parameters["launchRadius"].In("m");

        var landed = Math.Sqrt(
            (result.FinalState.Position.Y * result.FinalState.Position.Y)
            + (result.FinalState.Position.Z * result.FinalState.Position.Z));

        output.WriteLine($"entered at {launchRadius * 1e3:F2} mm from the axis");
        output.WriteLine($"outcome    {result.Outcome}{(result.StruckSurface is null ? "" : " on " + result.StruckSurface)}");
        output.WriteLine($"left at    {landed * 1e3:F3} mm, against an exit aperture of {exitRadius * 1e3:F2} mm");
        output.WriteLine($"flight     {result.FlightTimeSeconds * 1e6:F2} us in {result.AcceptedSteps} steps");
        output.WriteLine($"compression {launchRadius / landed:F1}x");

        // Arriving *is* the aperture test, now that rings are solid metal. The ion
        // passed inside every aperture in the stack, including the last one at
        // 1.5 mm, or it would have ended on a ring instead of at the detector.
        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        // The detector sits a pitch beyond the last ring, so the radius measured
        // there is larger than the radius the ion actually threaded - it diverges
        // once the confinement stops. Compression is the honest quantity, and it is
        // a lower bound for the same reason.
        output.WriteLine(string.Empty);
        output.WriteLine(
            $"it threaded the {exitRadius * 1e3:F2} mm exit aperture, so the compression through the "
            + $"stack was at least {launchRadius / exitRadius:F1}x; the {landed * 1e3:F3} mm above is "
            + "measured a pitch further on, after it has begun to diverge again");

        Assert.True(
            landed < 0.5 * launchRadius,
            $"the ion left at {landed * 1e3:F3} mm having entered at {launchRadius * 1e3:F2} mm, "
            + "which is not compression");
    }

    [Fact]
    public void ItIsTheRfThatConfines()
    {
        // The control that makes the previous test mean something. Switch the drive
        // off and only the DC gradient is left, which pushes the ion forward and
        // does nothing at all to keep it off the rings. The ion should be lost, and
        // lost on a named ring.
        var withoutRf = Fly(With(Template(), ("rfAmplitude", 0.0)));

        output.WriteLine($"with no RF: {withoutRf.Outcome}"
            + $"{(withoutRf.StruckSurface is null ? "" : " on " + withoutRf.StruckSurface)}");

        Assert.NotEqual(TrajectoryOutcome.StopConditionMet, withoutRf.Outcome);

        if (withoutRf.StruckSurface is { } struck)
        {
            Assert.StartsWith("ring-", struck, StringComparison.Ordinal);
            output.WriteLine($"the ion is lost on {struck}, which the taper walks into its path");
        }
    }

    [Fact]
    public void AcceptanceFallsOffWithEntryRadius()
    {
        // What a funnel is specified by. Close to the axis everything gets through;
        // far enough out the pseudopotential cannot turn the ion before a ring
        // does, and there is a radius in between where it stops working.
        output.WriteLine("entry / mm    outcome");

        var accepted = new List<double>();

        foreach (var radius in new[] { 1.0, 3.0, 6.0, 9.0, 11.0 })
        {
            var result = Fly(With(Template(), ("launchRadius", radius)));
            var through = result.Outcome == TrajectoryOutcome.StopConditionMet;

            if (through)
            {
                accepted.Add(radius);
            }

            output.WriteLine(
                $"{radius,10:F1}    {(through ? "through" : "lost")}"
                + $"{(result.StruckSurface is null ? "" : " on " + result.StruckSurface)}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("no gas here, so this is the acceptance without collisional damping,");
        output.WriteLine("which a real funnel has and which only helps.");

        // The near-axis ion has to get through, or nothing about the device works.
        Assert.Contains(1.0, accepted);

        // And something has to be lost, or the aperture is not being tested at all.
        Assert.True(accepted.Count < 5, "every entry radius got through, so this is not measuring acceptance");
    }
}
