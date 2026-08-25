using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Sweeps;
using Xunit.Abstractions;

namespace Einzel.Sweeps.Tests;

/// <summary>
/// The FLD-1 sensitivity fields, and the FLD-2 check that gates them.
/// </summary>
/// <remarks>
/// Spec §23 recommends spiking the linearity assumption before Phase 2 commits.
/// This is that spike: it measures how far a perturbation can go before the
/// linearised field stops standing in for a re-solve, rather than assuming the
/// answer.
/// </remarks>
public sealed class SensitivityFieldTests(ITestOutputHelper output)
{
    /// <summary>
    /// A fixed solve domain with a movable plate inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The domain, the cell size, and the mesh are all constant; only the plate's
    /// position and its potential vary. That is what a tolerance study actually
    /// models — a stripe placed a hundred microns off, not the whole chamber
    /// changing size — and it is also the only thing sensitivity fields can
    /// represent, since they are node-by-node differences on one mesh.
    /// </para>
    /// <para>
    /// Two channels with known character. The potential is exactly proportional to
    /// the applied voltage, so the linearisation should be exact. It goes as one
    /// over the plate position, so the linearisation must fail somewhere and the
    /// question is where.
    /// </para>
    /// </remarks>
    private static ModelDocument Model() => new()
    {
        SchemaVersion = "0.2",
        Name = "movable-plate",
        Parameters = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["plateX"] = new() { Value = 40.0, Unit = "mm", Minimum = 10.0, Maximum = 55.0 },
            ["applied"] = new() { Value = 1000.0, Unit = "V", Minimum = 0.0, Maximum = 5000.0 },
            ["thickness"] = new() { Value = 2.0, Unit = "mm" },
            ["halfThickness"] = new() { Expression = "thickness / 2", Unit = "mm" },
        },
        Ion = new IonDocument { MassToCharge = new QuantityValue(500.0, "Da"), ChargeNumber = 1 },
        Source = new SourceDocument
        {
            Position = new VectorValue([5.0, 0.0, 0.0], "mm"),
            Direction = new DirectionValue([1.0, 0.0, 0.0]),
            AccelerationPotential = new QuantityValue(100.0, "V"),
        },
        Fields =
        [
            new FieldDocument
            {
                Type = "solved2d",
                Solve = new SolvedFieldDocument
                {
                    // Every bound is a literal. Nothing here moves with a channel.
                    MinX = new QuantityValue(0.0, "mm"),
                    MinY = new QuantityValue(-10.0, "mm"),
                    MaxX = new QuantityValue(60.0, "mm"),
                    MaxY = new QuantityValue(10.0, "mm"),
                    // Coarse deliberately. The plate is an interior electrode, so the
                    // coarsening limit leaves few multigrid levels and each solve is
                    // expensive; a fine grid here made the campaign large enough to
                    // bring down the test host. The solve tolerance is still three
                    // orders below the residual being measured.
                    CellSize = new QuantityValue(1.0, "mm"),
                    Tolerance = 1e-9,
                    TopEdge = "neumann",
                    BottomEdge = "neumann",
                    RightEdge = "neumann",
                    Electrodes =
                    [
                        new ElectrodeDocument
                        {
                            Name = "ground", Shape = "rectangle",
                            MinX = new QuantityValue(0.0, "mm"),
                            MaxX = new QuantityValue(0.0, "mm"),
                            // Both electrodes overhang the domain in y, so the gap
                            // between them is one-dimensional and its potential is
                            // a straight ramp. Without that the field goes round
                            // the end of the plate and there is no closed form to
                            // measure a linearisation against.
                            MinY = new QuantityValue(-50.0, "mm"),
                            MaxY = new QuantityValue(50.0, "mm"),
                            Potential = new QuantityValue(0.0, "V"),
                        },
                        new ElectrodeDocument
                        {
                            Name = "plate", Shape = "rectangle",
                            MinX = new QuantityValue(0.0, "mm") { Expression = "plateX - halfThickness" },
                            MaxX = new QuantityValue(0.0, "mm") { Expression = "plateX + halfThickness" },
                            MinY = new QuantityValue(-50.0, "mm"),
                            MaxY = new QuantityValue(50.0, "mm"),
                            Potential = new QuantityValue(0.0, "V") { Expression = "applied" },
                        },
                    ],
                },
            },
        ],
        Detector = new DetectorDocument
        {
            PlanePoint = new VectorValue([100.0, 0.0, 0.0], "mm"),
            Normal = new DirectionValue([-1.0, 0.0, 0.0]),
        },
        Transport = new TransportDocument { MaximumFlightTime = new QuantityValue(1.0, "ms") },
    };

    private static PerturbationChannel Channel(string name, double halfWidth, string unit) =>
        new(name, Quantity.From(halfWidth, unit));

    [Fact]
    public void ALinearDependencyIsCapturedExactly()
    {
        // The potential is exactly proportional to the applied voltage, so its
        // sensitivity field is the whole story and superposition is not an
        // approximation at all.
        var channels = new[] { Channel("applied", 200.0, "V") };
        var (nominal, fields) = SensitivityFields.Build(Model(), channels);

        var check = SensitivityFields.Check(Model(), channels, nominal, fields, budget: 1e-6, draws: 3);

        output.WriteLine(
            $"voltage channel: {check.Draws} re-solves, worst potential residual "
            + $"{check.WorstRelativeResidual:E3}, worst field residual {check.WorstFieldResidual:E3}");

        Assert.True(check.Draws > 0, "no draws were re-solved, so the check proved nothing");
        Assert.True(check.Passed, $"a linear dependency should linearise exactly; residual {check.WorstRelativeResidual:E3}");
    }

    [Fact]
    public void SuperpositionReproducesAReSolveForAVoltageChange()
    {
        var channels = new[] { Channel("applied", 200.0, "V") };
        var (nominal, fields) = SensitivityFields.Build(Model(), channels);

        var linear = SensitivityFields.Linearise(
            nominal, fields, new Dictionary<string, double>(StringComparer.Ordinal) { ["applied"] = 150.0 });

        var (exact, _) = SensitivityFields.SolveAt(
            Model(),
            new Dictionary<string, Quantity>(StringComparer.Ordinal) { ["applied"] = Quantity.From(1150.0, "V") });

        var worst = 0.0;

        for (var k = 0; k < exact.Values.Length; k++)
        {
            worst = Math.Max(worst, Math.Abs(linear.Values[k] - exact.Values[k]));
        }

        output.WriteLine($"worst difference over the whole grid: {worst:E3} V on 1150 V");
        Assert.True(worst < 1e-3, $"superposition differs from a re-solve by {worst:E3} V");
    }

    [Fact]
    public void ASubCellPerturbationMovesTheBoundaryRatherThanNothing()
    {
        // This measurement used to be the most dangerous thing in the codebase,
        // and it was silent. The mesh here is 0.94 mm; a 0.2 mm plate move changed
        // which nodes the rasterised plate occupied not at all, so the perturbed
        // solve came back bit-identical to the nominal and the derivative was
        // exactly zero. Residual 0.000E+000 — which reads as perfect linearity and
        // means the model never saw the perturbation. A tolerance study built on
        // it would have reported the parameter as having no influence.
        //
        // With a cut-cell boundary the surface is where the geometry says it is,
        // at any fraction of a cell, so a fifth of a cell is a fifth of a cell.
        var step = 0.1e-3;
        var (nominal, fields) = SensitivityFields.Build(Model(), [Channel("plateX", 0.2, "mm")]);
        var derivative = fields[0].Derivative;

        // The gap is a ramp from the grounded plane at x = 0 to the plate face at
        // 39 mm, so V = 1000 x / L and dV/dL = -1000 x / L squared. Nothing here is
        // fitted: it is the closed form for the geometry.
        const double Gap = 39e-3;
        var worst = 0.0;
        var probes = 0;

        for (var i = 1; i < nominal.Grid.CountX; i++)
        {
            var x = nominal.Grid.X(i);

            if (x >= Gap - nominal.Grid.Spacing)
            {
                break;
            }

            var expected = -1000.0 * x / (Gap * Gap);
            worst = Math.Max(worst, Math.Abs(derivative[i, nominal.Grid.CountY / 2] - expected) / Math.Abs(expected));
            probes++;
        }

        output.WriteLine(
            $"step {step * 1e3:F2} mm = {step / nominal.Grid.Spacing:F2} cells; "
            + $"worst relative error in dV/dplateX over {probes} probes: {worst:E3}");

        Assert.True(probes > 20, $"only {probes} probes; the sweep is not measuring much");
        Assert.True(
            worst < 5e-3,
            $"the shape derivative is off by {worst:E3} of its closed form, on a geometry whose derivative "
            + "is exactly -1000 x / L squared");
    }

    [Fact]
    public void GeometryLinearisationIsSecondOrderInThePerturbation()
    {
        // The FLD-1 spike §23 asks for, run again now that the boundary moves
        // continuously. The answer has changed shape completely.
        //
        // Before, there was no step size that worked: below one cell the residual
        // was exactly zero because nothing moved, above one cell it was
        // percent-level because a rasterised boundary moves in whole cells, and
        // the two failure modes met with nothing in between. The residual did not
        // even grow smoothly — it was a staircase.
        //
        // Now it is an ordinary Taylor remainder. The potential in the gap goes as
        // 1/L, so the second-order term is (delta/L) squared, and that is what the
        // measurement should show: quadratic in the perturbation, which means
        // halving the tolerance quarters the error and there is a step size for
        // any budget.
        output.WriteLine("half-width   delta/L    residual    ratio to previous   within 1 ppm");

        var results = new List<(double Delta, double Residual, bool Passed)>();

        foreach (var halfWidthMm in new[] { 0.05, 0.1, 0.2, 0.4 })
        {
            var channels = new[] { Channel("plateX", halfWidthMm, "mm") };
            var (nominal, fields) = SensitivityFields.Build(Model(), channels);
            var check = SensitivityFields.Check(Model(), channels, nominal, fields, budget: 1e-6, draws: 3);

            var ratio = results.Count == 0 ? double.NaN : check.WorstRelativeResidual / results[^1].Residual;

            output.WriteLine(
                $"{halfWidthMm,8:F2} mm   {halfWidthMm / 39.0,7:E1}   {check.WorstRelativeResidual,9:E3}   "
                + $"{ratio,17:F2}   {check.Passed}");

            results.Add((halfWidthMm / 39.0, check.WorstRelativeResidual, check.Passed));
        }

        // Doubling the perturbation should roughly quadruple the residual. A
        // staircase does not do that, and neither does a discretisation error that
        // has nothing to do with the parameter.
        for (var k = 1; k < results.Count; k++)
        {
            var ratio = results[k].Residual / results[k - 1].Residual;

            Assert.InRange(ratio, 3.0, 5.5);
        }

        // And the leading coefficient is the physics, not the mesh: the remainder
        // of 1/L about L is (delta/L) squared to leading order.
        var predicted = results[^1].Delta * results[^1].Delta;

        output.WriteLine(
            $"closed-form second-order term at the largest perturbation: {predicted:E3}, "
            + $"measured {results[^1].Residual:E3}");

        Assert.InRange(results[^1].Residual / predicted, 0.3, 3.0);
    }

    [Fact]
    public void AFixedChannelHasNoSensitivity()
    {
        var channels = new[] { Channel("applied", 0.0, "V") };
        var (_, fields) = SensitivityFields.Build(Model(), channels);

        Assert.All(fields[0].Derivative.Values.ToArray(), v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void APerturbationThatMovesTheMeshIsRefusedWithAnExplanation()
    {
        // The trap this guards, and it is not hypothetical: a domain whose extent
        // is itself the perturbed parameter keeps its interval count while
        // rescaling the spacing, so node k moves physically and differencing node
        // by node compares unrelated points. It reported a residual of 0.23 at a
        // perturbation of one part in a thousand before the check compared
        // spacing as well as counts.
        var document = Model();
        var solve = document.Fields![0].Solve!;

        var moving = document with
        {
            Fields =
            [
                document.Fields[0] with
                {
                    Solve = solve with { MaxX = new QuantityValue(0.0, "mm") { Expression = "plateX * 1.5" } },
                },
            ],
        };

        var failure = Assert.Throws<ArgumentException>(() =>
            SensitivityFields.Build(moving, [Channel("plateX", 6.0, "mm")]));

        Assert.Contains("moved the mesh", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SensitivityFieldsCostOneCampaignRatherThanOnePerDraw()
    {
        // The whole economic argument for FLD-1: build once, then every draw is a
        // weighted sum. Counted here as elapsed work rather than asserted in prose.
        var channels = new[] { Channel("plateX", 2.5, "mm"), Channel("applied", 50.0, "V") };
        var (nominal, fields) = SensitivityFields.Build(Model(), channels);

        var start = System.Diagnostics.Stopwatch.StartNew();

        for (var d = 0; d < 500; d++)
        {
            SensitivityFields.Linearise(nominal, fields, new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["plateX"] = 1e-3 * ((d % 7) - 3),
                ["applied"] = 10.0 * ((d % 5) - 2),
            });
        }

        var linearised = start.Elapsed;
        start.Restart();
        SensitivityFields.SolveAt(Model(), null);
        var oneSolve = start.Elapsed;

        output.WriteLine(
            $"500 linearised draws in {linearised.TotalMilliseconds:F1} ms; one solve is "
            + $"{oneSolve.TotalMilliseconds:F1} ms, so 500 solves would be about "
            + $"{oneSolve.TotalMilliseconds * 500 / 1000.0:F1} s");

        Assert.True(
            linearised < oneSolve * 500,
            "superposition over 500 draws should cost far less than 500 solves");
    }
}
