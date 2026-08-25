using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Core.Tests.Model;

/// <summary>
/// The expression evaluator, whose job is as much dimensional checking as
/// arithmetic.
/// </summary>
public sealed class ExpressionTests
{
    private static readonly Dictionary<string, Quantity> Parameters = new(StringComparer.Ordinal)
    {
        ["depth"] = Quantity.From(90.0, "mm"),
        ["gap"] = Quantity.From(30.0, "mm"),
        ["volts"] = Quantity.From(4800.0, "V"),
        ["ratio"] = Quantity.Number(0.35),
    };

    private static Quantity Evaluate(string expression) =>
        ExpressionEvaluator.Evaluate(expression, Parameters, "/test");

    [Theory]
    [InlineData("depth", 0.090)]
    [InlineData("depth / 2", 0.045)]
    [InlineData("depth * ratio", 0.0315)]
    [InlineData("depth + gap", 0.120)]
    [InlineData("depth - gap", 0.060)]
    [InlineData("-depth", -0.090)]
    [InlineData("(depth + gap) / 2", 0.060)]
    [InlineData("depth * 2 - gap", 0.150)]
    [InlineData("abs(gap - depth)", 0.060)]
    [InlineData("min(depth, gap)", 0.030)]
    [InlineData("max(depth, gap)", 0.090)]
    [InlineData("1.5e-2 * depth", 0.00135)]
    public void EvaluatesArithmetic(string expression, double expectedMetres)
    {
        Assert.Equal(expectedMetres, Evaluate(expression).SiValue, Math.Abs(expectedMetres) * 1e-12 + 1e-18);
    }

    [Fact]
    public void MultiplicationBindsTighterThanAddition()
    {
        // gap + depth*ratio = 30 + 31.5 = 61.5 mm, not (gap+depth)*ratio = 42 mm.
        Assert.Equal(0.0615, Evaluate("gap + depth * ratio").SiValue, 1e-15);
    }

    [Fact]
    public void DimensionsPropagateThroughProducts()
    {
        // A gradient: volts per metre.
        var gradient = Evaluate("volts / depth");

        Assert.Equal(Dimension.ElectricField, gradient.Dimension);
        Assert.Equal(4800.0 / 0.090, gradient.SiValue, 1e-9);
    }

    [Fact]
    public void AddingIncompatibleDimensionsIsRefused()
    {
        // The check the evaluator exists for: this is caught where it is written,
        // not thousands of integration steps later.
        var failure = Assert.Throws<EinzelException>(() => Evaluate("depth + volts"));
        Assert.Equal(ErrorCodes.UnitsIncompatible, failure.Error.Code);
    }

    [Fact]
    public void SquareRootOfADimensionedQuantityIsRefused()
    {
        // The square root of a length has no representation in an integer-exponent
        // dimension system, and quietly dropping the dimension would defeat the
        // checking this evaluator is for.
        var failure = Assert.Throws<EinzelException>(() => Evaluate("sqrt(depth)"));
        Assert.Contains("dimensionless", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void SquareRootOfARatioIsFine()
    {
        Assert.Equal(Math.Sqrt(0.35), Evaluate("sqrt(ratio)").SiValue, 1e-12);
        Assert.Equal(Math.Sqrt(1.0 / 3.0), Evaluate("sqrt(gap / depth)").SiValue, 1e-12);
    }

    [Fact]
    public void AnUnknownNameListsWhatIsDeclared()
    {
        var failure = Assert.Throws<EinzelException>(() => Evaluate("depht * 2"));

        Assert.Equal("/test", failure.Error.Path);
        Assert.Contains("depth", failure.Error.Suggestion, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("depth +")]
    [InlineData("(depth")]
    [InlineData("depth 2")]
    [InlineData("* depth")]
    [InlineData("nosuchfunction(depth)")]
    [InlineData("min(depth)")]
    public void MalformedExpressionsAreRefused(string expression)
    {
        Assert.Throws<EinzelException>(() => Evaluate(expression));
    }

    [Fact]
    public void ReferencesFindParametersButNotFunctionNames()
    {
        var names = ExpressionEvaluator.References("max(depth, gap * ratio) + sqrt(one)");

        Assert.Contains("depth", names);
        Assert.Contains("gap", names);
        Assert.Contains("ratio", names);
        Assert.Contains("one", names);

        // Otherwise the dependency order would try to resolve 'max' as a parameter.
        Assert.DoesNotContain("max", names);
        Assert.DoesNotContain("sqrt", names);
    }

    [Theory]
    [InlineData("floor(2.7)", 2.0)]
    [InlineData("floor(-0.2)", -1.0)]
    [InlineData("mod(7, 3)", 1.0)]
    [InlineData("mod(8, 2)", 0.0)]
    [InlineData("1 - 2 * mod(3, 2)", -1.0)]
    [InlineData("1 - 2 * mod(4, 2)", 1.0)]
    public void FloorAndModMakeIndexedGeometryExpressible(string expression, double expected)
    {
        // What a repeated electrode needs. The alternating sign of a two-phase
        // stack is 1 - 2 mod(index, 2), and the position of the nth ring is
        // index * pitch - neither of which the grammar could say before.
        Assert.Equal(expected, Evaluate(expression).SiValue, 1e-12);
    }

    [Fact]
    public void ModCountsBackwardsTheWayAnIndexShould()
    {
        // Euclidean rather than truncated, so a negative index still alternates.
        // C# would give -1 here, which would make ring -1 the same phase as ring 1
        // rather than the opposite one.
        Assert.Equal(1.0, Evaluate("mod(-1, 2)").SiValue, 1e-12);
        Assert.Equal(0.0, Evaluate("mod(-2, 2)").SiValue, 1e-12);
    }

    [Fact]
    public void FloorRefusesADimensionedArgument()
    {
        // The floor of a length depends on which unit you take it in, which is
        // exactly the ambiguity the evaluator exists to refuse. Form a ratio first.
        var failure = Assert.Throws<Einzel.Core.Errors.EinzelException>(() => Evaluate("floor(gap)"));

        Assert.Contains("dimensionless", failure.Error.Constraint!, StringComparison.Ordinal);
    }

    [Fact]
    public void ModByZeroIsRefused()
    {
        Assert.Throws<Einzel.Core.Errors.EinzelException>(() => Evaluate("mod(3, 0)"));
    }
}
