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
    /// KNOWN DEFECT, characterised: an ion at rest when a field switches on underflows
    /// inside the refinement ladder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It could not. The run gave <c>StepSizeUnderflow</c> at exactly the switch after
    /// 63 accepted steps - a fixed count, invariant under tolerance, cell size, flight
    /// time and the ion's speed, which is the signature of a step being <em>rejected</em>
    /// at every size rather than a controller converging.
    /// </para>
    /// <para>
    /// <b>The cause was the refinement ladder, not the sequencer.</b> A single
    /// integration crosses the switch at every tolerance from 1e-8 to 1e-14;
    /// <c>FlightTimeStudy</c> does not, because it scaled the absolute position and
    /// velocity floors along with the relative tolerance. At its deepest level the
    /// velocity floor reaches 1e-11 m/s - ten picometres per second, against thermal
    /// speeds of hundreds of metres - and for an ion starting from rest the normalised
    /// velocity error is then unsatisfiable at any step size.
    /// </para>
    /// <para>
    /// A floor says what is negligible, and what is negligible does not change because a
    /// more accurate answer was asked for. The ladder refines the relative tolerance and
    /// holds the floors.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1e-8)]
    [InlineData(1e-10)]
    [InlineData(1e-12)]
    public void AnIonAtRestUnderflowsInTheRefinementLadder(double tolerance)
    {
        var model = Validate(Gap(
            "\"appliedVolts\": { \"value\": 0.0, \"unit\": \"V\" }",
            "\"appliedVolts\": { \"value\": 1000.0, \"unit\": \"V\" }")).Model!;

        var species = Transport.IonSpecies.FromModel(model);

        var settings = new Transport.Integration.IntegrationSettings
        {
            RelativeTolerance = tolerance,
            MaximumFlightTime = 10e-6,
        };

        var start = new Transport.PhaseState(model.SourcePosition, Core.Geometry.Vec3.Zero);

        var at = model.DetectorPoint;
        var normal = model.DetectorNormal;

        Transport.Integration.TrajectoryStopFunction detector =
            (in Transport.PhaseState state) => Core.Geometry.Vec3.Dot(state.Position - at, normal);

        var field = Fields.FieldAssembly.BuildReported(model).Field;

        var study = Transport.Integration.FlightTimeStudy.Run(
            start, species, field, settings, detector);

        // Characterising the defect rather than asserting it is correct. A rung that
        // underflows is not a measurement, and the study builds its interval from these
        // runs regardless - so the reported number covers a run that never happened.
        //
        // THE FIX IS ONE LINE, and it is not taken here because its cost needs a
        // decision: holding AbsoluteVelocityTolerance in the ladder makes this pass and
        // leaves the reflectron's flight time bit-identical with a 17x NARROWER interval
        // (1.48e-10 us against 2.58e-09) - but it also stops that model's rungs agreeing
        // to the last bit, which is the premise
        // IntegratorBehaviourTests.AnIntervalThatCollapsesToZeroIsReportedAsAFloorRatherThanAsExact
        // asserts. That model's bit-exact agreement DEPENDED on the ladder over-tightening
        // an unphysical floor, and no other construction reproduces the collapse through
        // the public API, so taking the fix costs `convergence.at-resolution` its only
        // physical test.
        //
        // When it is taken, this test should be inverted and renamed.
        Assert.Contains(
            study.Runs,
            r => r.Outcome == Transport.Integration.TrajectoryOutcome.StepSizeUnderflow);
    }

    /// <summary>
    /// And the flight is the closed form: a hold, then a traverse from rest.
    /// </summary>
    /// <remarks>
    /// Both plates sit at the same potential for the hold, so the field between them is
    /// exactly zero and an ion launched at rest waits. The second stage applies plus and
    /// minus half the voltage, and from rest across a uniform field the traverse is
    /// sqrt(2 d m / (q E)) with E = V/gap. Arithmetic this engine has no part in, and the
    /// same closed form the three-dimensional parallel-plate example is checked against.
    /// </remarks>
    [Fact]
    public void TheHeldThenExtractedFlightIsTheClosedForm()
    {
        var model = Validate(Gap(
            "\"appliedVolts\": { \"value\": 0.0, \"unit\": \"V\" }",
            "\"appliedVolts\": { \"value\": 1000.0, \"unit\": \"V\" }")).Model!;

        var species = Transport.IonSpecies.FromModel(model);

        var settings = new Transport.Integration.IntegrationSettings
        {
            RelativeTolerance = 1e-11,
            MaximumFlightTime = 10e-6,
        };

        var start = new Transport.PhaseState(model.SourcePosition, Core.Geometry.Vec3.Zero);

        var at = model.DetectorPoint;
        var normal = model.DetectorNormal;

        Transport.Integration.TrajectoryStopFunction detector =
            (in Transport.PhaseState state) => Core.Geometry.Vec3.Dot(state.Position - at, normal);

        var run = Transport.Integration.TrajectoryIntegrator.Integrate(
            start,
            species,
            Fields.FieldAssembly.BuildReported(model).Field,
            settings,
            detector);

        Assert.Equal(Transport.Integration.TrajectoryOutcome.StopConditionMet, run.Outcome);

        // 8 mm of the 10 mm gap at 1000 V, from rest: sqrt(2 d m / (q E)), plus the 2 us
        // hold. A per cent, because the plates are finite and the grounded boundary is a
        // third electrode - the geometry's own departure from an infinite capacitor,
        // which the three-dimensional example measures at a few parts in a thousand.
        const double Expected = 2e-6 + 0.910572113e-6;

        Assert.Equal(Expected, run.FlightTimeSeconds, 0.01 * Expected);
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
