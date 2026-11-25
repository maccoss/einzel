using System.Text.Json.Nodes;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Io;

namespace Einzel.Cli.Tests;

/// <summary>
/// A stage may change what an analytic element holds, not where it is: a phase that moves a region's
/// bounds or a half-space's plane is refused at validation, naming the element and the phase.
/// </summary>
/// <remarks>
/// A region is compiled once off the base surface and an analytic element's discontinuity members
/// are read off its first state, so a phase moving either left the surfaces where they were while
/// the values moved: a run landing on a plane no longer there and straddling the one that is, on a
/// document that validated cleanly.
/// </remarks>
public sealed class StageGeometryTests
{
    private static JsonObject Example(string name) => JsonNode.Parse(ExampleModels.Read(name))!.AsObject();

    private static ModelValidation Validate(JsonObject document) =>
        ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

    private static JsonObject Gap(double phaseEdgeMm)
    {
        var document = Example("bounded-accelerating-gap");
        document["schemaVersion"] = "0.15";
        document["parameters"] = JsonNode.Parse("""{ "edge": { "value": 20, "unit": "mm", "description": "the far face" } }""");
        document["fields"]![0]!["region"]!["maxX"] = JsonNode.Parse("""{ "expression": "edge" }""");
        document["sequence"] = JsonNode.Parse(
            $$"""
            [
              { "name": "first", "duration": { "value": 1, "unit": "us" } },
              { "name": "moved", "duration": { "value": 49, "unit": "us" },
                "set": { "edge": { "value": {{phaseEdgeMm}}, "unit": "mm" } } }
            ]
            """);
        return document;
    }

    private static JsonObject Mirror(string set)
    {
        var document = Example("reflectron-heavy-ion");
        document["schemaVersion"] = "0.15";
        document["parameters"] = JsonNode.Parse(
            """
            {
              "plane": { "value": 0, "unit": "mm", "description": "where the mirror starts" },
              "cap": { "value": 4, "unit": "kV", "description": "what the mirror holds" }
            }
            """);
        document["fields"]![0]!["planePoint"] = JsonNode.Parse("""{ "expression": ["plane", "0", "0"] }""");
        document["fields"]![0]!["capPotential"] = JsonNode.Parse("""{ "expression": "cap" }""");
        document["sequence"] = JsonNode.Parse(
            $$"""
            [
              { "name": "first", "duration": { "value": 1, "unit": "us" } },
              { "name": "second", "duration": { "value": 49, "unit": "us" }, "set": {{set}} }
            ]
            """);
        return document;
    }

    [Fact]
    public void APhaseMovingARegionIsRefused()
    {
        Assert.True(Validate(Gap(20.0)).IsValid, "the same document with the bound unmoved validates");

        var validation = Validate(Gap(25.0));

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Errors,
            e => e.Path == "/sequence/1"
                && e.Constraint.Contains("'moved'", StringComparison.Ordinal)
                && e.Constraint.Contains("its region", StringComparison.Ordinal)
                && e.Constraint.Contains("/fields/0", StringComparison.Ordinal));
    }

    [Fact]
    public void APhaseMovingAHalfSpacesPlaneIsRefusedAndOneChangingWhatItHoldsIsNot()
    {
        // What the mirror holds may change from phase to phase.
        Assert.True(Validate(Mirror("""{ "cap": { "value": 3, "unit": "kV" } }""")).IsValid);

        // Where it is may not.
        var validation = Validate(Mirror("""{ "plane": { "value": 5, "unit": "mm" } }"""));

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Errors,
            e => e.Path == "/sequence/1"
                && e.Constraint.Contains("'second'", StringComparison.Ordinal)
                && e.Constraint.Contains("its plane", StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether a phase moves an element is asked of the compile the per-phase loop already makes:
    /// each phase adds one compile of the element, not two.
    /// </summary>
    /// <remarks>
    /// The check compiled the element off every phase's surfaces and then the loop compiled it off
    /// the same surfaces again, so validating a sequence of N phases compiled a half-space 2N + 1
    /// times. Counted as the difference one phase more makes, so whatever else validation compiles
    /// cancels.
    /// </remarks>
    [Fact]
    public void EachPhaseCompilesTheElementOnce()
    {
        int Compiles(int phases)
        {
            var document = Mirror("""{ "cap": { "value": 3, "unit": "kV" } }""");
            var sequence = document["sequence"]!.AsArray();

            while (sequence.Count < phases)
            {
                sequence.Add(JsonNode.Parse(
                    $$"""{ "name": "more{{sequence.Count}}", "duration": { "value": 1, "unit": "us" }, "set": { "cap": { "value": {{sequence.Count}}, "unit": "kV" } } }"""));
            }

            var before = ModelValidator.AnalyticCompiles;
            Assert.True(Validate(document).IsValid);
            return ModelValidator.AnalyticCompiles - before;
        }

        Assert.Equal(1, Compiles(3) - Compiles(2));
        Assert.Equal(1, Compiles(4) - Compiles(3));
    }
}
