using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Sweeps;
using Xunit.Abstractions;

namespace Einzel.Sweeps.Tests;

/// <summary>
/// The Class B boundary search, against a step whose position is known exactly.
/// </summary>
/// <remarks>
/// ACC-6 asks for a boundary resolved to one part in five hundred of the scan.
/// What is asserted here is that it lands on a boundary this test placed, that the
/// bracket really contains it, and that reaching that resolution costs a
/// logarithmic number of evaluations rather than a linear one - which is the entire
/// reason this exists beside <see cref="ParameterScan"/>.
/// </remarks>
public sealed class BoundarySearchTests(ITestOutputHelper output)
{
    private static ModelDocument Model() => new()
    {
        SchemaVersion = "0.2",
        Name = "boundary-fixture",
        Parameters = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["alpha"] = new() { Value = 100.0, Unit = "mm", Minimum = 0.0, Maximum = 500.0 },
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

    /// <summary>The scan range, 20 mm to 300 mm.</summary>
    private static ScanAxis Bracket() =>
        new("alpha", Quantity.From(20.0, "mm"), Quantity.From(300.0, "mm"), 2);

    private const double Edge = 137.0;

    /// <summary>
    /// The boundary in millimetres, out of its GRD-1 envelope.
    /// </summary>
    /// <remarks>
    /// Deconstruction is the only route to a magnitude - <c>Measured</c> exposes no
    /// member that returns one, which is the rule working as intended and is why
    /// this helper exists rather than a property access. A test comparing a boundary
    /// against a known edge legitimately wants the number; taking it here makes the
    /// discard visible and greppable in one place.
    /// </remarks>
    private static double Millimetres(Core.Results.Measured measured)
    {
        var (value, _, _, _) = measured;

        return value.In("mm");
    }

    /// <summary>One inside the region, zero outside, with the step at 137 mm.</summary>
    private static double? Step(CompiledModel model) =>
        model.Parameters["alpha"].In("mm") < Edge ? 1.0 : 0.0;

    [Fact]
    public void ItLandsOnTheBoundaryAndBracketsIt()
    {
        var result = BoundarySearch.Run(Model(), Bracket(), Step, 0.5);

        var (value, interval, evidence, _) = result.Boundary;

        output.WriteLine($"boundary   {value.In("mm"):F6} mm");
        output.WriteLine($"bracket    [{result.LowSi * 1e3:F6}, {result.HighSi * 1e3:F6}] mm");
        output.WriteLine($"resolved   1 part in {1.0 / result.ResolvedFraction:F0} of the range");
        output.WriteLine($"cost       {result.Evaluations} evaluations");
        output.WriteLine($"evidence   {evidence}");

        // The bracket contains the boundary this test put there. Asserted before
        // the midpoint, because containment is what a bisection actually proves and
        // the midpoint is a convention on top of it.
        Assert.True(result.LowSi * 1e3 <= Edge, "the inside end is past the boundary");
        Assert.True(result.HighSi * 1e3 >= Edge, "the outside end is short of the boundary");

        // And the reported interval is that bracket, not an error bar around a
        // point - which is the honest GRD-1 reading of a bisection.
        Assert.Equal(result.LowSi, interval.LowerSi, 1e-15);
        Assert.Equal(result.HighSi, interval.UpperSi, 1e-15);

        Assert.Equal(Edge, value.In("mm"), 280.0 * BoundarySearch.AccuracyTarget);
    }

    [Fact]
    public void ItReachesAccSixAndSaysSo()
    {
        var result = BoundarySearch.Run(Model(), Bracket(), Step, 0.5);

        Assert.True(result.MetAccuracyTarget);
        Assert.True(result.ResolvedFraction <= BoundarySearch.AccuracyTarget);

        // No complaint about its own resolution when it met the target.
        Assert.DoesNotContain(result.Warnings, w => w.Code == "boundary.below-acc6");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "boundary.budget-exhausted");
    }

