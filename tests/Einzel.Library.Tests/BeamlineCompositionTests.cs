using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// Two instruments in one document, which an exact analytic analyser could not do before a
/// region existed.
/// </summary>
/// <remarks>
/// <para>
/// SPEC.md Amendment 32. Superposition is exact for electrostatics and the sequencer can
/// express a handover, so nothing about composing two devices was ever in doubt — except
/// that <b>an analytic field has no extent, because a formula does not</b>. A
/// quadro-logarithmic potential grows as z squared, so an orbital analyser declared beside
/// the trap that injects it puts an enormous field across that trap.
/// </para>
/// <para>
/// The escape of declaring the analyser as solved geometry does not exist: its electrodes
/// are equipotentials of the field they produce, so their profile is transcendental in r
/// and the 2-D shape vocabulary has no curve a document can name.
/// </para>
/// </remarks>
public sealed class BeamlineCompositionTests(ITestOutputHelper output)
{
    /// <summary>An orbital analyser and a second device, with and without a region.</summary>
    private static string Document(bool bounded) =>
        $$"""
        {
          "schemaVersion": "0.7",
          "name": "beamline",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [70, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            {
              "type": "quadroLogarithmic",
              "curvature": { "value": 20, "unit": "V/mm^2" },
              "characteristicRadius": { "value": 20, "unit": "mm" },
              "centre": { "value": [0, 0, 0], "unit": "mm" }{{(bounded
                ? """
                ,
                  "region": {
                    "minX": { "value": -30, "unit": "mm" },
                    "maxX": { "value": 30, "unit": "mm" },
                    "minY": { "value": -30, "unit": "mm" },
                    "maxY": { "value": 30, "unit": "mm" },
                    "minZ": { "value": -30, "unit": "mm" },
                    "maxZ": { "value": 30, "unit": "mm" }
                  }
                """
                : string.Empty)}}
            },
            {
              "type": "uniform",
              "field": { "value": [1000, 0, 0], "unit": "V/m" },
              "region": {
                "minX": { "value": 50, "unit": "mm" },
                "maxX": { "value": 100, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" },
                "maxY": { "value": 10, "unit": "mm" },
                "minZ": { "value": -10, "unit": "mm" },
                "maxZ": { "value": 10, "unit": "mm" }
              }
            }
          ],
          "detector": {
            "planePoint": { "value": [95, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "maximumFlightTime": { "value": 100, "unit": "us" },
            "relativeTolerance": 1e-10
          }
        }
        """;

