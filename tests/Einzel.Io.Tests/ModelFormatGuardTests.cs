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
            "pushField": { "value": 0, "unit": "V/m" },
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

    /// <summary>A transport block a diffusive phase can actually run in.</summary>
    /// <remarks>
    /// The mode stays "trajectory" on purpose: what these tests are about is a phase
    /// naming a different one, and a model whose own mode were already diffusion would
    /// not distinguish the two.
    /// </remarks>
    private const string DiffusiveTail =
        """
          "transport": {
            "mode": "trajectory",
            "maximumFlightTime": { "value": 1, "unit": "ms" },
            "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
            "densityGrid": {
              "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
              "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
              "intervalsX": 32, "intervalsY": 16
            },
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": 1, "unit": "mbar" },
              "mass": { "value": 28.0134, "unit": "Da" },
              "crossSection": { "value": 250, "unit": "Å^2" }
            }
          }
        }
        """;

    private const string LiveField =
        """
          "fields": [{ "type": "uniform", "field": { "value": [100000, 0, 0], "unit": "V/m" } }],
        """;

    /// <summary>What Head declares, so a version test can vary it.</summary>
    private const string Version = "0.3";

    private static ModelValidation Validate(string fields, string planePoint) =>
        Validate(fields, planePoint, Head);

    private static ModelValidation Validate(string fields, string planePoint, string head) =>
        Validate(fields, planePoint, head, Tail);

    private static ModelValidation Validate(
        string fields, string planePoint, string head, string tail) =>
        ModelValidator.Validate(ModelJson.Parse(
            head + fields
            + "  \"detector\": { \"planePoint\": " + planePoint + ", \"normal\": { \"value\": [-1, 0, 0] } },\n"
            + tail));

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

    private const string TwoSolvesBothStaged =
        """
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "stages": [
                { "name": "hold", "duration": { "value": 100, "unit": "us" } }
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
              "stages": [
                { "name": "other", "duration": { "value": 5, "unit": "us" } }
              ],
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
    /// A phase is the instrument's, so every element is recompiled against it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The defect this closes made a model that validated cleanly give two electrodes
    /// with identical expressions different voltages.</b> A stage sets a model
    /// <em>parameter</em>, but stages were compiled per element — so with
    /// <c>"potential": "volts"</c> on an electrode in each of two elements, the element
    /// declaring the stage went to 900 V during the push and the other stayed at 300,
    /// with no diagnostic anywhere.
    /// </para>
    /// <para>
    /// The stage design's own rationale is the claim that was failing: setting a
    /// parameter "moves everything that depends on it at once". It now does — the
    /// timeline is resolved once for the model and handed to every element.
    /// </para>
    /// </remarks>
    [Fact]
    public void APhaseMovesEveryElementNotOnlyTheOneThatDeclaredIt()
    {
        var validation = Validate(TwoSolvesOneStaged, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var a = validation.Model!.Fields[0].Solve!;
        var b = validation.Model!.Fields[1].Solve!;

        // Both baselines are the same expression over the same parameter.
        Assert.Equal(300.0, a.Electrodes[0].Potential, 1e-9);
        Assert.Equal(300.0, b.Electrodes[0].Potential, 1e-9);

        // Both follow the timeline, including the element that did not declare it.
        Assert.Equal(2, a.Stages.Count);
        Assert.Equal(2, b.Stages.Count);

        Assert.Equal(900.0, a.Stages[1].Electrodes[0].Potential, 1e-9);
        Assert.Equal(900.0, b.Stages[1].Electrodes[0].Potential, 1e-9);

        // And they switch at the same instants, because there is one timeline.
        Assert.Equal(a.Stages[0].DurationSeconds, b.Stages[0].DurationSeconds, 15);
        Assert.Equal(a.Stages[1].DurationSeconds, b.Stages[1].DurationSeconds, 15);
    }

    /// <summary>Two elements each declaring stages is two timelines, and is refused.</summary>
    /// <remarks>
    /// An instrument has one. Two would each switch at their own instants over the same
    /// parameters, and the document would say two things about what the instrument is
    /// doing — with no reading that makes both true.
    /// </remarks>
    [Fact]
    public void TwoElementsEachDeclaringStagesIsRefused()
    {
        var validation = Validate(TwoSolvesBothStaged, OnAxis);

        Assert.False(validation.IsValid);

        Assert.Contains(
            validation.Errors,
            e => e.Constraint.Contains("one timeline", StringComparison.Ordinal));
    }

    private const string ModelSequence =
        """
          "sequence": [
            { "name": "hold", "duration": { "value": 100, "unit": "us" } },
            { "name": "push", "duration": { "value": 10, "unit": "us" },
              "set": { "volts": { "value": 900, "unit": "V" } } }
          ],
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
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
    /// The timeline may be declared on the model, which is where it belongs.
    /// </summary>
    /// <remarks>
    /// Section 9's words are that "an instrument is a timed state machine" - a timeline
    /// is a property of the instrument, not of one electrode assembly. `stages` on a
    /// solve stays the older spelling because the shipped sequenced example is written
    /// in it and a single-element model has no ambiguity to resolve; `sequence` is the
    /// one to write when more than one element is involved.
    /// </remarks>
    [Fact]
    public void TheTimelineMayBeDeclaredOnTheModel()
    {
        var validation = Validate(ModelSequence, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        foreach (var element in validation.Model!.Fields)
        {
            Assert.Equal(2, element.Solve!.Stages.Count);
            Assert.Equal(300.0, element.Solve!.Electrodes[0].Potential, 1e-9);
            Assert.Equal(900.0, element.Solve!.Stages[1].Electrodes[0].Potential, 1e-9);
        }
    }

    private const string SequenceAndStages =
        """
          "sequence": [
            { "name": "hold", "duration": { "value": 100, "unit": "us" } }
          ],
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "stages": [
                { "name": "x", "duration": { "value": 1, "unit": "us" } }
              ],
              "electrodes": [{
                "name": "a", "shape": "rectangle",
                "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                "potential": { "expression": "volts", "unit": "V" }
              }]
            }
          }],
        """;

    /// <summary>Declaring the timeline twice is refused rather than merged.</summary>
    /// <remarks>
    /// The same argument that refuses a geometry declaring both <c>drive</c> and
    /// <c>drives</c>: a document saying the instrument has one timeline and also another
    /// is not a document with a default to fall back on.
    /// </remarks>
    [Fact]
    public void AModelSequenceAndAnElementStageIsRefused()
    {
        var validation = Validate(SequenceAndStages, OnAxis);

        Assert.False(validation.IsValid);

        Assert.Contains(
            validation.Errors,
            e => e.Constraint.Contains("also declares stages", StringComparison.Ordinal));
    }

    /// <summary>And a sequence on the only element is still fine.</summary>
    /// <remarks>
    /// The control. `sequenced-extraction` is in the release gate and is written this
    /// way, so a change that also broke the single-element spelling would be a regression
    /// wearing the clothes of a fix.
    /// </remarks>
    [Fact]
    public void ASequenceOnTheOnlyElementIsStillAccepted()
    {
        var validation = Validate(StagedSolve, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(2, validation.Model!.Fields[0].Solve!.Stages.Count);
    }

    private const string SequenceWithAnalytic =
        """
          "sequence": [
            { "name": "hold", "duration": { "value": 100, "unit": "us" } },
            { "name": "push", "duration": { "value": 10, "unit": "us" },
              "set": { "volts": { "value": 900, "unit": "V" } } }
          ],
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "electrodes": [{
                "name": "a", "shape": "rectangle",
                "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                "potential": { "expression": "volts", "unit": "V" }
              }]
            }
          }, {
            "type": "halfSpaceUniform",
            "planePoint": { "value": [50, 0, 0], "unit": "mm" },
            "inwardNormal": { "value": [1, 0, 0] },
            "capPotential": { "expression": "volts", "unit": "V" },
            "turningDepth": { "value": 20, "unit": "mm" }
          }],
        """;

    private const string SequenceWithStaticAnalytic =
        """
          "sequence": [
            { "name": "hold", "duration": { "value": 100, "unit": "us" } },
            { "name": "push", "duration": { "value": 10, "unit": "us" },
              "set": { "volts": { "value": 900, "unit": "V" } } }
          ],
          "fields": [{
            "type": "halfSpaceUniform",
            "planePoint": { "value": [50, 0, 0], "unit": "mm" },
            "inwardNormal": { "value": [1, 0, 0] },
            "capPotential": { "value": 300, "unit": "V" },
            "turningDepth": { "value": 20, "unit": "mm" }
          }],
        """;

    /// <summary>
    /// An analytic element follows the timeline too, not only a solved one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The half the first lift missed.</b> Threading the timeline to the solved branch
    /// alone left a model whose sequence set a parameter used by a <c>halfSpaceUniform</c>
    /// cap potential with the solved elements following and the analytic one frozen at
    /// baseline — the same silent wrong answer, in the elements nobody thought of because
    /// they have no stages of their own to carry a phase.
    /// </para>
    /// <para>
    /// Sharper still: a model whose <em>only</em> elements are analytic compiled a full
    /// timeline that nothing consumed, so the sequence was a silent no-op.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAnalyticElementFollowsTheTimelineToo()
    {
        var validation = Validate(SequenceWithAnalytic, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var solved = validation.Model!.Fields[0].Solve!;
        var analytic = validation.Model!.Fields[1];

        // The solved element follows, as it did before.
        Assert.Equal(900.0, solved.Stages[1].Electrodes[0].Potential, 1e-9);

        // And so does the analytic one. 300 V over 20 mm is 15 kV/m; 900 V is 45 kV/m.
        Assert.Equal(2, analytic.Phases.Count);
        Assert.Equal(15_000.0, analytic.PotentialGradientSi, 1e-6);
        Assert.Equal(15_000.0, analytic.Phases[0].PotentialGradientSi, 1e-6);
        Assert.Equal(45_000.0, analytic.Phases[1].PotentialGradientSi, 1e-6);

        // Boundaries are cumulative, so the push ends at 110 us. Compared with a
        // tolerance because 100 us is 9.999999999999999e-05, not 1e-4 - the unit
        // conversion rounds, and an exact comparison here would be asserting the
        // rounding rather than the schedule.
        Assert.Equal(2, analytic.PhaseBoundariesSeconds.Count);
        Assert.Equal(1.0e-4, analytic.PhaseBoundariesSeconds[0], 1e-16);
        Assert.Equal(1.1e-4, analytic.PhaseBoundariesSeconds[1], 1e-16);
    }

    /// <summary>
    /// An element the timeline does not move stays static, rather than being wrapped.
    /// </summary>
    /// <remarks>
    /// A real distinction and not an optimisation. An element whose expressions do not
    /// depend on any parameter a phase sets genuinely is static; wrapping it would make it
    /// answer a time-varying interface and hand the integrator switch instants to land on
    /// for a field that is the same on both sides of them.
    /// </remarks>
    [Fact]
    public void AnElementTheTimelineDoesNotMoveIsNotWrapped()
    {
        var validation = Validate(SequenceWithStaticAnalytic, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var analytic = validation.Model!.Fields[0];

        Assert.Equal(15_000.0, analytic.PotentialGradientSi, 1e-6);
        Assert.Empty(analytic.Phases);
        Assert.Empty(analytic.PhaseBoundariesSeconds);
    }

    private const string EmptySequence =
        """
          "sequence": [],
          "fields": [{
            "type": "halfSpaceUniform",
            "planePoint": { "value": [50, 0, 0], "unit": "mm" },
            "inwardNormal": { "value": [1, 0, 0] },
            "capPotential": { "value": 300, "unit": "V" },
            "turningDepth": { "value": 20, "unit": "mm" }
          }],
        """;

    /// <summary>An explicitly empty sequence is refused rather than read as absent.</summary>
    /// <remarks>
    /// An empty timeline reads exactly like no timeline. A generator that filtered every
    /// phase out should not produce a document indistinguishable from one that never had
    /// a sequence, which is the same argument that refuses an unrecognised property
    /// rather than ignoring it.
    /// </remarks>
    [Fact]
    public void AnEmptySequenceIsRefused()
    {
        var validation = Validate(EmptySequence, OnAxis);

        Assert.False(validation.IsValid);

        Assert.Contains(
            validation.Errors,
            e => e.Constraint.Contains("no phases in it", StringComparison.Ordinal));
    }

    private const string AtRestUntilAPhaseEnergises =
        """
          "sequence": [
            { "name": "hold", "duration": { "value": 2, "unit": "us" } },
            { "name": "push", "duration": { "value": 50, "unit": "us" },
              "set": { "pushField": { "value": 20000, "unit": "V/m" } } }
          ],
          "fields": [{
            "type": "uniform",
            "field": { "expression": ["pushField", "0", "0"] }
          }],
        """;

    /// <summary>
    /// A source at rest is allowed when only a phase energises the field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fifth configuration this check has had to learn.</b> CanDoWork asks whether
    /// anything could put energy into an ion that starts at rest, and it has been wrong
    /// four times before: reading only the DC (so a Paul trap, which holds all of its
    /// potential as drive, was refused), inspecting nothing at all in the 3D arm, reading
    /// only the base potentials rather than the solved stages, and now reading only an
    /// analytic element's baseline rather than its phases.
    /// </para>
    /// <para>
    /// A pulsed extraction is the archetype: everything sits at zero until the instrument
    /// switches. Written analytically here, that is a uniform field of exactly zero until
    /// the push phase gives it 20 kV/m — which the previous version declared incapable of
    /// moving anything.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASourceAtRestIsAllowedWhenOnlyAPhaseEnergisesTheField()
    {
        var validation = Validate(AtRestUntilAPhaseEnergises, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var element = validation.Model!.Fields[0];

        // Zero at baseline, energised by the push - which is the whole point.
        Assert.Equal(0.0, element.Field.Length, 1e-12);
        Assert.Equal(2, element.Phases.Count);
        Assert.Equal(0.0, element.Phases[0].Field.Length, 1e-12);
        Assert.Equal(20_000.0, element.Phases[1].Field.X, 1e-9);
    }

    /// <summary>And a sequence that never energises anything is still refused.</summary>
    /// <remarks>
    /// The control. A check widened until it accepts everything has stopped being a
    /// check, and this one exists to catch a model whose ion sits still until the
    /// flight-time ceiling expires.
    /// </remarks>
    [Fact]
    public void ASequenceThatNeverEnergisesAnythingIsStillRefused()
    {
        var inert = AtRestUntilAPhaseEnergises.Replace(
            "\"value\": 20000, \"unit\": \"V/m\"",
            "\"value\": 0, \"unit\": \"V/m\"",
            StringComparison.Ordinal);

        Assert.NotEqual(AtRestUntilAPhaseEnergises, inert);

        var validation = Validate(inert, OnAxis);

        Assert.False(
            validation.IsValid,
            "a model where nothing ever energises leaves its ion at rest until the "
            + "flight-time ceiling expires");
    }

    /// <summary>
    /// Every version this build claims to read, reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 14: "every schema bump ships a migration and a test that a corpus of
    /// prior-version models still loads". Schema 0.6 added the model-level sequence and
    /// this is that test — it did not exist for 0.2 through 0.5 either, so the claim that
    /// those still load has been an assertion in a list rather than a measurement since
    /// the first bump.
    /// </para>
    /// <para>
    /// Additive bumps are supposed to make this trivially true, which is exactly why it
    /// is worth checking: a property nobody exercises is one that holds until the day it
    /// does not. What would break it is a required member added to a document record, or
    /// a default that changes meaning — neither of which announces itself at the point of
    /// the change.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryVersionThisBuildClaimsToReadDoesRead()
    {
        Assert.Contains(ModelSchema.CurrentVersion, ModelSchema.SupportedVersions);

        foreach (var version in ModelSchema.SupportedVersions)
        {
            var document = Head.Replace(
                $"\"schemaVersion\": \"{Version}\"",
                $"\"schemaVersion\": \"{version}\"",
                StringComparison.Ordinal);

            // The edit has to have happened, or every iteration silently tests the
            // version Head already declares. Except for that one, where the replacement
            // is an identity - which is what this assertion caught on its first run.
            if (!string.Equals(version, Version, StringComparison.Ordinal))
            {
                Assert.NotEqual(Head, document);
            }

            var validation = Validate(LiveField, OnAxis, document);

            Assert.True(
                validation.IsValid,
                $"schema {version} is in SupportedVersions and did not load: "
                + string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        }
    }

    /// <summary>And a version this build does not know is refused rather than guessed at.</summary>
    /// <remarks>
    /// The control. A reader that accepted anything would pass the test above without
    /// reading a single version correctly.
    /// </remarks>
    [Fact]
    public void AnUnknownSchemaVersionIsRefused()
    {
        var document = Head.Replace(
            $"\"schemaVersion\": \"{Version}\"",
            "\"schemaVersion\": \"9.9\"",
            StringComparison.Ordinal);

        Assert.NotEqual(Head, document);

        var validation = Validate(LiveField, OnAxis, document);

        Assert.False(validation.IsValid);
    }

    private const string PlaneAndVolumeSequenced =
        """
          "sequence": [
            { "name": "hold", "duration": { "value": 100, "unit": "us" } },
            { "name": "push", "duration": { "value": 10, "unit": "us" },
              "set": { "volts": { "value": 900, "unit": "V" } } }
          ],
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "electrodes": [{
                "name": "plane", "shape": "rectangle",
                "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                "potential": { "expression": "volts", "unit": "V" }
              }]
            }
          }, {
            "type": "solved3d",
            "solve3d": {
              "minX": { "value": -5, "unit": "mm" }, "minY": { "value": -5, "unit": "mm" },
              "minZ": { "value": -5, "unit": "mm" },
              "maxX": { "value": 5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "maxZ": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 1, "unit": "mm" },
              "electrodes": [{
                "name": "volume", "shape": "box",
                "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 2, "unit": "mm" },
                "minY": { "value": 1, "unit": "mm" }, "maxY": { "value": 2, "unit": "mm" },
                "minZ": { "value": -2, "unit": "mm" }, "maxZ": { "value": 2, "unit": "mm" },
                "potential": { "expression": "volts", "unit": "V" }
              }]
            }
          }],
        """;

    /// <summary>
    /// A volume element follows the instrument's timeline exactly as a plane one does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The 3D arm of the sequencer had no test at all</b> — before the timeline was
    /// lifted or after — so `CompileStages3D` was exercised only by compiling. It is a
    /// separate copy of the same shape as the 2D arm rather than shared code, which is
    /// exactly the arrangement where one arm can be fixed and the other left behind.
    /// </para>
    /// <para>
    /// A sequence is the instrument's, so a model mixing a cross-section and a volume
    /// must switch both at the same instants and against the same parameter values. That
    /// is the claim `CompileStages3D`'s own doc comment makes, and this is what checks it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AVolumeElementFollowsTheSameTimelineAsAPlaneOne()
    {
        var validation = Validate(PlaneAndVolumeSequenced, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var plane = validation.Model!.Fields[0].Solve!;
        var volume = validation.Model!.Fields[1].Solve3D!;

        Assert.Equal(2, plane.Stages.Count);
        Assert.Equal(2, volume.Stages.Count);

        // Both baselines are the same expression over the same parameter, and both
        // follow the push - the volume element having declared no stages of its own.
        Assert.Equal(300.0, plane.Electrodes[0].Potential, 1e-9);
        Assert.Equal(300.0, volume.Electrodes[0].Potential, 1e-9);
        Assert.Equal(900.0, plane.Stages[1].Electrodes[0].Potential, 1e-9);
        Assert.Equal(900.0, volume.Stages[1].Electrodes[0].Potential, 1e-9);

        // One timeline means one set of instants.
        Assert.Equal(plane.Stages[0].DurationSeconds, volume.Stages[0].DurationSeconds, 15);
        Assert.Equal(plane.Stages[1].DurationSeconds, volume.Stages[1].DurationSeconds, 15);

        // And the phases are named the same, because they are the same phases.
        Assert.Equal("hold", volume.Stages[0].Name);
        Assert.Equal("push", volume.Stages[1].Name);
    }

    private const string ModeChangingSequence =
        """
          "sequence": [
            { "name": "thermalise", "duration": { "value": 100, "unit": "us" },
              "mode": "diffusion" },
            { "name": "extract", "duration": { "value": 50, "unit": "us" },
              "mode": "trajectory" }
          ],
        """;

    /// <summary>A phase may name its own transport mode (SEQ-1).</summary>
    /// <remarks>
    /// §9 lists transport mode among what a phase carries, alongside its duration and
    /// its excitation overrides, and SEQ-1 says a phase boundary may change it. A real
    /// instrument does this as a matter of course: ions are collected and thermalised in
    /// a gas-filled trap, where the description is a density, then extracted into vacuum
    /// and flown, where it is trajectories.
    /// </remarks>
    [Fact]
    public void APhaseMayNameItsOwnTransportMode()
    {
        var validation = Validate(
            ModeChangingSequence + LiveField, OnAxis, Head, DiffusiveTail);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var phases = validation.Model!.Phases;

        Assert.Equal(2, phases.Count);
        Assert.Equal("diffusion", phases[0].Mode);
        Assert.Equal("trajectory", phases[1].Mode);
        Assert.True(validation.Model!.ChangesTransportMode);

        // Cumulative, because what a run needs is when a phase ends rather than how
        // long it lasts.
        Assert.Equal(1.0e-4, phases[0].EndsAtSeconds, 1e-16);
        Assert.Equal(1.5e-4, phases[1].EndsAtSeconds, 1e-16);
    }

    /// <summary>A phase naming no mode keeps the model's, and that is not a change.</summary>
    /// <remarks>
    /// The same rule a phase's parameter overrides follow: anything it does not name
    /// keeps the value it has outside the sequence. So a model with no sequence and one
    /// whose every phase runs in the declared mode are the same run, and neither needs a
    /// conversion at any boundary.
    /// </remarks>
    [Fact]
    public void APhaseNamingNoModeKeepsTheModels()
    {
        var validation = Validate(StagedSolve, OnAxis);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        Assert.All(validation.Model!.Phases, phase => Assert.Equal("trajectory", phase.Mode));
        Assert.False(validation.Model!.ChangesTransportMode);
    }

    /// <summary>A mode a phase cannot name is refused, pointing at the phase.</summary>
    [Fact]
    public void AnUnknownPhaseModeIsRefused()
    {
        var validation = Validate(
            ModeChangingSequence.Replace(
                "\"mode\": \"diffusion\"", "\"mode\": \"statisticalDiffusion\"",
                StringComparison.Ordinal)
            + LiveField,
            OnAxis,
            Head,
            DiffusiveTail);

        Assert.False(validation.IsValid);

        var refusal = Assert.Single(
            validation.Errors,
            e => e.Path is not null && e.Path.EndsWith("/mode", StringComparison.Ordinal));

        Assert.Contains("sequence/0/mode", refusal.Path!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A diffusive phase needs a gas, even when the model's own mode is trajectory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The sixth configuration a check here has had to learn.</b> The diffusive
    /// requirements — a gas, a mobility, a density grid — were gated on the model's own
    /// <c>transport.mode</c>, so a trajectory model with a diffusive phase skipped all of
    /// them, validated cleanly, and would have failed at run time asking for the gas it
    /// never declared.
    /// </para>
    /// <para>
    /// The same shape as the DC, the drive, the 3D arm, the solved stages and the
    /// analytic phases before it: a check that asks what an instrument is doing must ask
    /// over every configuration it has.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiffusivePhaseNeedsAGasEvenWhenTheModelIsTrajectory()
    {
        var validation = Validate(ModeChangingSequence + LiveField, OnAxis, Head, Tail);

        Assert.False(
            validation.IsValid,
            "a diffusive phase in a trajectory model needs the gas the diffusive mode "
            + "describes ions moving through");

        Assert.Contains(
            validation.Errors,
            e => e.Path == "/transport/gas");
    }
}
