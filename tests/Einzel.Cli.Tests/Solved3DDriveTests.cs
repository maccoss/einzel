using System.Text.Json;

using Einzel.Core.Model;

namespace Einzel.Cli.Tests;

/// <summary>
/// That a three-dimensional solve can be operated by more than one generator.
/// </summary>
/// <remarks>
/// <para>
/// It could not, and the gap was one-sided: <c>CompiledSolvedField3D</c>,
/// <c>Geometry3D</c> and the three-dimensional builder all carried a list of drives from
/// the start, while the document spelled a single <c>drive</c>. So a volume geometry
/// could not express what a cross-section already could - a trap whose ring carries the
/// main drive while its endcaps carry a supplementary excitation, or a guide superposing
/// a fast confining field on a slow travelling wave.
/// </para>
/// <para>
/// <b>Shared with the two-dimensional path rather than reimplemented.</b> Both electrode
/// documents implement one interface and the tap validation is one function, so the
/// refusals below were not written twice - they arrived in three dimensions by being the
/// same code. A computation copied across a seam is how a declared gas came to take part
/// in a run and not in a figure of merit.
/// </para>
/// </remarks>
public sealed class Solved3DDriveTests
{
    /// <summary>Two generators on one geometry compile, and each electrode taps both.</summary>
    [Fact]
    public void AVolumeGeometryCanDeclareTwoGenerators()
    {
        var model = Compile(Plates(
            drives: """
            "drives": [
              { "name": "main", "frequency": { "value": 1.0, "unit": "MHz" }, "waveform": "sinusoid" },
              { "name": "aux",  "frequency": { "value": 0.1, "unit": "MHz" }, "waveform": "sinusoid" }
            ],
            """,
            lowerTaps: """
            "taps": [
              { "drive": "main", "amplitude": { "value": 200.0, "unit": "V" } },
              { "drive": "aux",  "amplitude": { "value": 50.0, "unit": "V" },
                "phase": { "value": 0.25, "unit": "1" } }
            ],
            """,
            upperTaps: """
            "taps": [
              { "drive": "main", "amplitude": { "value": -200.0, "unit": "V" } },
              { "drive": "aux",  "amplitude": { "value": -50.0, "unit": "V" },
                "phase": { "value": 0.25, "unit": "1" } }
            ],
            """));

        var solve = model.Fields[0].Solve3D;

        Assert.NotNull(solve);
        Assert.Equal(2, solve.Drives.Count);

        foreach (var electrode in solve.Electrodes)
        {
            Assert.Equal(2, electrode.Taps.Count);
        }

        // The two generators reach the same electrodes in the same proportions, so they
        // are one spatial pattern carrying two weights on two clocks - which is what
        // makes a second generator nearly free rather than a second solve.
        Assert.Equal(1.0e6, solve.Drives[0].FrequencyHz, 1e-6);
        Assert.Equal(1.0e5, solve.Drives[1].FrequencyHz, 1e-6);

        // Named, because a tap names the generator it is a tap on. An unnamed second
        // generator would leave the electrodes referring to nothing.
        Assert.Equal("main", solve.Drives[0].Name);
        Assert.Equal("aux", solve.Drives[1].Name);
    }

    /// <summary>Declaring one drive and several is refused rather than merged.</summary>
    /// <remarks>
    /// A document that says a geometry has one generator and also two has no default to
    /// fall back on. The refusal is the two-dimensional one, reached by the same code.
    /// </remarks>
    [Fact]
    public void DeclaringBothFormsIsRefused()
    {
        var errors = Errors(Plates(
            drives: """
            "drive": { "frequency": { "value": 1.0, "unit": "MHz" }, "waveform": "sinusoid" },
            "drives": [
              { "name": "main", "frequency": { "value": 1.0, "unit": "MHz" }, "waveform": "sinusoid" }
            ],
            """,
            lowerTaps: OneTap,
            upperTaps: Grounded));

        Assert.Contains(
            errors,
            e => e.Constraint.Contains("'drive' or 'drives', not both", StringComparison.Ordinal));
    }

    /// <summary>An electrode declaring both tap forms is refused too.</summary>
    [Fact]
    public void AnElectrodeDeclaringBothTapFormsIsRefused()
    {
        var errors = Errors(Plates(
            drives: """
            "drives": [
              { "name": "main", "frequency": { "value": 1.0, "unit": "MHz" }, "waveform": "sinusoid" }
            ],
            """,
            lowerTaps: """
            "driveAmplitude": { "value": 100.0, "unit": "V" },
            "taps": [ { "drive": "main", "amplitude": { "value": 10.0, "unit": "V" } } ],
            """,
            upperTaps: Grounded));

        Assert.Contains(
            errors,
            e => e.Constraint.Contains("'driveAmplitude' or 'taps', not both", StringComparison.Ordinal));
    }

    /// <summary>One tap on the single declared generator.</summary>
    private const string OneTap =
        "\"taps\": [ { \"drive\": \"main\", \"amplitude\": { \"value\": 10.0, \"unit\": \"V\" } } ],";

    /// <summary>An electrode taking part in no generator, so it declares no taps.</summary>
    private const string Grounded = "";

    private static CompiledModel Compile(string json)
    {
        var (model, errors) = Validate(json);

        Assert.True(
            errors.Count == 0,
            string.Join("; ", errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return model!;
    }

    private static IReadOnlyList<Einzel.Core.Errors.EinzelError> Errors(string json) =>
        Validate(json).Errors;

    private static ModelValidation Validate(string json)
    {
        var document = JsonSerializer.Deserialize<ModelDocument>(json, Io.ModelJson.Options)!;

        return ModelValidator.Validate(document);
    }

    private static string Plates(string drives, string lowerTaps, string upperTaps) => $$"""
    {
      "schemaVersion": "0.5",
      "name": "two-generator-volume",
      "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0.0, 0.0, 0.0], "unit": "mm" },
        "direction": { "value": [0, 0, 1] },
        "accelerationPotential": { "value": 10.0, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved3d",
          "solve3d": {
            {{drives}}
            "minX": { "value": -6.0, "unit": "mm" },
            "minY": { "value": -6.0, "unit": "mm" },
            "minZ": { "value": -6.0, "unit": "mm" },
            "maxX": { "value": 6.0, "unit": "mm" },
            "maxY": { "value": 6.0, "unit": "mm" },
            "maxZ": { "value": 6.0, "unit": "mm" },
            "cellSize": { "value": 1.0, "unit": "mm" },
            "electrodes": [
              {
                "name": "lower", "shape": "box",
                "minX": { "value": -4.0, "unit": "mm" },
                "minY": { "value": -4.0, "unit": "mm" },
                "minZ": { "value": -4.0, "unit": "mm" },
                "maxX": { "value": 4.0, "unit": "mm" },
                "maxY": { "value": 4.0, "unit": "mm" },
                "maxZ": { "value": -3.0, "unit": "mm" },
                {{lowerTaps}}
                "potential": { "value": 0.0, "unit": "V" }
              },
              {
                "name": "upper", "shape": "box",
                "minX": { "value": -4.0, "unit": "mm" },
                "minY": { "value": -4.0, "unit": "mm" },
                "minZ": { "value": 3.0, "unit": "mm" },
                "maxX": { "value": 4.0, "unit": "mm" },
                "maxY": { "value": 4.0, "unit": "mm" },
                "maxZ": { "value": 4.0, "unit": "mm" },
                {{upperTaps}}
                "potential": { "value": 0.0, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0.0, 0.0, 10.0], "unit": "mm" },
        "normal": { "value": [0, 0, -1] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 100.0, "unit": "us" }
      }
    }
    """;
}
