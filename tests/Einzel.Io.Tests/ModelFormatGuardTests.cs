using Einzel.Core.Model;
using Einzel.Io;

namespace Einzel.Io.Tests;

/// <summary>
/// The guards around the three format changes a trap needed.
/// </summary>
/// <remarks>
/// Widening a format is where the mistakes are, because a widening cannot fail a
/// test that was never written. Each of these pins a way the change could have
/// gone too far: a dimension rule that moves under a sweep, an expression that
/// evaluates to infinity, a field that is field-free in all but name, a second
/// error piled on one mistake, and a rectangle that disappears instead of
/// complaining.
/// </remarks>
public sealed class ModelFormatGuardTests
{
    private const string Head =
        """
        {
          "schemaVersion": "0.3",
          "name": "t",
          "parameters": {
            "gap": { "value": 4, "unit": "mm" },
            "span": { "value": 4, "unit": "mm" },
            "volts": { "value": 300, "unit": "V" },
            "tilt": { "value": 0, "unit": "1" },
            "drift": { "expression": "gap * 3", "unit": "mm" }
          },
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 0, "unit": "V" }
          },
        """;

    private const string Tail =
        """
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
        }
        """;

    private const string LiveField =
        """
          "fields": [{ "type": "uniform", "field": { "value": [100000, 0, 0], "unit": "V/m" } }],
        """;

    private static ModelValidation Validate(string fields, string planePoint) =>
        ModelValidator.Validate(ModelJson.Parse(
            Head + fields
            + "  \"detector\": { \"planePoint\": " + planePoint + ", \"normal\": { \"value\": [-1, 0, 0] } },\n"
            + Tail));

    private const string OnAxis = """{ "expression": ["drift", "0", "0"], "unit": "mm" }""";

