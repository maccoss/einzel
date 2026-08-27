using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;

namespace Einzel.Io.Tests;

/// <summary>
/// A solve that missed its tolerance has to be visible downstream.
/// </summary>
/// <remarks>
/// GRD-2: warnings propagate and are not suppressible above threshold. The seam
/// that assembles engine fields from a document used to discard the solve report
/// entirely, so a field that stopped short was indistinguishable from one that
/// converged - and the segmented quadrupole's transmission boundary sat at the
/// wrong working point for a whole revision behind exactly that gap. Fixing the
/// solve addressed the symptom; this covers the detection.
/// </remarks>
public sealed class FieldConvergenceWarningTests
{
    /// <summary>A quadrupole whose solve is given a tolerance it cannot reach.</summary>
    private static string Model(string tolerance) => $$"""
    {
      "schemaVersion": "0.3",
      "name": "strained",
      "description": "A ring in a grounded box, solved to a tolerance chosen by the test.",
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
            "cellSize": { "value": 0.4, "unit": "mm" },
            "tolerance": {{tolerance}},
            "electrodes": [
              {
                "name": "plate", "shape": "rectangle",
                "minX": { "value": -4, "unit": "mm" },
                "minY": { "value": -1, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" },
                "maxY": { "value": 1, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0, 9, 0], "unit": "mm" },
        "normal": { "value": [0, -1, 0] }
      },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
    }
    """;

    private static CompiledModel Compile(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);
        Assert.True(validation.IsValid, validation.IsValid ? string.Empty : validation.Errors[0].Constraint);
        return validation.Model!;
    }

    [Fact]
    public void AConvergedSolveEarnsNoWarning()
    {
        var (_, warnings) = FieldAssembly.BuildReported(Compile(Model("1e-9")));

        Assert.Empty(warnings);
    }

    [Fact]
    public void ASolveThatStopsShortIsCarriedAsAViolation()
    {
        // A tolerance below round-off: the residual stops falling long before it is
        // reached, the iteration stalls, and the solve returns not-converged. The
        // field it produces is perfectly usable-looking, and that is the point -
        // nothing about the returned object says otherwise.
        var (field, warnings) = FieldAssembly.BuildReported(Compile(Model("1e-30")));

        Assert.NotNull(field);

        var warning = Assert.Single(warnings);

        Assert.Equal("field.not-converged", warning.Code);
        Assert.Equal(WarningSeverity.ValidityViolation, warning.Severity);

        // GRD-3: a validity violation cannot be silenced.
        Assert.False(warning.IsSuppressible);

        Assert.Contains("field element 0", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBareBuilderRefusesRatherThanHidingIt()
    {
        // There is nowhere to attach a taint on a plain field, so the only honest
        // choices are to throw or to conceal. It used to conceal.
        var error = Assert.Throws<Core.Errors.EinzelException>(
            () => FieldAssembly.Build(Compile(Model("1e-30"))));

        Assert.Equal(Core.Errors.ErrorCodes.ConvergenceFailed, error.Error.Code);
    }
}
