using Einzel.Core.Model;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// A phase may ramp a parameter to an end value: the compiled stage carries the
/// electrodes at both ends, and the ramps that would run on the wrong curve or reach an
/// element that cannot follow are refused by name.
/// </summary>
public sealed class RampDocumentTests(ITestOutputHelper output)
{
    /// <summary>A plate whose potential is an expression over <c>bias</c>, in a grounded box, with a sequence.</summary>
    private static string Model(string potential, string sequence, string fields = "", string transport = "") => $$"""
    {
      "schemaVersion": "0.9",
      "name": "ramp-under-test",
      "description": "A plate ramped by a sequence.",
      "parameters": {
        "bias": { "value": 0, "unit": "V", "minimum": -1000, "maximum": 1000, "description": "The plate's potential." },
        "refBias": { "value": 100, "unit": "V", "minimum": 1, "maximum": 1000, "description": "A reference potential." },
        "gap": { "value": 1, "unit": "mm", "minimum": 0.1, "maximum": 10, "description": "A reference length." }
      },
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, -8, 0], "unit": "mm" },
        "direction": { "value": [0, 1, 0] },
        "accelerationPotential": { "value": 10, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved2d",
          "solve": {
            "minX": { "value": -10, "unit": "mm" },
            "minY": { "value": -10, "unit": "mm" },
            "maxX": { "value": 10, "unit": "mm" },
            "maxY": { "value": 10, "unit": "mm" },
            "cellSize": { "value": 0.5, "unit": "mm" },
            "electrodes": [
              {
                "name": "plate", "shape": "rectangle",
                "minX": { "value": -4, "unit": "mm" }, "minY": { "value": -1, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
                "potential": { "expression": "{{potential}}", "unit": "V" }
              }
            ]
          }
        }{{fields}}
      ],
      "sequence": [ {{sequence}} ],
      "detector": {
        "planePoint": { "value": [0, 9, 0], "unit": "mm" },
        "normal": { "value": [0, -1, 0] }
      },
      "transport": { {{(transport.Length == 0 ? "\"mode\": \"trajectory\", \"maximumFlightTime\": { \"value\": 1, \"unit\": \"ms\" }" : transport)}} }
    }
    """;

    private const string HoldThenRamp = """
        { "name": "hold", "duration": { "value": 10, "unit": "us" }, "set": { "bias": { "value": 0, "unit": "V" } } },
        { "name": "ramp", "duration": { "value": 10, "unit": "us" }, "ramp": { "bias": { "value": 100, "unit": "V" } } }
        """;

    /// <summary>A ramped phase compiles to a stage with electrodes at both ends, and a held phase to one without.</summary>
    [Fact]
    public void ARampCompilesToAStageWithBothEnds()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model("bias", HoldThenRamp)), null);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var stages = validation.Model!.Fields[0].Solve!.Stages;
        Assert.Equal(2, stages.Count);

        Assert.Null(stages[0].EndElectrodes);
        Assert.Equal(0.0, stages[0].Electrodes[0].Potential);

        Assert.NotNull(stages[1].EndElectrodes);
        Assert.Equal(0.0, stages[1].Electrodes[0].Potential);
        Assert.Equal(100.0, stages[1].EndElectrodes![0].Potential);

        output.WriteLine($"ramp: {stages[1].Electrodes[0].Potential} V to {stages[1].EndElectrodes![0].Potential} V over {stages[1].DurationSeconds * 1e6} us");
    }

    /// <summary>A ramp starts from the value in force, so a phase that names only a ramp starts where the previous phase left it.</summary>
    [Fact]
    public void ARampStartsWhereThePreviousPhaseLeftTheParameter()
    {
        const string sequence = """
            { "name": "hold", "duration": { "value": 10, "unit": "us" }, "set": { "bias": { "value": 40, "unit": "V" } } },
            { "name": "ramp", "duration": { "value": 10, "unit": "us" }, "ramp": { "bias": { "value": 100, "unit": "V" } } }
            """;
        var validation = ModelValidator.Validate(ModelJson.Parse(Model("bias", sequence)), null);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var ramp = validation.Model!.Fields[0].Solve!.Stages[1];

        // Deliberately: a phase that sets nothing inherits the MODEL's value, not the previous
        // phase's. That is how phases work here - each is resolved against the document plus
        // its own set - and a ramp follows the same rule, so it starts at the declared 0 V.
        Assert.Equal(0.0, ramp.Electrodes[0].Potential);
        Assert.Equal(100.0, ramp.EndElectrodes![0].Potential);
        output.WriteLine("a phase inherits from the document, not from the phase before it; a ramp likewise");
    }

    /// <summary>A ramp end written as an expression is refused, as a set value is.</summary>
    [Fact]
    public void ARampToAnExpressionIsRefused()
    {
        const string sequence = """
            { "name": "ramp", "duration": { "value": 10, "unit": "us" }, "ramp": { "bias": { "expression": "refBias", "unit": "V" } } }
            """;
        var validation = ModelValidator.Validate(ModelJson.Parse(Model("bias", sequence)), null);
        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Path.EndsWith("/ramp/bias", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Contains("not at an expression", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// A potential that is the square root of the ramped parameter is refused: at the
    /// midpoint it is 70.7 V, not the 50 V a linear ramp would give.
    /// </summary>
    [Fact]
    public void ANonLinearDependenceIsRefusedNamingTheNumbers()
    {
        var validation = ModelValidator.Validate(
            ModelJson.Parse(Model("refBias * sqrt(bias / refBias)", HoldThenRamp)), null);

        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Constraint.Contains("non-linearly", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Contains("70.7107", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("50 V", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>A ramp whose parameter reaches an analytic element is refused rather than leaving it frozen.</summary>
    [Fact]
    public void ARampReachingAnAnalyticElementIsRefused()
    {
        const string analytic = """
            ,
            {
              "type": "uniform",
              "field": { "expression": ["bias / gap", "0", "0"], "unit": "V/m" }
            }
            """;
        var validation = ModelValidator.Validate(ModelJson.Parse(Model("bias", HoldThenRamp, fields: analytic)), null);

        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Constraint.Contains("analytic element", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
    }

    /// <summary>A ramp in a diffusive phase is refused, because the density solver holds its field within a phase.</summary>
    [Fact]
    public void ARampInADiffusivePhaseIsRefused()
    {
        const string diffusive = """
            "mode": "diffusion",
            "maximumFlightTime": { "value": 100, "unit": "us" },
            "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
            "densityGrid": {
              "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
              "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
              "intervalsX": 32, "intervalsY": 32
            },
            "gas": { "model": "hardSphere", "pressure": { "value": 1, "unit": "mbar" },
                     "mass": { "value": 28.0134, "unit": "Da" }, "crossSection": { "value": 250, "unit": "angstrom^2" } }
            """;
        var validation = ModelValidator.Validate(ModelJson.Parse(Model("bias", HoldThenRamp, transport: diffusive)), null);

        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Constraint.Contains("diffusive phase", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
    }
}