    [Fact]
    public void TheBaselineIsValid()
    {
        // Everything below is this document with one thing broken, so it has to be
        // whole first or the tests pass for the wrong reason.
        var validation = Validate(LiveField, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(0.012, validation.Model!.DetectorPoint.X, 1e-15);
    }

    private const string StagedSolve =
        """
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "stages": [
                { "name": "hold", "duration": { "value": 100, "unit": "us" } },
                { "name": "push", "duration": { "value": 10, "unit": "us" },
                  "set": { "gap": { "value": 8, "unit": "mm" } } }
              ],
              "electrodes": [{
                "name": "plate", "shape": "rectangle",
                "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                "potential": { "expression": "volts * drift / span", "unit": "V" }
              }]
            }
          }],
        """;

    private const string MovingSolve =
        """
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "stages": [
                { "name": "before", "duration": { "value": 1, "unit": "us" } },
                { "name": "moved", "duration": { "value": 1, "unit": "us" },
                  "set": { "gap": { "value": 8, "unit": "mm" } } }
              ],
              "electrodes": [{
                "name": "plate", "shape": "rectangle",
                "minX": { "expression": "span - gap", "unit": "mm" },
                "maxX": { "expression": "span", "unit": "mm" },
                "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              }]
            }
          }],
        """;

    [Fact]
    public void AStageSetsParametersForATime()
    {
        // The sequencer, as a document says it. A stage names parameter values and
        // a duration, and because electrode potentials are already expressions over
        // parameters, setting one moves everything that depends on it at once -
        // including the derived parameters, which is what listing electrode
        // settings instead could not do.
        var validation = Validate(StagedSolve, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var stages = validation.Model!.Fields[0].Solve!.Stages;

        Assert.Equal(2, stages.Count);
        Assert.Equal("hold", stages[0].Name);
        Assert.Equal(1e-4, stages[0].DurationSeconds, 15);
        Assert.Equal(1e-5, stages[1].DurationSeconds, 15);

        // The potential is written in terms of drift, which is *derived* from gap.
        // Setting gap to 8 mm therefore has to move drift to 24 mm and the potential
        // with it - 900 V to 1800 V - which is the whole reason a stage sets
        // parameters rather than listing electrode settings.
        Assert.Equal(900.0, stages[0].Electrodes[0].Potential, 9);
        Assert.Equal(1800.0, stages[1].Electrodes[0].Potential, 9);
    }

    [Fact]
    public void AStageThatMovesMetalIsRefused()
    {
        // A stage may change what an electrode holds, not where it is. Moving a
        // plate would change the mask, so every stage would need its own solve and
        // its own grid - and the field would still be computed, and it would be
        // wrong in a way nothing else catches.
        var validation = Validate(MovingSolve, OnAxis);

        Assert.False(validation.IsValid);

        Assert.Contains(
            validation.Errors,
            e => e.Constraint!.Contains("moves electrode 'plate'", StringComparison.Ordinal));
    }

    [Fact]
    public void AStageNeedsAPositiveDuration()
    {
        var validation = Validate(
            StagedSolve.Replace(
                """{ "value": 100, "unit": "us" }""",
                """{ "value": 0, "unit": "us" }""",
                StringComparison.Ordinal),
            OnAxis);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Constraint!.Contains("positive time", StringComparison.Ordinal));
    }

    [Fact]
    public void AZeroValuedParameterDoesNotStandInForALiteralZero()
    {
        // The exception is on what was written, not on what it evaluated to. A
        // value test would make dimensional validity depend on a number: this
        // document would validate at nominal and then fail with a units error
        // partway through a sweep, the moment an optimiser moved the parameter off
        // zero. Dimensions are a property of the text and must not move under an
        // override.
        var validation = Validate(
            LiveField, """{ "expression": ["drift", "tilt", "0"], "unit": "mm" }""");

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void ANonFiniteVectorComponentIsRefused()
    {
        // The literal path has always checked this. The expression path is where a
        // non-finite value actually comes from, because a literal has to be typed
        // out and a division does not.
        var validation = Validate(
            LiveField, """{ "expression": ["drift / 0", "0", "0"], "unit": "mm" }""");

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Constraint!.Contains("finite", StringComparison.Ordinal));
    }

    [Fact]
    public void AFieldThatCannotDoWorkDoesNotLicenceASourceAtRest()
    {
        // The narrowing is about whether anything can accelerate the ion, not about
        // which type discriminator a field carries. A uniform field of zero is
        // field-free in everything but its name, and an ion at rest in one sits
        // there until the flight-time ceiling expires - the exact outcome the check
        // exists to prevent.
        var validation = Validate(
            """
              "fields": [{ "type": "uniform", "field": { "value": [0, 0, 0], "unit": "V/m" } }],
            """,
            OnAxis);

        Assert.False(validation.IsValid);

        Assert.Contains(
            validation.Errors,
            e => string.Equals(e.Path, "/source/accelerationPotential", StringComparison.Ordinal));
    }

    [Fact]
    public void AGroundedSolveCannotAccelerateEither()
    {
        // Same point through the other field kind. Every electrode at zero against
        // a grounded boundary is a solve with no gradient anywhere in it.
        var validation = Validate(
            """
              "fields": [{
                "type": "solved2d",
                "solve": {
                  "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
                  "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
                  "cellSize": { "value": 0.5, "unit": "mm" },
                  "electrodes": [{
                    "name": "grounded", "shape": "rectangle",
                    "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                    "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                    "potential": { "value": 0, "unit": "V" }
                  }]
                }
              }],
            """,
            OnAxis);

        Assert.False(validation.IsValid);

        Assert.Contains(
            validation.Errors,
            e => string.Equals(e.Path, "/source/accelerationPotential", StringComparison.Ordinal));
    }

    [Fact]
    public void ABrokenFieldDoesNotProduceASecondErrorAgainstTheSource()
    {
        // One mistake, one error. A field that failed to compile is not evidence
        // that nothing can accelerate the ion, it is evidence that we cannot tell -
        // and advising the author to declare a field they did declare is worse than
        // saying nothing.
        var validation = Validate(
            """
              "fields": [{ "type": "uniform", "field": { "value": [100000, 0, 0], "unit": "mm" } }],
            """,
            OnAxis);

        Assert.False(validation.IsValid);

        Assert.DoesNotContain(
            validation.Errors,
            e => string.Equals(e.Path, "/source/accelerationPotential", StringComparison.Ordinal));
    }

    [Fact]
    public void AnInvertedRectangleIsRefusedRatherThanVanishing()
    {
        // A rectangle whose bounds cross rasterises to nothing: the electrode is
        // absent from the solve and the run reports a plausible non-arrival rather
        // than a geometry error. Reachable from ordinary parameter arithmetic,
        // which is what makes it worth an error - a tolerance sweep would otherwise
        // attribute a vanished electrode to physics.
        var validation = Validate(
            """
              "fields": [{
                "type": "solved2d",
                "solve": {
                  "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
                  "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
                  "cellSize": { "value": 0.5, "unit": "mm" },
                  "electrodes": [{
                    "name": "backwards", "shape": "rectangle",
                    "minX": { "value": 2, "unit": "mm" }, "maxX": { "value": -2, "unit": "mm" },
                    "minY": { "value": -1, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
                    "potential": { "value": 100, "unit": "V" }
                  }]
                }
              }],
            """,
            OnAxis);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Constraint!.Contains("inverted", StringComparison.Ordinal));
    }

