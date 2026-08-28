using System.Text.Json;

using Einzel.Core.Model;

namespace Einzel.Cli.Tests;

/// <summary>
/// That the density's time discretisation is declarable, and refused where it would
/// mean nothing.
/// </summary>
/// <remarks>
/// The implicit scheme removes the Courant limit and charges Gauss-Seidel sweeps for
/// it, so whether it is worth using is a property of the model rather than of the
/// engine - which is exactly why it belongs in the document and not in a default.
/// </remarks>
public sealed class DensityStepSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-densitystep", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>The default is explicit, and it needs no block to say so.</summary>
    [Fact]
    public void TheDefaultIsExplicit()
    {
        var step = Compile("\"mode\": \"diffusion\"").DensityStep;

        Assert.Equal("explicit", step.Scheme);
        Assert.Equal(1.0, step.Gain);
        Assert.False(step.IsImplicit);
    }

    /// <summary>A declared scheme and gain reach the compiled model.</summary>
    [Fact]
    public void ADeclaredSchemeIsCarried()
    {
        var step = Compile(
            "\"mode\": \"diffusion\", "
            + "\"densityStep\": { \"scheme\": \"implicit\", \"gain\": 64 }").DensityStep;

        Assert.True(step.IsImplicit);
        Assert.Equal(64.0, step.Gain);
    }

    /// <summary>
    /// A block against a trajectory model is refused, not ignored.
    /// </summary>
    /// <remarks>
    /// Only a diffusive run has a density to step. Ignoring the block would leave an
    /// author who believes they configured the solver and a run that stepped ions one
    /// at a time - the shape of every silent-wrongness bug in this codebase's history.
    /// </remarks>
    [Fact]
    public void ABlockAgainstATrajectoryModelIsRefused()
    {
        var error = Assert.Single(
            Errors("\"mode\": \"trajectory\", \"densityStep\": { \"scheme\": \"implicit\" }"),
            e => e.Path == "/transport/densityStep");

        Assert.Contains("diffusive mode", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("trajectory", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// A gain against the explicit scheme is refused rather than quietly dropped.
    /// </summary>
    /// <remarks>
    /// The explicit scheme is bounded by its own stability limit and cannot take a
    /// longer step. Honouring the block while ignoring half of it would leave the
    /// author concluding the scheme is slow rather than that the request went nowhere.
    /// </remarks>
    [Fact]
    public void AGainAgainstTheExplicitSchemeIsRefused()
    {
        var error = Assert.Single(
            Errors(
                "\"mode\": \"diffusion\", "
                + "\"densityStep\": { \"scheme\": \"explicit\", \"gain\": 8 }"),
            e => e.Path == "/transport/densityStep/gain");

        Assert.Contains("stability limit", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("\"implicit\"", error.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>A gain below one buys nothing and costs everything.</summary>
    [Fact]
    public void AGainBelowOneIsRefused()
    {
        Assert.Contains(
            Errors(
                "\"mode\": \"diffusion\", "
                + "\"densityStep\": { \"scheme\": \"implicit\", \"gain\": 0.25 }"),
            e => e.Path == "/transport/densityStep/gain");
    }

    /// <summary>An unknown scheme names the ones this build has, and what they cost.</summary>
    [Fact]
    public void AnUnknownSchemeNamesBothAndTheirTrade()
    {
        var error = Assert.Single(
            Errors(
                "\"mode\": \"diffusion\", "
                + "\"densityStep\": { \"scheme\": \"crank-nicolson\" }"),
            e => e.Path == "/transport/densityStep/scheme");

        Assert.Contains("\"explicit\"", error.Suggestion, StringComparison.Ordinal);
        Assert.Contains("\"implicit\"", error.Suggestion, StringComparison.Ordinal);

        // AGT-3 wants a recovery instruction, and here the recovery depends on a trade
        // rather than on a spelling: the implicit scheme is not simply better.
        Assert.Contains("Courant", error.Suggestion, StringComparison.Ordinal);
        Assert.Contains("sweeps", error.Suggestion, StringComparison.Ordinal);
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
          "name": "density-step-schema",
          "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0.0, 0.0, 0.0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 100.0, "unit": "V" }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [50.0, 0.0, 0.0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            {{transport}},
            "maximumFlightTime": { "value": 1.0, "unit": "ms" },
            "mobility": { "zeroField": { "value": 2.0, "unit": "cm^2/(V s)" } },
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": 1.0, "unit": "mbar" },
              "temperature": { "value": 300.0, "unit": "K" },
              "mass": { "value": 28.0, "unit": "Da" },
              "crossSection": { "value": 2.5e-18, "unit": "m^2" }
            },
            "densityGrid": {
              "minX": { "value": 0.0, "unit": "mm" },
              "maxX": { "value": 50.0, "unit": "mm" },
              "minY": { "value": -5.0, "unit": "mm" },
              "maxY": { "value": 5.0, "unit": "mm" },
              "intervalsX": 64,
              "intervalsY": 16
            }
          }
        }
        """;

        var document = JsonSerializer.Deserialize<ModelDocument>(json, Io.ModelJson.Options)!;

        return ModelValidator.Validate(document);
    }
}