    private static CompiledModel Compile(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    /// <summary>Each device feels its own field and not its neighbour's.</summary>
    /// <remarks>
    /// <b>The unbounded case is the control and it is the point.</b> Without a region the
    /// orbital analyser reaches across the whole document, and the second device — a
    /// perfectly ordinary 1 kV/m accelerating section — is subjected to a field three orders
    /// larger than its own. That is not a small perturbation to be tolerated; it is a
    /// different instrument.
    /// </remarks>
    [Fact]
    public void ABoundedAnalyserLeavesItsNeighbourAlone()
    {
        // BuildReported rather than Build, and that is not a convenience. Bounding a
        // UNIFORM field puts a potential step at its boundary equal to E times the
        // distance from the potential's own zero - 100 V here - because a uniform
        // potential never decays. `Build` refuses that, correctly, and the refusal is
        // asserted in its own test below.
        var (loose, _) = FieldAssembly.BuildReported(Compile(Document(bounded: false)));
        var (tight, _) = FieldAssembly.BuildReported(Compile(Document(bounded: true)));

        // In the middle of the second device, and deliberately 2 mm off the analyser's
        // axis. ON that axis the unbounded analyser does not merely swamp its neighbour -
        // it REFUSES to be evaluated at all, because a quadro-logarithmic field is singular
        // where its central electrode is. So without a region, a perfectly ordinary
        // accelerating section 75 mm away has a line through it at which the model cannot
        // be asked a question.
        var downstream = new Vec3(0.075, 0.002, 0.0);

        var withAnalyser = loose.ElectricFieldAt(in downstream);
        var confined = tight.ElectricFieldAt(in downstream);

        output.WriteLine(
            $"in the second device, analyser unbounded: {withAnalyser.X,12:N1} V/m along x");

        output.WriteLine(
            $"                      analyser bounded:   {confined.X,12:N1} V/m along x");

        // Bounded, the second device feels exactly its own declared field.
        Assert.Equal(1000.0, confined.X, 6);
        Assert.Equal(0.0, confined.Y, 9);

        // Unbounded, it feels the analyser instead - by a factor of hundreds.
        Assert.True(
            Math.Abs(withAnalyser.X) > 100.0 * 1000.0,
            $"the unbounded analyser contributed {withAnalyser.X:N0} V/m where the second "
            + "device declares 1000, so this test is not demonstrating what a region is for");
    }

    /// <summary>And inside the analyser, its own field is untouched by the neighbour.</summary>
    /// <remarks>
    /// The other half, and the one that would be easy to lose: a region that silenced an
    /// element everywhere, or that leaked its neighbour inward, would pass the test above.
    /// The second device is bounded too, so the analyser's own volume is exactly what a
    /// document declaring the analyser alone would give.
    /// </remarks>
    [Fact]
    public void TheAnalysersOwnVolumeIsUnchangedByTheNeighbour()
    {
        var (composed, _) = FieldAssembly.BuildReported(Compile(Document(bounded: true)));

        var alone = QuadroLogarithmicField.Create(
            Core.Units.Quantity.From(20.0, "V/mm^2"),
            Core.Units.Quantity.From(20.0, "mm"),
            Vec3.Zero);

        var worst = 0.0;

        for (var k = 0; k < 30; k++)
        {
            var p = new Vec3(-0.015 + (0.001 * k), 0.005 + (0.0002 * k), 0.001);

            var a = alone.ElectricFieldAt(in p);
            var b = composed.ElectricFieldAt(in p);

            worst = Math.Max(worst, Math.Sqrt(Vec3.Dot(a - b, a - b)));
        }

        output.WriteLine($"worst difference inside the analyser: {worst:E1} V/m");

        Assert.Equal(0.0, worst);
    }

    /// <summary>The energy an ion would gain crossing the boundary is reported, always.</summary>
    /// <remarks>
    /// A box is not an equipotential of anything interesting, so the potential does not
    /// match across a region boundary and an ion crossing gains or loses whatever the inner
    /// field held there. REG-2's rule applies: the step is reported <b>whether or not it
    /// crosses a threshold</b>, because a reader who sees a number knows the boundary was
    /// checked and one who sees nothing cannot tell that from its not having been checked.
    /// It is a validity violation rather than an advisory, so it cannot be suppressed.
    /// </remarks>
    [Fact]
    public void TheRegionsPotentialStepIsReportedAndCannotBeSuppressed()
    {
        var (_, warnings) = FieldAssembly.BuildReported(Compile(Document(bounded: true)));

        var steps = warnings
            .Where(w => w.Code == "field.region-potential-step")
            .ToList();

        foreach (var warning in steps)
        {
            output.WriteLine(warning.Message);
        }

        // One per bounded element: the analyser and the second device.
        Assert.Equal(2, steps.Count);

        Assert.All(steps, w => Assert.False(w.IsSuppressible));

        // Every one names the step in volts AND as a fraction of the beam, because a step
        // means nothing on its own: 100 V across a 10 V beam is a different instrument and
        // across a 4 kV beam it is two and a half per cent.
        Assert.All(
            steps,
            w => Assert.Contains(" V on that boundary", w.Message, StringComparison.Ordinal));

        Assert.All(
            steps,
            w => Assert.Contains("this ion is accelerated through", w.Message, StringComparison.Ordinal));
    }

    /// <summary>A step that would break ACC-1 is refused, not carried.</summary>
    /// <remarks>
    /// <para>
    /// <b>`Build`'s contract narrowed when regions arrived, and this pins where it now
    /// draws the line.</b> It used to refuse a field with any warning at all. An
    /// unconverged solve means the numbers the field hands out may not be the ones the
    /// document describes, and a bare field has no envelope to carry that on — so refusing
    /// is the only honest option. A region's potential step is not that: the field is
    /// exactly what the document declares. Throwing on every one would make `Build`
    /// unusable for the composed beamlines a region exists to enable.
    /// </para>
    /// <para>
    /// So the step is graded against the ion's own energy and refused above ACC-1's 1 ppm
    /// budget, which is where a flight time across the boundary stops meaning what this
    /// engine claims for it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStepAboveTheAccuracyBudgetStillRefusesABareField()
    {
        var error = Assert.Throws<Core.Errors.EinzelException>(
            () => FieldAssembly.Build(Compile(Document(bounded: true))));

        output.WriteLine(error.Error.Constraint);

        Assert.Contains("ppm budget", error.Error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>A solved element may not declare a region: it already has one.</summary>
    /// <remarks>
    /// Refused rather than ignored. A solve is bounded by its own domain, so a region would
    /// be a second statement about the same extent, and a document that says a thing twice
    /// can say it two ways. The same argument refuses a geometry declaring both
    /// <c>drive</c> and <c>drives</c>.
    /// </remarks>
    [Fact]
    public void ASolvedElementMayNotDeclareARegion()
    {
        var json = """
        {
          "schemaVersion": "0.7",
          "name": "doubly-bounded",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            {
              "type": "solved2d",
              "solve": {
                "minX": { "value": 0, "unit": "mm" },
                "maxX": { "value": 10, "unit": "mm" },
                "minY": { "value": 0, "unit": "mm" },
                "maxY": { "value": 10, "unit": "mm" },
                "cellSize": { "value": 1, "unit": "mm" },
                "electrodes": []
              },
              "region": {
                "minX": { "value": 0, "unit": "mm" },
                "maxX": { "value": 5, "unit": "mm" },
                "minY": { "value": 0, "unit": "mm" },
                "maxY": { "value": 5, "unit": "mm" },
                "minZ": { "value": -5, "unit": "mm" },
                "maxZ": { "value": 5, "unit": "mm" }
              }
            }
          ],
          "detector": {
            "planePoint": { "value": [9, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "maximumFlightTime": { "value": 10, "unit": "us" },
            "relativeTolerance": 1e-9
          }
        }
        """;

        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.False(validation.IsValid);

        var error = validation.Errors.Single(e => e.Path.EndsWith("/region", StringComparison.Ordinal));

        output.WriteLine($"{error.Path}: {error.Constraint}");

        Assert.Contains("already bounded", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>A region with no extent is refused rather than silencing the element.</summary>
    /// <remarks>
    /// Equal bounds would make the element contribute nothing anywhere, which is what
    /// deleting the element does and says more clearly. Reversed bounds are the same
    /// mistake written the other way round, and both are one comparison to catch.
    /// </remarks>
    [Theory]
    [InlineData(30.0, 30.0, "equal")]
    [InlineData(30.0, 10.0, "reversed")]
    public void ARegionWithNoExtentIsRefused(double minX, double maxX, string why)
    {
        var json = $$"""
        {
          "schemaVersion": "0.7",
          "name": "degenerate-region",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            {
              "type": "uniform",
              "field": { "value": [1000, 0, 0], "unit": "V/m" },
              "region": {
                "minX": { "value": {{minX}}, "unit": "mm" },
                "maxX": { "value": {{maxX}}, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" },
                "maxY": { "value": 10, "unit": "mm" },
                "minZ": { "value": -10, "unit": "mm" },
                "maxZ": { "value": 10, "unit": "mm" }
              }
            }
          ],
          "detector": {
            "planePoint": { "value": [95, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "maximumFlightTime": { "value": 100, "unit": "us" },
            "relativeTolerance": 1e-10
          }
        }
        """;

        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.False(validation.IsValid, $"a {why} region should be refused");

        output.WriteLine(validation.Errors[0].Constraint);

        Assert.Contains(
            "upper bound must exceed",
            validation.Errors[0].Constraint,
            StringComparison.Ordinal);
    }
}