    [Fact]
    public void AZeroExtentRectangleIsStillAllowed()
    {
        // A rectangle of zero extent in one axis is a line segment, which is how an
        // infinitely thin plate is written - the mirror template's cap is one - and
        // cut cells resolve it exactly. Rejecting it along with the inverted case
        // would have broken a shipped template, and did once.
        var validation = Validate(
            """
              "fields": [{
                "type": "solved2d",
                "solve": {
                  "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
                  "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
                  "cellSize": { "value": 0.5, "unit": "mm" },
                  "electrodes": [{
                    "name": "thinPlate", "shape": "rectangle",
                    "minX": { "value": 2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                    "minY": { "value": -1, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
                    "potential": { "value": 100, "unit": "V" }
                  }]
                }
              }],
            """,
            OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
    }

    private const string TwoSolvesOneStaged =
        """
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "stages": [
                { "name": "hold", "duration": { "value": 100, "unit": "us" } },
                { "name": "push", "duration": { "value": 10, "unit": "us" },
                  "set": { "volts": { "value": 900, "unit": "V" } } }
              ],
              "electrodes": [{
                "name": "a", "shape": "rectangle",
                "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                "potential": { "expression": "volts", "unit": "V" }
              }]
            }
          }, {
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "electrodes": [{
                "name": "b", "shape": "rectangle",
                "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                "minY": { "value": -2, "unit": "mm" }, "maxY": { "value": -1, "unit": "mm" },
                "potential": { "expression": "volts", "unit": "V" }
              }]
            }
          }],
        """;

    /// <summary>
    /// A sequence belongs to the instrument, so a model with one may have one element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this closes made a model that validated cleanly give two electrodes
    /// with identical expressions different voltages.</b> A stage sets a model
    /// <em>parameter</em>, but only its own element is recompiled against the stage's
    /// surface — so with `"potential": "volts"` on an electrode in each of two elements,
    /// the staged one went to 900 V during the push and the other stayed at 300, with no
    /// diagnostic anywhere.
    /// </para>
    /// <para>
    /// The stage design's own rationale is the claim that fails: setting a parameter
    /// "moves everything that depends on it at once". It moves everything in one element.
    /// </para>
    /// <para>
    /// Refused rather than patched, because the coherent readings need work this does not
    /// do — either the timeline is the instrument's and every element recompiles at each
    /// stage, which is what SEQ-1's transport mode per phase will need anyway, or a stage
    /// is scoped to its element, which is a different feature from the documented one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASequenceAndASecondElementIsRefused()
    {
        var validation = Validate(TwoSolvesOneStaged, OnAxis);

        Assert.False(
            validation.IsValid,
            "a sequenced model with a second element compiles two electrodes with the "
            + "same expression to different voltages");

        var refusal = Assert.Single(
            validation.Errors,
            e => e.Constraint.Contains("field elements", StringComparison.Ordinal));

        Assert.Contains("identical expressions", refusal.Suggestion!, StringComparison.Ordinal);
    }

    /// <summary>And a sequence on a single element is still fine.</summary>
    /// <remarks>
    /// The control. A refusal that also refused the shipped case would be a regression
    /// wearing the clothes of a fix, and `sequenced-extraction` is in the release gate.
    /// </remarks>
    [Fact]
    public void ASequenceOnTheOnlyElementIsStillAccepted()
    {
        var validation = Validate(StagedSolve, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(2, validation.Model!.Fields[0].Solve!.Stages.Count);
    }
}
