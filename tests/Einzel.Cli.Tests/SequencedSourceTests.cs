using System.Text.Json;

using Einzel.Core.Model;

namespace Einzel.Cli.Tests;

/// <summary>
/// A sequenced instrument that holds everything at zero until it switches.
/// </summary>
/// <remarks>
/// <para>
/// Two defects, both found by writing a corpus example for pulsed extraction — which is
/// the one Phase 4 capability the corpus does not exercise, and which the example still
/// cannot exercise for a third reason recorded in <c>docs/lessons.md</c>.
/// </para>
/// <para>
/// The first is the fourth appearance of a pattern this repository already warns about:
/// <em>reading the DC of an electrode that holds none</em>. <c>CanDoWork</c> asked
/// whether any electrode held a non-zero potential or a drive, and a pulsed-extraction
/// trap holds neither until its second stage — so the archetypal start-at-rest device
/// was refused as one in which nothing could move an ion. Exactly what happened to the
/// Paul trap when the check looked only at DC.
/// </para>
/// </remarks>
public sealed class SequencedSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-seq", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A gap whose plates are moved by a stage rather than by their own potential.</summary>
    private static string Gap(string firstStageSet, string secondStageSet) => $$"""
    {
      "schemaVersion": "0.5",
      "name": "sequenced-gap",
      "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
      "parameters": {
        "appliedVolts": {
          "value": 0.0, "unit": "V", "minimum": -2000.0, "maximum": 2000.0,
          "description": "What the plates hold. The stages set this."
        }
      },
      "source": {
        "position": { "value": [0, -4, 0], "unit": "mm" },
        "direction": { "value": [0, 1, 0] },
        "accelerationPotential": { "value": 0.0, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved2d",
          "solve": {
            "minX": { "value": -30, "unit": "mm" },
            "minY": { "value": -16, "unit": "mm" },
            "maxX": { "value": 30, "unit": "mm" },
            "maxY": { "value": 16, "unit": "mm" },
            "cellSize": { "value": 1.0, "unit": "mm" },
            "electrodes": [
              {
                "name": "lower", "shape": "rectangle",
                "minX": { "value": -20, "unit": "mm" },
                "minY": { "value": -6, "unit": "mm" },
                "maxX": { "value": 20, "unit": "mm" },
                "maxY": { "value": -5, "unit": "mm" },
                "potential": { "expression": "appliedVolts / 2", "unit": "V" }
              },
              {
                "name": "upper", "shape": "rectangle",
                "minX": { "value": -20, "unit": "mm" },
                "minY": { "value": 5, "unit": "mm" },
                "maxX": { "value": 20, "unit": "mm" },
                "maxY": { "value": 6, "unit": "mm" },
                "potential": { "expression": "-appliedVolts / 2", "unit": "V" }
              }
            ],
            "stages": [
              { "name": "hold", "duration": { "value": 2.0, "unit": "us" },
                "set": { {{firstStageSet}} } },
              { "name": "extract", "duration": { "value": 100.0, "unit": "us" },
                "set": { {{secondStageSet}} } }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0, 4, 0], "unit": "mm" },
        "normal": { "value": [0, -1, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 50.0, "unit": "us" }
      }
    }
    """;

    private static ModelValidation Validate(string json) =>
        ModelValidator.Validate(Io.ModelJson.Parse(json), null);

    /// <summary>
    /// A source may start at rest when a <em>stage</em> can accelerate it.
    /// </summary>
    /// <remarks>
    /// The fourth sighting of one pattern. Every electrode holds zero until the second
    /// stage, so a check reading only the base potentials asks what the instrument is
    /// doing before it has been told to do anything — and refuses the archetypal
    /// start-at-rest device on the grounds that nothing can move the ion.
    /// </remarks>
    [Fact]
    public void AStageThatEnergisesTheGeometryLetsTheSourceStartAtRest()
    {
        var validation = Validate(Gap(
            "\"appliedVolts\": { \"value\": 0.0, \"unit\": \"V\" }",
            "\"appliedVolts\": { \"value\": 1000.0, \"unit\": \"V\" }"));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        Assert.Equal(2, validation.Model!.Fields[0].Solve!.Stages.Count);
    }

    /// <summary>And a sequence that never energises anything is still refused.</summary>
    /// <remarks>
    /// The control. Widening a check until it accepts the case in front of you is easy
    /// and useless; what says the widening was correct is that the thing it was
    /// protecting against is still caught. An ion at rest in a geometry that is at zero
    /// throughout every stage sits there until the flight-time ceiling expires, which is
    /// exactly the outcome the check exists to prevent.
    /// </remarks>
    [Fact]
    public void ASequenceThatNeverEnergisesAnythingIsStillRefused()
    {
        var validation = Validate(Gap(
            "\"appliedVolts\": { \"value\": 0.0, \"unit\": \"V\" }",
            "\"appliedVolts\": { \"value\": 0.0, \"unit\": \"V\" }"));

        Assert.False(validation.IsValid);

        Assert.Contains(
            validation.Errors,
            e => e.Path == "/source/accelerationPotential");
    }

    /// <summary>
    /// A stage set to an expression is refused, not read as its absent literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to be read as <c>Value</c>, which for a document carrying an expression is
    /// the default zero. So a stage meant to apply a kilovolt applied nothing: the model
    /// validated, the field solved, and the run reported an ion that never moved. There
    /// was no diagnostic anywhere, because from the engine's point of view the author had
    /// asked for zero volts.
    /// </para>
    /// <para>
    /// Refused rather than supported, because what an expression should mean here is not
    /// settled: the surface it would evaluate against is the one the stage is in the
    /// middle of changing.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStageSetToAnExpressionIsRefused()
    {
        var validation = Validate(Gap(
            "\"appliedVolts\": { \"value\": 0.0, \"unit\": \"V\" }",
            "\"appliedVolts\": { \"expression\": \"appliedVolts\", \"unit\": \"V\" }"));

        Assert.False(validation.IsValid);

        var failure = Assert.Single(
            validation.Errors,
            e => e.Path.EndsWith("/set/appliedVolts", StringComparison.Ordinal));

        Assert.Contains("not to an expression", failure.Constraint, StringComparison.Ordinal);
    }
}