    [Fact]
    public void TheCostIsLogarithmicRatherThanLinear()
    {
        // The whole reason this exists beside a scan. One part in five hundred on a
        // grid costs 501 evaluations; halving the bracket costs log2(500) plus the
        // two that establish it, and the confirmation walk costs another log2 either
        // way. Three logarithms is still not a linear number.
        var result = BoundarySearch.Run(Model(), Bracket(), Step, 0.5);

        var grid = (int)Math.Ceiling(1.0 / BoundarySearch.AccuracyTarget) + 1;

        output.WriteLine($"bisection {result.Evaluations - result.Probes} evaluations");
        output.WriteLine($"confirm   {result.Probes} probes");
        output.WriteLine($"total     {result.Evaluations}, grid would be {grid}");

        Assert.True(
            result.Evaluations - result.Probes <= 14,
            $"{result.Evaluations - result.Probes} evaluations to halve a bracket 9 times is too many");

        Assert.True(result.Evaluations < grid / 10);
    }

    [Fact]
    public void ACleanEdgeIsConfirmedRatherThanAssumed()
    {
        var result = BoundarySearch.Run(Model(), Bracket(), Step, 0.5);

        output.WriteLine($"probes {result.Probes}, second crossing {result.SecondCrossingSi?.ToString() ?? "none"}");

        Assert.Null(result.SecondCrossingSi);
        Assert.True(result.Probes > 0, "a converged bracket must be walked");

        // REG-2's rule: a check made and passed has to be visible, or a reader
        // cannot tell it from a check never made.
        Assert.Contains(result.Warnings, w => w.Code == "boundary.single-crossing-checked");
        Assert.DoesNotContain(result.Warnings, w => w.Code == "boundary.multiple-crossings");
    }

    /// <summary>
    /// A cut-off at 137 mm with an island of survivors at 150 to 156 mm past it.
    /// </summary>
    /// <remarks>
    /// Not a contrived shape. It is what a stability boundary measured through a
    /// finite observation window looks like: the growth rate goes to zero at the
    /// edge, so whether an ion reaches an electrode inside the hold time stops being
    /// a property of the design and starts being a property of the hold, and the
    /// clean step frays into a strip of survivors. The shipped Paul trap does
    /// exactly this at sixty RF cycles and stops at two hundred.
    /// </remarks>
    private static double? Frayed(CompiledModel model)
    {
        var alpha = model.Parameters["alpha"].In("mm");

        return alpha < Edge || alpha is >= 150.0 and <= 156.0 ? 1.0 : 0.0;
    }

