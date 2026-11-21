using Einzel.Commands;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A field's discontinuity members are functions of position alone: asking the field anything at
/// any instant leaves every member's measure at a fixed point where it was.
/// </summary>
/// <remarks>
/// <para>
/// The integrator reads every member at a step's end and uses the readings as the next step's start
/// without asking again (<see cref="IElectrostaticField.DiscontinuityMembers"/>); it reads afresh only
/// after a landed sequencer switch, a collision or an analytic drift. A member whose measure moved
/// with the instant - a driven or sequenced composite caching the state it was last asked about -
/// would be read stale between those events. The member interface takes no time, so the only way a
/// measure can depend on one is hidden state: that is what this walks the shipped models for.
/// </para>
/// <para>
/// Every template and example whose fields are cross-sections or analytic is built and asked; a
/// volume solve is left out for its cost, and it reaches the integrator through the same composites
/// - the time-varying sum, the sequenced field, the time shift - which the cross-sections exercise.
/// </para>
/// </remarks>
public sealed class MemberTimeInvarianceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _assets = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_assets))
        {
            Directory.Delete(_assets, recursive: true);
        }
    }

    [Fact]
    public void NoShippedFieldsMembersMoveWithTheInstant()
    {
        var asked = 0;
        var timeVarying = 0;
        var moved = new List<string>();

        foreach (var name in DeviceTemplates.Names())
        {
            Ask($"template {name}", DeviceTemplates.Read(name), null, ref asked, ref timeVarying, moved);
        }

        foreach (var name in ExampleModels.Names)
        {
            var directory = Path.Combine(_assets, name);
            Directory.CreateDirectory(directory);
            ExampleModels.WriteAssets(name, directory);
            Ask($"example {name}", ExampleModels.Read(name), directory, ref asked, ref timeVarying, moved);
        }

        output.WriteLine($"{asked} fields asked, {timeVarying} of them time-varying");

        // A walk that asked nothing would pass; the corpus has dozens of fields, and the driven and
        // sequenced ones are the point.
        Assert.True(asked > 30, $"only {asked} fields were asked");
        Assert.True(timeVarying > 10, $"only {timeVarying} time-varying fields were asked");
        Assert.Empty(moved);
    }

    private static void Ask(string label, string json, string? directory, ref int asked, ref int timeVarying, List<string> moved)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null, directory);

        Assert.True(validation.IsValid, $"{label}: {(validation.IsValid ? string.Empty : validation.Errors[0].Constraint)}");

        var model = validation.Model!;

        if (model.Fields.Any(f => f.Solve3D is not null))
        {
            return;
        }

        var field = FieldAssembly.BuildReported(model).Field;
        asked++;

        var points = Points(model).ToList();
        var count = field.DiscontinuityMembers;
        var before = points.Select(p => Read(field, p, count)).ToList();

        // Ask the field everything a flight asks, at instants a flight reaches: through a period, past
        // the first switch, far on.
        if (field is ITimeVaryingField driven)
        {
            timeVarying++;

            var period = double.IsFinite(driven.ShortestPeriodSeconds) ? driven.ShortestPeriodSeconds : 1e-6;
            var instants = new List<double> { 0.37 * period, 2.71 * period, 1e-3 };
            var switchAt = driven.NextSwitchAfter(0.0);

            if (double.IsFinite(switchAt))
            {
                instants.Add(switchAt * (1.0 + 1e-9));
                instants.Add(driven.NextSwitchAfter(switchAt * (1.0 + 1e-9)) is var next && double.IsFinite(next) ? next * 1.000001 : switchAt * 3.0);
            }

            foreach (var t in instants)
            {
                foreach (var p in points)
                {
                    var at = p;
                    Touch(() => driven.ElectricFieldAt(in at, t));
                    Touch(() => driven.PotentialAt(in at, t));
                }

                Compare(label, $"after asking at {t:E3} s", field, points, before, count, moved);
            }
        }

        // And the time-free questions too, which a driven field answers at some instant of its own.
        foreach (var p in points)
        {
            var at = p;
            Touch(() => field.ElectricFieldAt(in at));
            Touch(() => field.PotentialAt(in at));
            Touch(() => field.SignedDistanceToDiscontinuity(in at));
        }

        Compare(label, "after the time-free questions", field, points, before, count, moved);
    }

    // A point the field refuses - an orbital analyser's axis - is a question it does not answer,
    // which changes nothing.
    private static void Touch<T>(Func<T> ask)
    {
        try
        {
            _ = ask();
        }
        catch (PointRefusedException)
        {
        }
    }

    private static void Compare(
        string label, string when, IElectrostaticField field, List<Vec3> points, List<double[]> before, int count, List<string> moved)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var now = Read(field, points[i], count);

            for (var k = 0; k < count; k++)
            {
                if (BitConverter.DoubleToInt64Bits(now[k]) != BitConverter.DoubleToInt64Bits(before[i][k]))
                {
                    moved.Add($"{label}: member {k} at {points[i]} {when}: {before[i][k]:R} became {now[k]:R}");
                }
            }
        }
    }

    private static double[] Read(IElectrostaticField field, Vec3 position, int count)
    {
        var distances = new double[count];
        field.MemberDistancesToDiscontinuity(in position, distances);
        return distances;
    }

    // The source, the detector, between them, and points through each solved cross-section's box.
    private static IEnumerable<Vec3> Points(CompiledModel model)
    {
        yield return model.SourcePosition;
        yield return model.DetectorPoint;
        yield return 0.5 * (model.SourcePosition + model.DetectorPoint);

        foreach (var solve in model.Fields.Select(f => f.Solve).OfType<CompiledSolvedField>())
        {
            foreach (var fx in new[] { 0.13, 0.5, 0.87 })
            {
                foreach (var fy in new[] { 0.21, 0.5, 0.79 })
                {
                    yield return new Vec3(
                        solve.MinX + (fx * (solve.MaxX - solve.MinX)),
                        solve.MinY + (fy * (solve.MaxY - solve.MinY)),
                        0.0);
                }
            }
        }
    }
}
