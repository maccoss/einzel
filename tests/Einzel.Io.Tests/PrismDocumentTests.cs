using Einzel.Core.Model;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// A prism in a model document: an outline given a length along one axis, with the same
/// vertex runs a polygon takes, and the refusals a volume electrode has.
/// </summary>
public sealed class PrismDocumentTests(ITestOutputHelper output)
{
    private static string Model(string electrode) => $$"""
    {
      "schemaVersion": "0.9",
      "name": "prism-under-test",
      "description": "A prism in a grounded box.",
      "parameters": {
        "r0": { "value": 4, "unit": "mm", "minimum": 1, "maximum": 10, "description": "Inscribed radius." },
        "halfWidth": { "value": 6, "unit": "mm", "minimum": 1, "maximum": 10, "description": "Face half-width." },
        "sectionLength": { "value": 12, "unit": "mm", "minimum": 1, "maximum": 50, "description": "Rod section length." }
      },
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, 0, -20], "unit": "mm" },
        "direction": { "value": [0, 0, 1] },
        "accelerationPotential": { "value": 10, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved3d",
          "solve3d": {
            "minX": { "value": -14, "unit": "mm" }, "maxX": { "value": 14, "unit": "mm" },
            "minY": { "value": -14, "unit": "mm" }, "maxY": { "value": 14, "unit": "mm" },
            "minZ": { "value": -25, "unit": "mm" }, "maxZ": { "value": 25, "unit": "mm" },
            "cellSize": { "value": 1.0, "unit": "mm" },
            "electrodes": [ {{electrode}} ]
          }
        }
      ],
      "detector": { "planePoint": { "value": [0, 0, 24], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 100, "unit": "us" } }
    }
    """;

    private const string HyperbolicRod = """
        {
          "name": "rodYPlus", "shape": "prism", "axis": "z",
          "lower": { "expression": "-sectionLength / 2", "unit": "mm" },
          "upper": { "expression": "sectionLength / 2", "unit": "mm" },
          "vertices": [
            { "count": { "value": 25, "unit": "1" }, "index": "k",
              "x": { "expression": "-halfWidth + 2 * halfWidth * k / 24", "unit": "mm" },
              "y": { "expression": "r0 * sqrt(1 + ((-halfWidth + 2 * halfWidth * k / 24) / r0) * ((-halfWidth + 2 * halfWidth * k / 24) / r0))", "unit": "mm" } },
            { "x": { "expression": "halfWidth", "unit": "mm" }, "y": { "value": 12, "unit": "mm" } },
            { "x": { "expression": "-halfWidth", "unit": "mm" }, "y": { "value": 12, "unit": "mm" } }
          ],
          "potential": { "value": 100, "unit": "V" }
        }
        """;

    /// <summary>A hyperbolic rod section compiles: 27 vertices on and behind the hyperbola, the declared length, bounds to match.</summary>
    [Fact]
    public void AHyperbolicRodSectionCompiles()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(HyperbolicRod)), null);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        var rod = validation.Model!.Fields[0].Solve3D!.Electrodes.Single();
        output.WriteLine($"{rod.Name}: {rod.Shape} along {rod.Axis}, {rod.Vertices.Count} vertices, z [{rod.Lower * 1e3:F1}, {rod.Upper * 1e3:F1}] mm, bounds x [{rod.Bounds.MinX * 1e3:F1}, {rod.Bounds.MaxX * 1e3:F1}] y [{rod.Bounds.MinY * 1e3:F1}, {rod.Bounds.MaxY * 1e3:F1}]");

        Assert.Equal(Electrode3DShape.Prism, rod.Shape);
        Assert.Equal(CylinderAxis.Z, rod.Axis);
        Assert.Equal(27, rod.Vertices.Count);
        Assert.Equal(-6.0e-3, rod.Lower, 12);
        Assert.Equal(6.0e-3, rod.Upper, 12);

        foreach (var (x, y) in rod.Vertices.Take(25))
        {
            Assert.Equal(16.0e-6, (y * y) - (x * x), 12);   // r0^2 = 16 mm^2
        }

        // The vertex of the face is 4 mm from the axis, and the rod is not there beyond its ends.
        Assert.Equal(4.0e-3, rod.SignedDistance(0.0, 0.0, 0.0), 9);
        Assert.True(rod.Contains(0.0, 5.0e-3, 0.0));
        Assert.False(rod.Contains(0.0, 5.0e-3, 7.0e-3));
        Assert.Equal(1.0e-3, rod.SignedDistance(0.0, 5.0e-3, 7.0e-3), 12);
    }

    /// <summary>A prism may not be tilted; write the tilt into the outline.</summary>
    [Fact]
    public void ATiltedPrismIsRefused()
    {
        var tilted = HyperbolicRod.Replace("\"axis\": \"z\",", "\"axis\": \"z\", \"tiltHalfTurns\": { \"value\": 0.01, \"unit\": \"1\" },", StringComparison.Ordinal);
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(tilted)), null);
        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Constraint.Contains("only a box may be tilted", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
    }

    /// <summary>An outline with two vertices is refused for a prism as for a polygon.</summary>
    [Fact]
    public void ADegenerateOutlineIsRefused()
    {
        const string line = """
            {
              "name": "line", "shape": "prism",
              "lower": { "value": -1, "unit": "mm" }, "upper": { "value": 1, "unit": "mm" },
              "vertices": [ { "x": { "value": 0, "unit": "mm" }, "y": { "value": 0, "unit": "mm" } }, { "x": { "value": 1, "unit": "mm" }, "y": { "value": 0, "unit": "mm" } } ],
              "potential": { "value": 1, "unit": "V" }
            }
            """;
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(line)), null);
        Assert.Null(validation.Model);
        Assert.Contains(validation.Errors, e => e.Constraint.Contains("at least three", StringComparison.Ordinal));
    }
}