    [Fact]
    public void ASecondCrossingIsFoundRatherThanBisectedPast()
    {
        // The failure this exists for. Bisection converges onto 137 mm and every
        // step of the path is consistent with a clean cut-off - a frayed edge and a
        // clean one produce identical histories, so the search can never contradict
        // its own assumption. Only a walk outward from the converged bracket can.
        var result = BoundarySearch.Run(Model(), Bracket(), Frayed, 0.5);

        output.WriteLine($"edge   {Millimetres(result.Boundary):F4} mm");
        output.WriteLine($"probes {result.Probes}");
        output.WriteLine($"again  {result.SecondCrossingSi * 1e3:F4} mm");

        // It did find an edge at 137, correctly and to ACC-6.
        Assert.Equal(Edge, Millimetres(result.Boundary), 280.0 * BoundarySearch.AccuracyTarget);

        // And it says there is another crossing beyond it, so that number is one
        // edge of several rather than the edge of the region.
        Assert.NotNull(result.SecondCrossingSi);
        Assert.InRange(result.SecondCrossingSi!.Value * 1e3, 150.0, 156.0);

        var warning = Assert.Single(
            result.Warnings, w => w.Code == "boundary.multiple-crossings");

        Assert.Equal(Core.Results.WarningSeverity.ValidityViolation, warning.Severity);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "boundary.single-crossing-checked");
    }

    [Fact]
    public void TheWalkStaysInsideTheDeclaredRange()
    {
        // A bracket that spans only one edge of a band cannot be told from a cut-off
        // by any amount of walking, because the other edge is not in the range the
        // study declared - and evaluating outside it would be answering a question
        // nobody asked, at parameter values that may not even be buildable. So the
        // honest answer is "no second crossing here", and the warning says
        // "consistent with" rather than "proof of".
        var axis = new ScanAxis("alpha", Quantity.From(150.0, "mm"), Quantity.From(300.0, "mm"), 2);

        var result = BoundarySearch.Run(Model(), axis, Frayed, 0.5);

        output.WriteLine($"edge   {Millimetres(result.Boundary):F4} mm");
        output.WriteLine($"probes {result.Probes}");

        Assert.Equal(156.0, Millimetres(result.Boundary), 150.0 * BoundarySearch.AccuracyTarget);
        Assert.Null(result.SecondCrossingSi);

        var warning = Assert.Single(
            result.Warnings, w => w.Code == "boundary.single-crossing-checked");

        Assert.Contains("not proof", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequestedResolutionCoarserThanAccSixIsQualified()
    {
        // A boundary quoted more precisely than its bracket is a boundary quoted
        // more precisely than it was measured, so a coarse search says so rather
        // than reporting a midpoint that looks like a measurement.
        var result = BoundarySearch.Run(Model(), Bracket(), Step, 0.5, resolution: 0.05);

        Assert.True(result.MetAccuracyTarget);
        Assert.Contains(result.Warnings, w => w.Code == "boundary.below-acc6");

        output.WriteLine(result.Warnings.Single(w => w.Code == "boundary.below-acc6").Message);
    }

    [Fact]
    public void AFigureThatStopsExistingIsOutside()
    {
        // The case this is really for. A low-mass cut-off is the value at which the
        // ion stops arriving, so a search that treated a missing figure as a failed
        // evaluation would refuse to look for the thing being looked for.
        var result = BoundarySearch.Run(
            Model(),
            Bracket(),
            model => model.Parameters["alpha"].In("mm") < Edge ? 1.0 : (double?)null,
            0.5);

        var found = Millimetres(result.Boundary);

        output.WriteLine($"boundary {found:F4} mm from a vanishing figure");

        Assert.Equal(Edge, found, 280.0 * BoundarySearch.AccuracyTarget);
        Assert.True(result.MetAccuracyTarget);
    }

    [Fact]
    public void ItWorksWithTheInsideEndEitherWay()
    {
        // A low-mass cut-off is crossed going up and a high-mass one going down, so
        // the search must not care which end of the bracket is inside. Same edge,
        // opposite sense, and the answer has to be the same.
        var rising = BoundarySearch.Run(Model(), Bracket(), Step, 0.5);

        var falling = BoundarySearch.Run(
            Model(),
            Bracket(),
            model => model.Parameters["alpha"].In("mm") < Edge ? 0.0 : 1.0,
            0.5);

        var up = Millimetres(rising.Boundary);
        var down = Millimetres(falling.Boundary);

        output.WriteLine($"inside below: {up:F4} mm");
        output.WriteLine($"inside above: {down:F4} mm");

        Assert.Equal(up, down, 280.0 * BoundarySearch.AccuracyTarget);
    }

    [Fact]
    public void ABracketWhoseEndsAgreeIsRefused()
    {
        // Bisection needs the ends to disagree, and guessing a bracket from a
        // predicate that never flips would return a midpoint with no meaning at all.
        var failure = Assert.Throws<EinzelException>(() => BoundarySearch.Run(
            Model(),
            new ScanAxis("alpha", Quantity.From(20.0, "mm"), Quantity.From(100.0, "mm"), 2),
            Step,
            0.5));

        output.WriteLine(failure.Error.Constraint);
        output.WriteLine(failure.Error.Suggestion!);

        Assert.Equal("/boundary", failure.Error.Path);
        Assert.Contains("both ends", failure.Error.Constraint, StringComparison.Ordinal);

        // AGT-3: what to do about it, not only what went wrong.
        Assert.Contains("einzel scan", failure.Error.Suggestion!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSenseOfTheThresholdIsRespected()
    {
        // Same figure, same bracket, threshold read the other way round. A search
        // that ignored the sense would find the same edge from both, which is the
        // failure a single-sense test cannot see.
        var above = BoundarySearch.Run(
            Model(), Bracket(), model => model.Parameters["alpha"].In("mm"), 137.0,
            BoundarySense.Below);

        var edge = Millimetres(above.Boundary);

        output.WriteLine($"alpha <= 137 up to {edge:F4} mm");

        Assert.Equal(137.0, edge, 280.0 * BoundarySearch.AccuracyTarget);
    }
}
