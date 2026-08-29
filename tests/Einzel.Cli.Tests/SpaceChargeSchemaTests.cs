using System.Text.Json;

using Einzel.Core.Model;

namespace Einzel.Cli.Tests;

/// <summary>
/// That <c>"spaceCharge": "pic"</c> is reachable from a model document, configurable,
/// and refused where it would mean nothing.
/// </summary>
/// <remarks>
/// The method existed for a commit before it was declarable, which is a gap worth
/// having tests about: an engine capability nothing can ask for is not a capability.
/// </remarks>
public sealed class SpaceChargeSchemaTests
{
    /// <summary>The declared method reaches the compiled model.</summary>
    [Fact]
    public void PicIsAccepted()
    {
        var model = Compile("\"spaceCharge\": \"pic\"");

        Assert.Equal("pic", model.SpaceChargeMode);
        Assert.True(model.ModelsSpaceCharge);
    }

    /// <summary>
    /// Asking for it without a grid block gets the documented defaults rather than
    /// null.
    /// </summary>
    /// <remarks>
    /// A null grid here would reach <c>FlyTogether</c> and be defaulted there instead,
    /// which is the same numbers written twice - and the run's provenance line would
    /// have nothing to report the approximation from.
    /// </remarks>
    [Fact]
    public void PicWithoutAGridBlockGetsDefaults()
    {
        var grid = Compile("\"spaceCharge\": \"pic\"").SpaceChargeGrid;

        Assert.NotNull(grid);
        Assert.Equal(32, grid.Nodes);
        Assert.Equal(4.0, grid.Padding);
        Assert.Equal(0.05, grid.RefreshTolerance);
    }

    /// <summary>A declared grid overrides each default independently.</summary>
    [Fact]
    public void AGridBlockIsCarried()
    {
        var grid = Compile(
            "\"spaceCharge\": \"pic\", \"spaceChargeGrid\": { \"nodes\": 64, \"padding\": 3.0 }")
            .SpaceChargeGrid;

        Assert.NotNull(grid);
        Assert.Equal(64, grid.Nodes);
        Assert.Equal(3.0, grid.Padding);

        // Untouched, so still the default rather than zero.
        Assert.Equal(0.05, grid.RefreshTolerance);
    }

    /// <summary>
    /// A grid declared against a method that cannot use one is refused, not ignored.
    /// </summary>
    /// <remarks>
    /// This is the case the whole block exists to catch. Ignoring it would leave a
    /// document that says it configured a solve, an author who believes it did, and a
    /// run computed by the pairwise sum - which is the shape of every silent-wrongness
    /// bug in this codebase's history.
    /// </remarks>
    [Theory]
    [InlineData("direct")]
    [InlineData("none")]
    public void AGridAgainstAnotherMethodIsRefused(string method)
    {
        var errors = Errors(
            $"\"spaceCharge\": \"{method}\", \"spaceChargeGrid\": {{ \"nodes\": 64 }}");

        var error = Assert.Single(errors, e => e.Path == "/transport/spaceChargeGrid");

        Assert.Contains("particle-in-cell", error.Constraint, StringComparison.Ordinal);
        Assert.Contains(method, error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>Each grid number has a bound, and each names its own path.</summary>
    [Theory]
    [InlineData("\"nodes\": 4", "/transport/spaceChargeGrid/nodes")]
    [InlineData("\"padding\": 1.0", "/transport/spaceChargeGrid/padding")]
    [InlineData("\"refreshTolerance\": 0.0", "/transport/spaceChargeGrid/refreshTolerance")]
    public void AnUnusableGridNumberIsRefusedByPath(string property, string path)
    {
        var errors = Errors($"\"spaceCharge\": \"pic\", \"spaceChargeGrid\": {{ {property} }}");

        Assert.Contains(errors, e => e.Path == path);
    }

    /// <summary>
    /// An unrecognised method still names the ones this build has, all of them.
    /// </summary>
    [Fact]
    public void AnUnknownMethodSuggestsEveryKnownOne()
    {
        var error = Assert.Single(
            Errors("\"spaceCharge\": \"pretty please\""),
            e => e.Path == "/transport/spaceCharge");

        Assert.Contains("\"none\"", error.Suggestion, StringComparison.Ordinal);
        Assert.Contains("\"direct\"", error.Suggestion, StringComparison.Ordinal);
        Assert.Contains("\"pic\"", error.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gas refusal names the method the document asked for, not one of them.
    /// </summary>
    /// <remarks>
    /// It read "the direct space-charge method" for every method, which was true when
    /// there was one. AGT-3 wants an error a reader can act on, and being told about a
    /// method they did not ask for is the opposite.
    /// </remarks>
    [Fact]
    public void TheGasRefusalNamesTheMethodAsked()
    {
        var errors = Errors(
            "\"spaceCharge\": \"pic\", "
            + "\"gas\": { \"pressure\": { \"value\": 1e-3, \"unit\": \"mbar\" }, "
            + "\"temperature\": { \"value\": 300, \"unit\": \"K\" }, \"model\": \"hardSphere\", "
            + "\"mass\": { \"value\": 28.0, \"unit\": \"Da\" }, "
            + "\"crossSection\": { \"value\": 1e-18, \"unit\": \"m^2\" } }");

        Assert.True(
            errors.Any(e => e.Constraint.Contains("'pic'", StringComparison.Ordinal)),
            string.Join("; ", errors.Select(e => $"{e.Path}: {e.Constraint}")));
    }

    private static CompiledModel Compile(string transport)
    {
        var (model, errors) = Validate(transport);

        Assert.True(
            errors.Count == 0,
            string.Join("; ", errors.Select(e => $"{e.Path}: {e.Constraint}")));

        Assert.NotNull(model);

        return model;
    }

    private static IReadOnlyList<Einzel.Core.Errors.EinzelError> Errors(string transport) =>
        Validate(transport).Errors;

    private static ModelValidation Validate(string transport)
    {
        var json = $$"""
        {
          "schemaVersion": "0.5",
          "name": "space-charge-schema",
          "ion": {
            "massToCharge": { "value": 500.0, "unit": "Da" },
            "chargeNumber": 1
          },
          "source": {
            "position": { "value": [0.0, 0.0, 0.0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 100.0, "unit": "V" },
            "cloud": {
              "ions": 8,
              "seed": 1,
              "transverseSpread": { "value": 0.1, "unit": "mm" }
            }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [50.0, 0.0, 0.0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "maximumFlightTime": { "value": 1.0, "unit": "ms" },
            {{transport}}
          }
        }
        """;

        var document = JsonSerializer.Deserialize<ModelDocument>(
            json, Io.ModelJson.Options)!;

        return ModelValidator.Validate(document);
    }
}
