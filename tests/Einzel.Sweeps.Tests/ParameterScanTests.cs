using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Sweeps;
using Xunit.Abstractions;

namespace Einzel.Sweeps.Tests;

/// <summary>
/// The scan driver, against a model whose figure is an arithmetic function of its
/// parameters, so the curve is known exactly and only the driver is under test.
/// </summary>
/// <remarks>
/// The third driver beside the sweep and the optimiser, and the one every curve
/// this engine has produced so far was hand-written for: the low-mass cut-off
/// scans, the extraction-slot scan, the drift-length scan. What those did not have
/// was a manifest, a result file, or a form an agent could write.
/// </remarks>
public sealed class ParameterScanTests(ITestOutputHelper output)
{
    private static ModelDocument Model() => new()
    {
        SchemaVersion = "0.2",
        Name = "scan-fixture",
        Parameters = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["alpha"] = new() { Value = 100.0, Unit = "mm", Minimum = 50.0, Maximum = 150.0 },
            ["decades"] = new() { Value = 1.0, Unit = "1", Minimum = 1e-4, Maximum = 1e4 },
            ["derived"] = new() { Expression = "alpha * 2", Unit = "mm" },
        },
        Ion = new IonDocument { MassToCharge = new QuantityValue(500.0, "Da"), ChargeNumber = 1 },
        Source = new SourceDocument
        {
            Position = new VectorValue([-100.0, 0.0, 0.0], "mm"),
            Direction = new DirectionValue([1.0, 0.0, 0.0]),
            AccelerationPotential = new QuantityValue(4.0, "kV"),
        },
        Fields = [new FieldDocument { Type = "fieldFree" }],
        Detector = new DetectorDocument
        {
            PlanePoint = new VectorValue([0.0, 0.0, 0.0], "mm"),
            Normal = new DirectionValue([-1.0, 0.0, 0.0]),
        },
        Transport = new TransportDocument { MaximumFlightTime = new QuantityValue(1.0, "ms") },
    };

    private static double? Alpha(CompiledModel model) => model.Parameters["alpha"].In("mm");

    private static ScanAxis Axis(int points, ScanSpacing spacing = ScanSpacing.Linear) =>
        new("alpha", Quantity.From(60.0, "mm"), Quantity.From(140.0, "mm"), points, spacing);

    [Fact]
    public void BothEndsOfTheRangeAreVisited()
    {
        // The off-by-one that matters. A scan declared from 60 to 140 that stops at
        // 136 has quietly answered about a different range, and on a stability scan
        // the end is usually the point - the boundary is at one edge or the other.
        var result = ParameterScan.Run(Model(), Axis(5), Alpha);

        var values = result.Points.Select(p => p.ValueSi * 1e3).ToArray();

        output.WriteLine($"alpha / mm: {string.Join(", ", values.Select(v => v.ToString("G6")))}");

        Assert.Equal(5, values.Length);
        Assert.Equal(60.0, values[0], 1e-9);
        Assert.Equal(140.0, values[^1], 1e-9);

        // Evenly spaced, and the middle exactly at the middle - which it is not if
        // the step is accumulated rather than computed from the fraction.
        Assert.Equal(100.0, values[2], 1e-9);
    }

    [Fact]
    public void AScanFromOneDeclaredBoundToTheOtherReachesBoth()
    {
        // The obvious thing to write, and the one that breaks if the ends are
        // interpolated to rather than returned. Half of (0.1, 0.2) is
        // 0.15000000000000002 in binary, so an end computed as a fraction of the
        // range lands an ulp outside the bound and the point is refused by
        // validation - with nothing on the page to say why the last row is blank.
        var axis = new ScanAxis("alpha", Quantity.From(50.0, "mm"), Quantity.From(150.0, "mm"), 6);

        var result = ParameterScan.Run(Model(), axis, Alpha);

        output.WriteLine(
            $"alpha / mm: {string.Join(", ", result.Points.Select(p => (p.ValueSi * 1e3).ToString("R")))}");

        Assert.Equal(6, result.Succeeded);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "scan.outside-declared-bounds");

        // Exactly, not nearly: the ends are the declared quantities themselves.
        Assert.Equal(0.05, result.Points[0].ValueSi);
        Assert.Equal(0.15, result.Points[^1].ValueSi);
    }

    [Fact]
    public void TheFigureIsEvaluatedAtEachPoint()
    {
        // The figure here is the parameter itself, so the curve has to be the scan.
        // A driver that validated the overrides and then evaluated the unperturbed
        // model would give a flat line, which looks like a perfectly good result.
        var result = ParameterScan.Run(Model(), Axis(5), Alpha);

        foreach (var point in result.Points)
        {
            Assert.NotNull(point.FigureOfMerit);
            Assert.Equal(point.ValueSi * 1e3, point.FigureOfMerit!.Value, 1e-9);
        }

        Assert.Equal(5, result.Succeeded);

        // And the nominal is the model's own value, not the first point of the scan.
        Assert.Equal(100.0, result.Nominal!.Value, 1e-9);
    }

    [Fact]
    public void ALogarithmicScanIsEvenInTheLogarithm()
    {
        // A range spanning decades taken linearly puts every point but one in the
        // top decade. The case this exists for is a pressure scan, where the thin
        // end is where the transport mode changes.
        var axis = new ScanAxis(
            "decades", Quantity.From(1e-3, "1"), Quantity.From(1e3, "1"), 7, ScanSpacing.Logarithmic);

        var result = ParameterScan.Run(
            Model(), axis, model => model.Parameters["decades"].In("1"));

        var values = result.Points.Select(p => p.ValueSi).ToArray();

        output.WriteLine($"decades: {string.Join(", ", values.Select(v => v.ToString("G4")))}");

        Assert.Equal(1e-3, values[0], 1e-12);
        Assert.Equal(1e3, values[^1], 1e-9);

        // One decade per point across six intervals and six decades.
        for (var i = 0; i + 1 < values.Length; i++)
        {
            Assert.Equal(10.0, values[i + 1] / values[i], 1e-9);
        }
    }

    [Fact]
    public void APointOutsideTheDeclaredBoundsIsARowWithAReason()
    {
        // Not the end of the scan. Finding out where a design stops working is what
        // a scan is for, and a driver that threw on the first refusal would stop
        // exactly at the interesting value.
        var axis = new ScanAxis("alpha", Quantity.From(100.0, "mm"), Quantity.From(180.0, "mm"), 5);

        var result = ParameterScan.Run(Model(), axis, Alpha);

        foreach (var point in result.Points)
        {
            output.WriteLine(
                $"{point.ValueSi * 1e3,8:F1} mm  "
                + (point.FigureOfMerit is null ? point.Failure![..40] : "ok"));
        }

        Assert.Equal(5, result.Points.Count);

        // 100, 120, 140 are inside the declared [50, 150]; 160 and 180 are not.
        Assert.Equal(3, result.Succeeded);
        Assert.All(result.Points.Skip(3), p => Assert.NotNull(p.Failure));

        // Said up front as well as per row, because half a table of blanks reads as
        // the solver failing rather than as the model refusing.
        Assert.Contains(result.Warnings, w => w.Code == "scan.outside-declared-bounds");
    }

    [Fact]
    public void TheSteepestIntervalIsWhereTheFigureMovesMost()
    {
        // The precursor to a Class B boundary, and deliberately not one: what it
        // reports is where on the grid actually computed the figure moves fastest.
        var axis = new ScanAxis("alpha", Quantity.From(60.0, "mm"), Quantity.From(140.0, "mm"), 9);

        // A step at 100 mm, which is a stand-in for a stability edge.
        var result = ParameterScan.Run(
            Model(), axis, model => model.Parameters["alpha"].In("mm") > 100.0 ? 1.0 : 0.0);

        var steepest = result.SteepestInterval;

        Assert.NotNull(steepest);

        output.WriteLine(
            $"steepest between {steepest!.Value.LowSi * 1e3:F1} and "
            + $"{steepest.Value.HighSi * 1e3:F1} mm, change {steepest.Value.Change:G4}");

        Assert.Equal(100.0, steepest.Value.LowSi * 1e3, 1e-9);
        Assert.Equal(110.0, steepest.Value.HighSi * 1e3, 1e-9);
        Assert.Equal(1.0, steepest.Value.Change, 1e-12);
    }

    [Fact]
    public void AFigureThatStopsExistingRanksAboveOneThatMerelyMoves()
    {
        // On a mass filter the cut-off is where the ion stops arriving, so a driver
        // that scored a vanished figure as "no change" would rank the one interval
        // that matters last. Here a large finite step competes with a vanishing, and
        // the vanishing has to win.
        var axis = new ScanAxis("alpha", Quantity.From(60.0, "mm"), Quantity.From(140.0, "mm"), 9);

        var result = ParameterScan.Run(Model(), axis, model =>
        {
            var alpha = model.Parameters["alpha"].In("mm");

            // Lost past 120 mm; a step of a thousand at 90 mm.
            return alpha > 120.0 ? null : alpha > 90.0 ? 1000.0 : 0.0;
        });

        var steepest = result.SteepestInterval;

        Assert.NotNull(steepest);

        output.WriteLine(
            $"steepest between {steepest!.Value.LowSi * 1e3:F1} and {steepest.Value.HighSi * 1e3:F1} mm");

        Assert.Equal(120.0, steepest.Value.LowSi * 1e3, 1e-9);
        Assert.True(double.IsPositiveInfinity(steepest.Value.Change));
    }

    [Fact]
    public void AScanThatProducesNothingAnywhereSaysSo()
    {
        // An all-empty table and a genuinely flat response look nothing alike in the
        // data and identical on a plot with no points on it.
        var result = ParameterScan.Run(Model(), Axis(5), _ => null);

        Assert.Equal(0, result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Code == "scan.no-figure-anywhere");
        Assert.Contains(result.Warnings, w => !w.IsSuppressible);
    }

    [Fact]
    public void ADerivedParameterCannotBeScanned()
    {
        // Whatever it is derived from overwrites it the moment the model compiles,
        // so a scan of one would report a flat curve with no sign anything was wrong.
        var axis = new ScanAxis("derived", Quantity.From(1.0, "mm"), Quantity.From(2.0, "mm"), 5);

        var failure = Assert.Throws<EinzelException>(
            () => ParameterScan.Run(Model(), axis, Alpha));

        output.WriteLine(failure.Error.Constraint);

        Assert.Equal("/scan/parameter", failure.Error.Path);
        Assert.Contains("derived", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeOfZeroWidthIsRefused()
    {
        // Every point is the same run, which is not a scan and would report a
        // perfectly flat curve.
        var axis = new ScanAxis("alpha", Quantity.From(100.0, "mm"), Quantity.From(100.0, "mm"), 5);

        var failure = Assert.Throws<EinzelException>(() => ParameterScan.Run(Model(), axis, Alpha));

        output.WriteLine(failure.Error.Constraint);
        Assert.Equal("/scan/to", failure.Error.Path);
    }

    [Fact]
    public void ALogarithmicScanAcrossZeroIsRefused()
    {
        var axis = new ScanAxis(
            "alpha", Quantity.From(-10.0, "mm"), Quantity.From(10.0, "mm"), 5, ScanSpacing.Logarithmic);

        var failure = Assert.Throws<EinzelException>(() => ParameterScan.Run(Model(), axis, Alpha));

        output.WriteLine(failure.Error.Constraint);
        Assert.Equal("/scan/spacing", failure.Error.Path);
    }

    [Fact]
    public void ARangeInTheWrongDimensionIsRefused()
    {
        // SI internally, units explicit at every boundary. A scan of a length over
        // a range of volts is a wrong question rather than a wrong answer.
        var axis = new ScanAxis("alpha", Quantity.From(1.0, "V"), Quantity.From(2.0, "V"), 5);

        var failure = Assert.Throws<EinzelException>(() => ParameterScan.Run(Model(), axis, Alpha));

        output.WriteLine($"{failure.Error.Code} at {failure.Error.Path}: {failure.Error.Constraint}");

        Assert.Equal(ErrorCodes.UnitsIncompatible, failure.Error.Code);
    }

    /// <summary>Running the points at once gives exactly the sequential curve.</summary>
    /// <remarks>
    /// <para>
    /// <b>Bit-identical, not close.</b> The points are independent — each compiles its own
    /// model from the same immutable document and solves its own field — and every seed a
    /// point uses is its own, so nothing about a point's answer depends on what else is
    /// running. Anything less than exact equality would mean it does.
    /// </para>
    /// <para>
    /// The ordering is asserted with it, because the rows are written by index rather than
    /// appended: a scan whose curve arrived in completion order would reorder itself run to
    /// run, which breaks CLI-6's deterministic output and, worse, breaks <c>verify</c> —
    /// the stored result would stop matching a re-run of the same study.
    /// </para>
    /// </remarks>
    [Fact]
    public void RunningThePointsAtOnceGivesTheSequentialCurve()
    {
        var axis = new ScanAxis(
            "alpha", Quantity.From(40.0, "mm"), Quantity.From(200.0, "mm"), 64);

        var sequential = ParameterScan.Run(Model(), axis, Alpha, maxParallelism: 1);
        var parallel = ParameterScan.Run(Model(), axis, Alpha, maxParallelism: 16);

        Assert.Equal(sequential.Points.Count, parallel.Points.Count);

        for (var i = 0; i < sequential.Points.Count; i++)
        {
            var a = sequential.Points[i];
            var b = parallel.Points[i];

            // Index and parameter value pin the ordering; the figure pins the answer.
            Assert.Equal(a.Index, b.Index);
            Assert.Equal(a.ValueSi, b.ValueSi);
            Assert.Equal(a.FigureOfMerit, b.FigureOfMerit);
            Assert.Equal(a.Failure, b.Failure);
        }

        // And the rows really are in scan order, which is what makes the index assertion
        // above mean something rather than comparing two identically-shuffled lists.
        Assert.Equal(
            Enumerable.Range(0, axis.Points),
            parallel.Points.Select(p => p.Index));
    }
}
