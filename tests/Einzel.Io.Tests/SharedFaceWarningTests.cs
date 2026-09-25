using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Fields.Solved;

namespace Einzel.Io.Tests;

/// <summary>
/// A node on a face two disagreeing conductors share reaches every result computed
/// through the field, from a model document.
/// </summary>
/// <remarks>
/// <para>
/// GRD-2, at the seam every run, preview, study and figure passes through. The detector
/// lives in the geometry builders and hands its finding back on the <c>SolveReport</c>, so
/// this checks the part that has gone wrong here before: that nothing between the report
/// and the result drops it.
/// </para>
/// <para>
/// Declared from a document on purpose. A face written "2 mm" and a node computed as
/// origin plus ten spacings agree only to a rounding, which is the case the detector's
/// tolerance exists for - a unit test that took its face from the grid would be exact.
/// </para>
/// </remarks>
public sealed class SharedFaceWarningTests
{
    /// <summary>Two plates meeting at <paramref name="face"/> millimeters, in a 16 mm box at a 1 mm cell.</summary>
    private static string Model(string face) => $$"""
    {
      "schemaVersion": "0.3",
      "name": "abutting",
      "description": "Two plates at opposite potentials sharing a face, in a grounded box.",
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, -7, 0], "unit": "mm" },
        "direction": { "value": [0, 1, 0] },
        "accelerationPotential": { "value": 10, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved2d",
          "solve": {
            "minX": { "value": -8, "unit": "mm" },
            "minY": { "value": -8, "unit": "mm" },
            "maxX": { "value": 8, "unit": "mm" },
            "maxY": { "value": 8, "unit": "mm" },
            "cellSize": { "value": 1, "unit": "mm" },
            "tolerance": 1e-9,
            "electrodes": [
              {
                "name": "left", "shape": "rectangle",
                "minX": { "value": -4, "unit": "mm" },
                "minY": { "value": 2, "unit": "mm" },
                "maxX": { "value": {{face}}, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              },
              {
                "name": "right", "shape": "rectangle",
                "minX": { "value": {{face}}, "unit": "mm" },
                "minY": { "value": 2, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "potential": { "value": -100, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0, 7, 0], "unit": "mm" },
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
    public void AFaceOnAColumnOfNodesIsCarriedOntoTheResult()
    {
        var (_, warnings) = FieldAssembly.BuildReported(Compile(Model("2")));

        var warning = Assert.Single(warnings, w => w.Code == SharedFaceNodes.Code);

        Assert.Equal(WarningSeverity.Qualified, warning.Severity);
        Assert.False(warning.IsSuppressible);

        // Named by element, which the builder cannot do because it does not know which
        // element of the model it was handed.
        Assert.StartsWith("field element 0 (solved2d): 3 mesh nodes", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'left' and 'right'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSamePlatesAQuarterCellOffTheNodesEarnNothing()
    {
        var (_, warnings) = FieldAssembly.BuildReported(Compile(Model("2.25")));

        Assert.DoesNotContain(warnings, w => w.Code == SharedFaceNodes.Code);
    }

    [Fact]
    public void TheBareBuilderDoesNotRefuseIt()
    {
        // Qualified, not a violation: the field is a solution of a geometry within half a
        // cell of the declared one at three nodes, which a result can carry as a caveat.
        // Build refuses only what says the field is not the declared geometry's solution.
        Assert.NotNull(FieldAssembly.Build(Compile(Model("2"))));
    }
}
