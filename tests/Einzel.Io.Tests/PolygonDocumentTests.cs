using Einzel.Core.Model;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// A polygon electrode in a model document: parametric vertices compile to SI with
/// a bounding box, and the outlines that would vanish or be ambiguous in a solve are
/// refused by name rather than solved.
/// </summary>
public sealed class PolygonDocumentTests(ITestOutputHelper output)
{
    private static string Model(string vertices) => $$"""
    {
      "schemaVersion": "0.3",
      "name": "polygon-under-test",
      "description": "A polygon plate in a grounded box.",
      "parameters": {
        "halfWidth": { "value": 3, "unit": "mm", "minimum": 0.1, "maximum": 8, "description": "Half the plate's width." },
        "height": { "value": 1, "unit": "mm", "minimum": 0, "maximum": 4, "description": "The plate's height." }
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
                "name": "plate", "shape": "polygon",
                "vertices": [ {{vertices}} ],
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

    private static string V(string x, string y) =>
        $$"""{ "x": { "expression": "{{x}}", "unit": "mm" }, "y": { "expression": "{{y}}", "unit": "mm" } }""";

    /// <summary>Vertices are expressions over the parameter surface and compile to a bounding box in SI.</summary>
    [Fact]
    public void ParametricVerticesCompile()
    {
        var json = Model(string.Join(", ",
            V("-halfWidth", "0"), V("halfWidth", "0"), V("halfWidth", "height"), V("-halfWidth", "height")));

        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var plate = validation.Model!.Fields[0].Solve!.Electrodes.Single();
        output.WriteLine($"{plate.Name}: {plate.Shape}, {plate.Vertices.Count} vertices, box x [{plate.MinX * 1e3:F2}, {plate.MaxX * 1e3:F2}] y [{plate.MinY * 1e3:F2}, {plate.MaxY * 1e3:F2}] mm");

        Assert.Equal(ElectrodeShape.Polygon, plate.Shape);
        Assert.Equal(4, plate.Vertices.Count);
        Assert.Equal(-3.0e-3, plate.MinX, 12);
        Assert.Equal(3.0e-3, plate.MaxX, 12);
        Assert.Equal(0.0, plate.MinY, 12);
        Assert.Equal(1.0e-3, plate.MaxY, 12);
        Assert.Equal(100.0, plate.Potential);
    }

    /// <summary>Fewer than three vertices enclose nothing and are refused at the vertices path.</summary>
    [Fact]
    public void TwoVerticesAreRefused()
    {
        var json = Model(string.Join(", ", V("-halfWidth", "0"), V("halfWidth", "0")));
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);

        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Path.EndsWith("/vertices", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Contains("three vertices", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Consecutive coincident vertices are merged rather than refused: a parametric
    /// outline produces them whenever a feature collapses, as a slot of zero height does.
    /// </summary>
    [Fact]
    public void ARepeatedVertexIsMerged()
    {
        var json = Model(string.Join(", ",
            V("-halfWidth", "0"), V("halfWidth", "0"), V("halfWidth", "0"), V("0", "height")));
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);

        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        var plate = validation.Model!.Fields[0].Solve!.Electrodes.Single();
        output.WriteLine($"{plate.Vertices.Count} distinct vertices from 4 declared");
        Assert.Equal(3, plate.Vertices.Count);

        // But merging cannot rescue an outline that was only ever one or two points.
        var collapsed = Model(string.Join(", ", V("0", "0"), V("0", "0"), V("halfWidth", "0")));
        var refused = ModelValidator.Validate(ModelJson.Parse(collapsed), null);
        Assert.Null(refused.Model);
        Assert.Contains(refused.Errors, e => e.Constraint.Contains("three distinct vertices", StringComparison.Ordinal));
    }

    /// <summary>
    /// A polygon whose height parameter has gone to zero has collapsed to a line. It
    /// would fix no node and vanish from the solve, so it is refused.
    /// </summary>
    [Fact]
    public void ZeroAreaIsRefused()
    {
        var json = Model(string.Join(", ", V("-halfWidth", "0"), V("0", "0"), V("halfWidth", "0")));
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);

        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Constraint.Contains("encloses no area", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
    }

    /// <summary>
    /// A bow-tie has no single inside and is refused naming the crossing edges. The
    /// bow-tie is asymmetric on purpose: a symmetric one has exactly zero signed area
    /// and is caught by the area check first, which is a different refusal.
    /// </summary>
    [Fact]
    public void ASelfCrossingOutlineIsRefused()
    {
        var json = Model(string.Join(", ",
            V("-halfWidth", "0"), V("halfWidth", "height"), V("halfWidth", "0"), V("-halfWidth", "2 * height")));
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);

        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Constraint.Contains("crosses itself", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
    }

    /// <summary>A vertex with a unit the grammar cannot read as a length is refused at its own path.</summary>
    [Fact]
    public void AVertexWithTheWrongDimensionIsRefusedAtItsOwnPath()
    {
        var bad = """{ "x": { "value": 1, "unit": "V" }, "y": { "value": 0, "unit": "mm" } }""";
        var json = Model(string.Join(", ", V("-halfWidth", "0"), V("halfWidth", "0"), bad));
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);

        Assert.Null(validation.Model);
        var error = Assert.Single(validation.Errors, e => e.Path.EndsWith("/vertices/2/x", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
    }
}
