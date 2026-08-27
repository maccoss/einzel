using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Sweeps;
using Xunit.Abstractions;

namespace Einzel.Sweeps.Tests;

/// <summary>
/// The two derivative-free searches spec section 13 asks for, against functions
/// whose optima are known exactly.
/// </summary>
/// <remarks>
/// <para>
/// Analytic test functions rather than a device, deliberately. An optimiser tested
/// only on the problem it was written for passes by construction; the standard
/// functions are chosen precisely because each defeats a naive method in a
/// different way, and they cost microseconds rather than field solves.
/// </para>
/// <para>
/// The model here is a shell: parameters with bounds, and an objective that
/// ignores everything except the parameter values. That is not a cheat, it is the
/// seam being tested - the optimiser is supposed to know nothing about what it is
/// optimising, so an objective that is pure arithmetic exercises exactly the same
/// path a mirror does.
/// </para>
/// </remarks>
public sealed class OptimiserTests(ITestOutputHelper output)
{
    private static ModelDocument Model(params (string Name, double Value, double Minimum, double Maximum)[] parameters)
    {
        var declared = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal);

        foreach (var (name, value, minimum, maximum) in parameters)
        {
            declared[name] = new ParameterDocument
            {
                Value = value,
                Unit = "1",
                Minimum = minimum,
                Maximum = maximum,
            };
        }

