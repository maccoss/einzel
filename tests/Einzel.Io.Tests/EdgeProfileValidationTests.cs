using Einzel.Core.Model;
using Einzel.Io;

namespace Einzel.Io.Tests;

/// <summary>
/// What the validator has to know about an edge profile, which keeps its volts somewhere
/// no other electrode does.
/// </summary>
/// <remarks>
/// <para>
/// Every other electrode answers "what do you hold" with a scalar potential and a list of
/// taps. An edge profile answers with neither: its potential is the profile along its edge,
/// and every shipped one leaves the scalar null. So two checks that ask the scalar got the
/// wrong answer - the start-at-rest guard concluded that a model whose only live electrode
/// was a profiled board could not move an ion at all, and the sequenced-solve check could
/// not see a stage changing one.
/// </para>
/// <para>
/// The first is the <b>sixth</b> configuration the start-at-rest guard has had to learn,
/// after the DC, the drive, the 3D arm, the solved stages and an analytic element energised
/// only by a phase. Its own remarks predicted this: a new way to hold a potential is a new
/// configuration.
/// </para>
/// </remarks>
public sealed class EdgeProfileValidationTests
{
    private const string Head =
        """
        {
          "schemaVersion": "0.6",
          "name": "edge",
          "parameters": {
            "boardVolts": { "value": -400, "unit": "V" },
            "stageVolts": { "value": -900, "unit": "V" }
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
          "detector": { "planePoint": { "value": [4, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 50, "unit": "us" } }
        }
        """;

    /// <summary>A box whose right edge is a profiled board, optionally restated by a stage.</summary>
    private static string Field(string stages, string stageVolts) =>
        $$"""
          "fields": [{
            "type": "solved2d",
            "solve": {
              "minX": { "value": -6, "unit": "mm" }, "maxX": { "value": 6, "unit": "mm" },
              "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
              "cellSize": { "value": 0.5, "unit": "mm" },
              "electrodes": [{
                "name": "board", "shape": "edgeProfile", "edge": "right",
                "profile": [
                  { "at": { "value": -6, "unit": "mm" }, "potential": { "expression": "boardVolts", "unit": "V" } },
                  { "at": { "value": 6, "unit": "mm" }, "potential": { "expression": "boardVolts", "unit": "V" } }
                ]
              }]{{stages}}
            }
          }],
        """.Replace("__STAGE__", stageVolts, StringComparison.Ordinal);

    private static ModelValidation Validate(string stages = "", string stageVolts = "boardVolts") =>
        ModelValidator.Validate(ModelJson.Parse(Head + Field(stages, stageVolts) + Tail));

    /// <summary>
    /// A profiled board is the only thing energised, and the source starts at rest. The
    /// board is worth hundreds of volts across the box, so the model is perfectly runnable -
    /// and it used to be refused as an instrument in which nothing could move an ion.
    /// </summary>
    [Fact]
    public void AProfiledBoardCanMoveAnIonFromRest()
    {
        var result = Validate();

        Assert.DoesNotContain(
            result.Errors,
            e => e.Constraint!.Contains("nothing", StringComparison.Ordinal)
                 || e.Path == "/source/accelerationPotential");
    }

    /// <summary>
    /// The control: a board at earth really cannot move anything, and the refusal must
    /// survive. Without this, filling the gap above could have been "always true".
    /// </summary>
    [Fact]
    public void AnEarthedProfiledBoardStillCannotMoveAnIonFromRest()
    {
        var earthed = ModelValidator.Validate(ModelJson.Parse(
            Head.Replace("\"value\": -400", "\"value\": 0", StringComparison.Ordinal)
            + Field("", "boardVolts") + Tail));

        Assert.Contains(earthed.Errors, e => e.Path == "/source/accelerationPotential");
    }

    /// <summary>
    /// A stage that changes the profile is refused rather than run. A solved sequence
    /// re-weights spatial patterns by a scalar, and two different profiles are two patterns
    /// - so the second stage would silently run the first one's field.
    /// </summary>
    [Fact]
    public void AStageThatChangesAProfileIsRefused()
    {
        const string Stages =
            """
            ,
                  "stages": [
                    { "name": "hold", "duration": { "value": 10, "unit": "us" }, "set": { "boardVolts": { "value": -400, "unit": "V" } } },
                    { "name": "push", "duration": { "value": 10, "unit": "us" }, "set": { "boardVolts": { "value": -900, "unit": "V" } } }
                  ]
            """;

        var result = Validate(Stages);

        Assert.Contains(
            result.Errors,
            e => e.Constraint!.Contains("changes the profile on edge electrode 'board'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The control: a stage that leaves the profile alone is not refused by that check.
    /// A guard that fired on every sequenced solve would pass the test above and break
    /// every sequenced mirror anyone ever writes.
    /// </summary>
    [Fact]
    public void AStageThatLeavesTheProfileAloneIsNotRefusedForIt()
    {
        const string Stages =
            """
            ,
                  "stages": [
                    { "name": "hold", "duration": { "value": 10, "unit": "us" }, "set": { "boardVolts": { "value": -400, "unit": "V" } } },
                    { "name": "again", "duration": { "value": 10, "unit": "us" }, "set": { "boardVolts": { "value": -400, "unit": "V" } } }
                  ]
            """;

        var result = Validate(Stages);

        Assert.DoesNotContain(
            result.Errors,
            e => e.Constraint!.Contains("changes the profile", StringComparison.Ordinal));
    }
}
