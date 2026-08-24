using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Core.Tests.Model;

public sealed class ParameterSurfaceTests
{
    private static Dictionary<string, ParameterDocument> Declared() => new(StringComparer.Ordinal)
    {
        ["depth"] = new() { Value = 90.0, Unit = "mm", Minimum = 20.0, Maximum = 300.0, Description = "mirror depth" },
        ["fraction"] = new() { Value = 0.35, Unit = "1", Minimum = 0.0, Maximum = 0.9 },
        ["stageDepth"] = new() { Expression = "depth * fraction", Unit = "mm" },
        ["halfStage"] = new() { Expression = "stageDepth / 2", Unit = "mm" },
    };

    private static ParameterSurface Resolve(
        Dictionary<string, ParameterDocument> declared,
        IReadOnlyDictionary<string, Quantity>? overrides = null)
    {
        var errors = new List<EinzelError>();
        var surface = ParameterSurface.Resolve(declared, overrides, errors);

        Assert.True(surface is not null, string.Join("; ", errors.Select(e => e.ToString())));
        return surface!;
    }

    private static List<EinzelError> Failures(Dictionary<string, ParameterDocument> declared)
    {
        var errors = new List<EinzelError>();
        Assert.Null(ParameterSurface.Resolve(declared, null, errors));
        Assert.NotEmpty(errors);
        return errors;
    }

    [Fact]
    public void ResolvesDerivedParametersInDependencyOrder()
    {
        // halfStage depends on stageDepth depends on depth: declaration order in
        // the document must not matter.
        var surface = Resolve(Declared());

        Assert.Equal(0.090, surface["depth"].SiValue, 1e-15);
        Assert.Equal(0.0315, surface["stageDepth"].SiValue, 1e-15);
        Assert.Equal(0.01575, surface["halfStage"].SiValue, 1e-15);
    }

    [Fact]
    public void OnlyFreeParametersAreOfferedForVarying()
    {
        // An optimiser must not be handed a knob that is really a consequence of
        // another knob.
        var free = Resolve(Declared()).FreeParameters.Select(p => p.Name).ToArray();

        Assert.Equal(["depth", "fraction"], free);
    }

    [Fact]
    public void AnOverrideCarriesThroughEveryDerivedParameter()
    {
        // The property that makes a sweep meaningful.
        var surface = Resolve(Declared(), new Dictionary<string, Quantity>(StringComparer.Ordinal)
        {
            ["depth"] = Quantity.From(200.0, "mm"),
        });

        Assert.Equal(0.200, surface["depth"].SiValue, 1e-15);
        Assert.Equal(0.070, surface["stageDepth"].SiValue, 1e-15);
        Assert.Equal(0.035, surface["halfStage"].SiValue, 1e-15);
    }

    [Fact]
    public void AnOverrideOfTheWrongDimensionIsRefused()
    {
        var errors = new List<EinzelError>();

        var surface = ParameterSurface.Resolve(
            Declared(),
            new Dictionary<string, Quantity>(StringComparer.Ordinal) { ["depth"] = Quantity.From(5.0, "kV") },
            errors);

        Assert.Null(surface);
        Assert.Contains(errors, e => e.Code == ErrorCodes.UnitsIncompatible);
    }

    [Fact]
    public void BoundsAreCheckedNotClamped()
    {
        // A sweep that walks past a declared range has found something the
        // template author did not intend; clamping would hide it.
        var errors = new List<EinzelError>();

        var surface = ParameterSurface.Resolve(
            Declared(),
            new Dictionary<string, Quantity>(StringComparer.Ordinal) { ["depth"] = Quantity.From(500.0, "mm") },
            errors);

        Assert.Null(surface);

        var error = Assert.Single(errors, e => e.Path == "/parameters/depth");
        Assert.Equal(ErrorCodes.ValueOutOfBounds, error.Code);
        Assert.Equal(500.0, error.Observed!.Value, 1e-9);
    }

    [Fact]
    public void ACycleIsRefusedWithTheChainNamed()
    {
        var declared = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["a"] = new() { Expression = "b + 1", Unit = "mm" },
            ["b"] = new() { Expression = "c * 2", Unit = "mm" },
            ["c"] = new() { Expression = "a / 2", Unit = "mm" },
        };

        var error = Failures(declared).First(e => e.Constraint.Contains("cycle", StringComparison.Ordinal));
        Assert.Contains("->", error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterMustDeclareItsUnit()
    {
        var declared = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["depth"] = new() { Value = 90.0 },
        };

        Assert.Contains(Failures(declared), e => e.Constraint.Contains("unit", StringComparison.Ordinal));
    }

    [Fact]
    public void AParameterIsAValueOrAnExpressionNotBoth()
    {
        var declared = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["depth"] = new() { Value = 90.0, Expression = "1 + 1", Unit = "mm" },
        };

        Assert.Contains(Failures(declared), e => e.Constraint.Contains("not both", StringComparison.Ordinal));
    }

    [Fact]
    public void ADerivedParameterMustDeclareAUnitOfTheDimensionItProduces()
    {
        var declared = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["depth"] = new() { Value = 90.0, Unit = "mm" },
            ["wrong"] = new() { Expression = "depth * 2", Unit = "kV" },
        };

        var error = Assert.Single(Failures(declared), e => e.Path == "/parameters/wrong");
        Assert.Equal(ErrorCodes.UnitsIncompatible, error.Code);
    }

    [Fact]
    public void AnEmptyDeclarationResolvesToAnEmptySurface()
    {
        var errors = new List<EinzelError>();

        Assert.Empty(ParameterSurface.Resolve(null, null, errors)!.Parameters);
        Assert.Empty(errors);
    }
}
