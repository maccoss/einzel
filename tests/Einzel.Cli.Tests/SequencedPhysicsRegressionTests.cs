using System.Globalization;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport.Collisions;

namespace Einzel.Cli.Tests;

public sealed class SequencedPhysicsRegressionTests
{
    private static SequencedOutcome Run(string text)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(text));
        Assert.True(validation.IsValid, string.Join(";", validation.Errors.Select(e => e.Constraint)));
        var model = validation.Model!;
        return SequencedRun.Execute(model, FieldAssembly.BuildReported(model).Field, BackgroundGas.FromModel(model.Gas));
    }

    private static string LongLeg => SequencedRunTests.Model.Replace(
        "\"value\": 1, \"unit\": \"us\"", "\"value\": 10, \"unit\": \"us\"", StringComparison.Ordinal);

    [Fact]
    public void ADeclaredGasSlowsTheTrajectoryLeg()
    {
        SequencedOutcome AtPressure(double pressure) => Run(LongLeg.Replace(
            "\"value\": 1e-6, \"unit\": \"mbar\"",
            $"\"value\": {pressure.ToString(CultureInfo.InvariantCulture)}, \"unit\": \"mbar\"", StringComparison.Ordinal));
        var vacuum = AtPressure(1e-12);
        var gas = AtPressure(1);
        Assert.InRange(vacuum.Phases[0].CentroidMm[0], 23, 25);
        Assert.InRange(gas.Phases[0].CentroidMm[0], 9.5, 11);
        Assert.Contains(gas.Warnings, warning => warning.Code == "collisions.sequence-leg");
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("pic")]
    public void SelfRepulsionActsDuringTheTrajectoryLeg(string method)
    {
        SequencedOutcome WithInteraction(string method) => Run(LongLeg
            .Replace("\"ions\": 200", "\"ions\": 20, \"population\": 100000000", StringComparison.Ordinal)
            .Replace("\"maximumFlightTime\":", $"\"spaceCharge\": \"{method}\", \"maximumFlightTime\":", StringComparison.Ordinal));
        var independent = WithInteraction("none");
        var pushed = WithInteraction(method);
        Assert.Equal(0, independent.Arrived);
        Assert.True(pushed.Arrived > 0, "the expanding packet must reach the detector during the first leg");
    }

    [Theory]
    [InlineData("[20, 0, 0]", "[-1, 0, 0]")]
    [InlineData("[40, 0, 0]", "[-1, 1, 0]")]
    public void AnUnrepresentableDiffusiveDetectorIsRefused(string point, string normal)
    {
        var text = SequencedRunTests.Model.Replace("[40, 0, 0]", point, StringComparison.Ordinal)
            .Replace("[-1, 0, 0]", normal, StringComparison.Ordinal);
        var error = Assert.Throws<EinzelException>(() => Run(text));
        Assert.Equal("/detector", error.Error.Path);
    }
}