        return new ModelDocument
        {
            SchemaVersion = "0.2",
            Name = "surrogate",
            Parameters = declared,
            Ion = new IonDocument { MassToCharge = new QuantityValue(500.0, "Da"), ChargeNumber = 1 },
            Source = new SourceDocument
            {
                Position = new VectorValue([0.0, 0.0, 0.0], "mm"),
                Direction = new DirectionValue([1.0, 0.0, 0.0]),
                AccelerationPotential = new QuantityValue(100.0, "V"),
            },
            Fields = [new FieldDocument { Type = "fieldFree" }],
            Detector = new DetectorDocument
            {
                PlanePoint = new VectorValue([100.0, 0.0, 0.0], "mm"),
                Normal = new DirectionValue([-1.0, 0.0, 0.0]),
            },
            Transport = new TransportDocument { MaximumFlightTime = new QuantityValue(1.0, "ms") },
        };
    }

    private static double At(CompiledModel model, string name) => model.Parameters[name].In("1");

    private static (double Value, Measured Envelope) Read(OptimisationResult result, string name)
    {
        var (value, _, _, _) = result.Best[name];
        return (value.In("1"), result.Best[name]);
    }

    [Theory]
    [InlineData(OptimisationAlgorithm.NelderMead)]
    [InlineData(OptimisationAlgorithm.CmaEs)]
    public void RosenbrockIsFoundInTwoDimensions(OptimisationAlgorithm algorithm)
    {
        // The standard hard case for a derivative-free method: a long curved
        // valley whose floor is nearly flat, with the minimum at (1, 1) inside it.
        // Getting into the valley is easy and following it to the end is not,
        // which is exactly the failure mode both of these are supposed to survive.
        var result = Optimiser.Run(
            Model(("x", -1.2, -3.0, 3.0), ("y", 1.0, -3.0, 3.0)),
            [new DesignVariable("x"), new DesignVariable("y")],
            model =>
            {
                var x = At(model, "x");
                var y = At(model, "y");
                return (100.0 * (y - (x * x)) * (y - (x * x))) + ((1.0 - x) * (1.0 - x));
            },
            ObjectiveSense.Minimise,
            algorithm,
            new OptimisationSettings { MaximumEvaluations = 4000, ParameterTolerance = 1e-6 });

        var (x, envelope) = Read(result, "x");
        var (y, _) = Read(result, "y");
        var (objective, _, _, _) = result.Objective;

        output.WriteLine($"{algorithm}: x = {x:F6}, y = {y:F6}, f = {objective.In("1"):E3}");
        output.WriteLine($"  {result.Evaluations} evaluations, {result.Iterations} iterations, converged {result.Converged}");
        output.WriteLine($"  envelope: {envelope}");

        Assert.Equal(1.0, x, 1e-3);
        Assert.Equal(1.0, y, 1e-3);
        Assert.True(objective.In("1") < 1e-6, $"objective {objective.In("1"):E3} is not at the floor of the valley");
    }

    [Theory]
    [InlineData(OptimisationAlgorithm.NelderMead)]
    [InlineData(OptimisationAlgorithm.CmaEs)]
    public void ASixDimensionalSphereConvergesFromACorner(OptimisationAlgorithm algorithm)
    {
        // Easy in shape, harder in count: six variables is past where a simplex is
        // comfortable and squarely where section 13 points at CMA-ES. The optimum
        // is deliberately not at the centre of any box, since a search started at
        // the nominal and stopping at the centre would pass a centred test without
        // doing anything.
        var offsets = new[] { 0.3, -0.7, 1.4, -1.9, 0.55, 2.2 };
        var names = Enumerable.Range(0, offsets.Length).Select(k => $"p{k}").ToArray();

        var result = Optimiser.Run(
            Model([.. names.Select(n => (n, -3.0, -4.0, 4.0))]),
            [.. names.Select(n => new DesignVariable(n))],
            model =>
            {
                var total = 0.0;

                for (var k = 0; k < names.Length; k++)
                {
                    var d = At(model, names[k]) - offsets[k];
                    total += d * d;
                }

                return total;
            },
            ObjectiveSense.Minimise,
            algorithm,
            new OptimisationSettings { MaximumEvaluations = 6000, ParameterTolerance = 1e-7 });

        var found = names.Select(n => Read(result, n).Value).ToArray();
        var worst = found.Select((v, k) => Math.Abs(v - offsets[k])).Max();

        output.WriteLine(
            $"{algorithm}: worst coordinate error {worst:E3} after {result.Evaluations} evaluations, "
            + $"converged {result.Converged}");

        Assert.True(worst < 1e-3, $"worst coordinate is off by {worst:E3}");
    }

    [Theory]
    [InlineData(OptimisationAlgorithm.NelderMead)]
    [InlineData(OptimisationAlgorithm.CmaEs)]
    public void MaximisingIsNotMinimisingWithASignError(OptimisationAlgorithm algorithm)
    {
        // The sense is a stated input rather than something a caller negates by
        // hand, because a sign error in an objective does not throw - it returns
        // the worst design in the box and looks like an answer. This is the test
        // that would fail if the sense were dropped anywhere along the path.
        var result = Optimiser.Run(
            Model(("x", 0.0, -5.0, 5.0)),
            [new DesignVariable("x")],
            model =>
            {
                var x = At(model, "x");
                return 10.0 - ((x - 2.0) * (x - 2.0));
            },
            ObjectiveSense.Maximise,
            algorithm,
            new OptimisationSettings { MaximumEvaluations = 2000 });

        var (x, _) = Read(result, "x");
        var (objective, _, _, _) = result.Objective;

        output.WriteLine($"{algorithm}: peak at x = {x:F6}, f = {objective.In("1"):F6}");

        // A thousandth rather than something tighter, and the looseness is the
        // physics of a smooth optimum rather than slack in the test. The objective
        // is quadratic about its peak, so a parameter offset of delta costs only
        // delta squared in objective: at the 1e-8 objective tolerance the
        // parameter is indistinguishable within about 1e-4 either way, and no
        // amount of searching recovers a digit the objective does not carry. It is
        // the same reason a first-order focus is a broad optimum in separation.
        Assert.Equal(2.0, x, 1e-3);

        // Reported in the caller's sense, not the minimiser's.
        Assert.Equal(10.0, objective.In("1"), 1e-6);
    }

    [Fact]
    public void AnOptimumOnItsBoundSaysSo()
    {
        // The most useful thing an optimiser can tell you and the easiest to miss.
        // The objective falls monotonically across the whole box, so what comes
        // back is the edge of the box - a perfectly good number that means
        // something entirely different from "the optimum".
        var result = Optimiser.Run(
            Model(("x", 0.0, -1.0, 1.0)),
            [new DesignVariable("x")],
            model => At(model, "x"),
            ObjectiveSense.Minimise,
            OptimisationAlgorithm.NelderMead,
            new OptimisationSettings { MaximumEvaluations = 500 });

        var (x, envelope) = Read(result, "x");

        output.WriteLine($"x = {x:F6}");

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"  {warning}");
        }

        Assert.Equal(-1.0, x, 1e-6);

        var atBound = Assert.Single(result.Warnings, w => w.Code == "optimiser.optimum-at-bound");
        Assert.False(atBound.IsSuppressible);
        Assert.Contains("x", atBound.Message, StringComparison.Ordinal);

        // GRD-2: the warning rides on the envelope, not beside it.
        Assert.True(envelope.HasNonSuppressibleWarnings);
    }

    [Fact]
    public void AnExhaustedBudgetIsNotSilentlyAnOptimum()
    {
        var result = Optimiser.Run(
            Model(("x", -1.2, -3.0, 3.0), ("y", 1.0, -3.0, 3.0)),
            [new DesignVariable("x"), new DesignVariable("y")],
            model =>
            {
                var x = At(model, "x");
                var y = At(model, "y");
                return (100.0 * (y - (x * x)) * (y - (x * x))) + ((1.0 - x) * (1.0 - x));
            },
            ObjectiveSense.Minimise,
            OptimisationAlgorithm.NelderMead,
            new OptimisationSettings { MaximumEvaluations = 12, ParameterTolerance = 1e-9 });

        output.WriteLine($"{result.Evaluations} evaluations, converged {result.Converged}");

        Assert.False(result.Converged);
        Assert.True(result.Evaluations <= 12 + 1, $"the budget was overspent: {result.Evaluations}");

        var exhausted = Assert.Single(result.Warnings, w => w.Code == "optimiser.budget-exhausted");
        Assert.False(exhausted.IsSuppressible);
    }

    [Fact]
    public void ADesignThatDoesNotWorkIsADataPointRatherThanACrash()
    {
        // A geometry that fails partway through a search is ordinary. Half the box
        // here returns nothing at all, and the minimum sits in the half that works,
        // just next to the boundary between them.
        var result = Optimiser.Run(
            Model(("x", 1.0, -2.0, 4.0)),
            [new DesignVariable("x")],
            model =>
            {
                var x = At(model, "x");
                return x < 0.5 ? null : (x - 0.6) * (x - 0.6);
            },
            ObjectiveSense.Minimise,
            OptimisationAlgorithm.NelderMead,
            new OptimisationSettings { MaximumEvaluations = 800 });

        var (x, _) = Read(result, "x");

        output.WriteLine($"x = {x:F6} with {result.Failures} of {result.Evaluations} evaluations failing");

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"  {warning}");
        }

        Assert.Equal(0.6, x, 1e-3);
        Assert.True(result.Failures > 0, "the failing half of the box was never visited, so nothing was tested");
        Assert.Single(result.Warnings, w => w.Code == "optimiser.failed-evaluations");
    }

    [Fact]
    public void AStartingPointThatDoesNotWorkIsRefusedUpFront()
    {
        // A search improves on something. If the starting design produces no
        // figure of merit there is nothing to improve on, and every candidate
        // scores the same, so the search wanders and returns whatever it touched
        // last - which looks exactly like an answer.
        //
        // This nearly shipped wrong. A failed evaluation is deliberately a large
        // finite number rather than an infinity, so the guard, which asked whether
        // the nominal came back finite, would never have fired. It has to ask
        // whether the evaluation was counted as a failure.
        var failure = Assert.Throws<Core.Errors.EinzelException>(() => Optimiser.Run(
            Model(("x", 1.0, -2.0, 4.0)),
            [new DesignVariable("x")],
            _ => null));

        output.WriteLine(failure.Error.ToString());

        Assert.Equal(Core.Errors.ErrorCodes.ConvergenceFailed, failure.Error.Code);
        Assert.Contains("starting design", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameSeedGivesTheSameSearch()
    {
        // PRJ-3: a run manifest fully determines its run. CMA-ES samples, so
        // without this the same study cannot be compared against itself.
        OptimisationResult Run(int seed) => Optimiser.Run(
            Model(("x", -1.2, -3.0, 3.0), ("y", 1.0, -3.0, 3.0)),
            [new DesignVariable("x"), new DesignVariable("y")],
            model =>
            {
                var x = At(model, "x");
                var y = At(model, "y");
                return (100.0 * (y - (x * x)) * (y - (x * x))) + ((1.0 - x) * (1.0 - x));
            },
            ObjectiveSense.Minimise,
            OptimisationAlgorithm.CmaEs,
            new OptimisationSettings { MaximumEvaluations = 600, Seed = seed });

        var first = Run(7);
        var again = Run(7);
        var different = Run(8);

        var (a, _, _, _) = first.Objective;
        var (b, _, _, _) = again.Objective;
        var (c, _, _, _) = different.Objective;

        output.WriteLine($"seed 7: {a.In("1"):E6} and {b.In("1"):E6}; seed 8: {c.In("1"):E6}");

        Assert.Equal(a.In("1"), b.In("1"));
        Assert.Equal(first.Evaluations, again.Evaluations);

        // Not a requirement of the algorithm, but if two seeds gave identical
        // results the seed would not be reaching the sampler at all.
        Assert.NotEqual(a.In("1"), c.In("1"));
    }

    [Fact]
    public void AVariableWithNoBoundIsRefusedRatherThanGuessedAt()
    {
        var model = new ModelDocument
        {
            SchemaVersion = "0.2",
            Name = "unbounded",
            Parameters = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
            {
                ["loose"] = new() { Value = 1.0, Unit = "1" },
            },
            Ion = new IonDocument { MassToCharge = new QuantityValue(500.0, "Da"), ChargeNumber = 1 },
            Source = new SourceDocument
            {
                Position = new VectorValue([0.0, 0.0, 0.0], "mm"),
                Direction = new DirectionValue([1.0, 0.0, 0.0]),
                AccelerationPotential = new QuantityValue(100.0, "V"),
            },
            Fields = [new FieldDocument { Type = "fieldFree" }],
            Detector = new DetectorDocument
            {
                PlanePoint = new VectorValue([100.0, 0.0, 0.0], "mm"),
                Normal = new DirectionValue([-1.0, 0.0, 0.0]),
            },
            Transport = new TransportDocument { MaximumFlightTime = new QuantityValue(1.0, "ms") },
        };

        var failure = Assert.Throws<Core.Errors.EinzelException>(() => Optimiser.Run(
            model, [new DesignVariable("loose")], _ => 1.0));

        output.WriteLine(failure.Error.ToString());

        Assert.Equal(Core.Errors.ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Contains("minimum", failure.Error.Constraint, StringComparison.Ordinal);

        // AGT-3: the error is a recovery instruction, so it has to say what to do.
        Assert.Contains("add a", failure.Error.Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public void ADerivedParameterCannotBeOptimisedDirectly()
    {
        var model = Model(("base", 2.0, 1.0, 5.0));

        var withDerived = model with
        {
            Parameters = new Dictionary<string, ParameterDocument>(model.Parameters!, StringComparer.Ordinal)
            {
                ["doubled"] = new() { Expression = "base * 2", Unit = "1" },
            },
        };

        var failure = Assert.Throws<Core.Errors.EinzelException>(() => Optimiser.Run(
            withDerived, [new DesignVariable("doubled", Quantity.From(1.0, "1"), Quantity.From(9.0, "1"))],
            _ => 1.0));

        output.WriteLine(failure.Error.ToString());

        // Varying a consequence is not varying anything: whatever it is derived
        // from would overwrite it on the next evaluation.
        Assert.Contains("derived", failure.Error.Constraint, StringComparison.Ordinal);
    }
}
