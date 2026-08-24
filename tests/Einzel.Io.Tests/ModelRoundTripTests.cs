using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Io;

namespace Einzel.Io.Tests;

public sealed class ModelRoundTripTests
{
    private const string Reflectron =
        """
        {
          "schemaVersion": "0.1",
          "name": "t",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-100, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 4, "unit": "kV" }
          },
          "fields": [{
            "type": "halfSpaceUniform",
            "planePoint": { "value": [0, 0, 0], "unit": "mm" },
            "inwardNormal": { "value": [1, 0, 0] },
            "capPotential": { "value": 4, "unit": "kV" },
            "turningDepth": { "value": 50, "unit": "mm" }
          }],
          "detector": {
            "planePoint": { "value": [-100, 0, 0], "unit": "mm" },
            "normal": { "value": [1, 0, 0] }
          },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
        }
        """;

    private static CompiledModel Compile(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json));
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.ToString())));
        return validation.Model!;
    }

    [Fact]
    public void CompilesQuantitiesToSi()
    {
        var model = Compile(Reflectron);

        Assert.Equal(-0.1, model.SourcePosition.X, 1e-15);
        Assert.Equal(4000.0, model.AccelerationPotentialSi, 1e-9);
        Assert.Equal(500 * 1.66053906892e-27, model.MassSi, 1e-37);
        Assert.Equal(1.602176634e-19, model.ChargeSi, 1e-30);

        // 4 kV over 50 mm.
        Assert.Equal(80000.0, model.Fields[0].PotentialGradientSi, 1e-6);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var document = ModelJson.Parse(Reflectron);
        var rewritten = ModelJson.Parse(ModelJson.Write(document));

        Assert.Equal(document, rewritten);
    }

    [Fact]
    public void DerivesLaunchSpeedFromEnergyRatherThanTakingItOnTrust()
    {
        var model = Compile(Reflectron);

        // v = sqrt(2qV/m). Stating a velocity in the document as well would let it
        // disagree with the energy, so it is derived and cannot.
        var expected = Math.Sqrt(2.0 * model.ChargeSi * 4000.0 / model.MassSi);
        Assert.Equal(expected, model.LaunchSpeedSi(), expected * 1e-12);
    }

    [Fact]
    public void ABareNumberWhereAQuantityBelongsIsRejected()
    {
        // Spec section 9: {"energy": 4000} is a validation error, on purpose.
        var json = Reflectron.Replace(
            "\"accelerationPotential\": { \"value\": 4, \"unit\": \"kV\" }",
            "\"accelerationPotential\": 4000",
            StringComparison.Ordinal);

        var failure = Assert.Throws<EinzelException>(() => ModelJson.Parse(json));
        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
    }

    [Fact]
    public void AUnitOfTheWrongDimensionNamesBothDimensions()
    {
        var json = Reflectron.Replace(
            "\"accelerationPotential\": { \"value\": 4, \"unit\": \"kV\" }",
            "\"accelerationPotential\": { \"value\": 4, \"unit\": \"mm\" }",
            StringComparison.Ordinal);

        var validation = ModelValidator.Validate(ModelJson.Parse(json));
        var error = Assert.Single(validation.Errors);

        Assert.Equal(ErrorCodes.UnitsIncompatible, error.Code);
        Assert.Equal("/source/accelerationPotential", error.Path);
        Assert.Equal("mm", error.Observed!.Unit);
        Assert.Contains(Dimension.ElectricPotential.ToString(), error.Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryErrorIsReportedNotJustTheFirst()
    {
        // AGT-3: the recovery an agent wants is the full list, not one unit per
        // round trip.
        var json = Reflectron
            .Replace("\"unit\": \"kV\" }", "\"unit\": \"mm\" }", StringComparison.Ordinal)
            .Replace("\"chargeNumber\": 1", "\"chargeNumber\": 0", StringComparison.Ordinal);

        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.True(validation.Errors.Count >= 2, $"expected several errors, got {validation.Errors.Count}");
        Assert.Contains(validation.Errors, e => e.Path == "/ion/chargeNumber");
    }

    [Fact]
    public void AnUnknownFieldTypeNamesThePermittedValues()
    {
        var json = Reflectron.Replace("\"halfSpaceUniform\"", "\"halfSpaceUnifrom\"", StringComparison.Ordinal);
        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        var error = Assert.Single(validation.Errors, e => e.Path == "/fields/0/type");
        Assert.Contains("halfSpaceUniform", error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void AskingForStatisticalDiffusionIsARegimeViolationNotASilentSubstitution()
    {
        // REG-1 makes the two transport modes peers. Only one exists in this
        // build, and saying so beats quietly running the other.
        var json = Reflectron.Replace("\"mode\": \"trajectory\"", "\"mode\": \"statisticalDiffusion\"", StringComparison.Ordinal);
        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        var error = Assert.Single(validation.Errors, e => e.Path == "/transport/mode");
        Assert.Equal(ErrorCodes.RegimeInvalid, error.Code);
    }

    [Fact]
    public void AMissingFlightTimeCeilingIsRejected()
    {
        var json = Reflectron.Replace(
            ", \"maximumFlightTime\": { \"value\": 1, \"unit\": \"ms\" }", string.Empty, StringComparison.Ordinal);

        var validation = ModelValidator.Validate(ModelJson.Parse(json));
        Assert.Contains(validation.Errors, e => e.Path == "/transport/maximumFlightTime");
    }

    [Fact]
    public void AnIonLaunchedBehindItsDetectorIsCaught()
    {
        // GRD-4: validity is checked, not assumed. This geometry produces a zero
        // flight time that reads like a physics answer.
        var json = Reflectron.Replace(
            "\"planePoint\": { \"value\": [-100, 0, 0], \"unit\": \"mm\" },\n    \"normal\"",
            "\"planePoint\": { \"value\": [50, 0, 0], \"unit\": \"mm\" },\n    \"normal\"",
            StringComparison.Ordinal);

        var validation = ModelValidator.Validate(ModelJson.Parse(json));
        Assert.Contains(validation.Errors, e => e.Path == "/source/position");
    }

    [Fact]
    public void DirectionsAreNormalisedRatherThanRequiredToBeUnitVectors()
    {
        var json = Reflectron.Replace(
            "\"direction\": { \"value\": [1, 0, 0] }",
            "\"direction\": { \"value\": [7, 0, 0] }",
            StringComparison.Ordinal);

        var model = Compile(json);
        Assert.Equal(1.0, model.SourceDirection.Length, 1e-15);
    }
}
