using System.Text.Json.Nodes;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A sequenced run is costed phase by phase, each mode over its own phases, and every part of
/// the estimate - the number, the basis and a study's evaluation - reads that one decomposition.
/// </summary>
/// <remarks>
/// The drift tube's field is analytic, so with calibration off nothing is solved and nothing is
/// piloted, and an estimate's seconds are the density's stepping alone: a fraction of the flight
/// stepped is that fraction of the seconds, to within a step - on a 513 by 513 grid a few
/// hundred of them, so a fraction is resolved to a per cent.
/// </remarks>
public sealed class EstimateSpansTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-estimate-spans", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Write(string name, string document)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, name + ".json");

        File.WriteAllText(path, document);

        return path;
    }

    private string Model(string kind, Action<JsonNode>? change = null)
    {
        var document = JsonNode.Parse(RunMemoryCases.Document(kind, 512))!;

        change?.Invoke(document);

        return Write(kind + Guid.NewGuid().ToString("N")[..6], document.ToJsonString());
    }

    /// <summary>
    /// A trajectory model that enters a diffusive phase steps the density over that phase only,
    /// not over the whole flight beside a trajectory pilot scaled over the whole flight too.
    /// </summary>
    [Fact]
    public void ADiffusivePhaseIsSteppedOverItsOwnDurationAlone()
    {
        var plain = EstimateCommand.Execute(Model("tube-plain"), calibrate: false);
        var entered = EstimateCommand.Execute(Model("tube-entered"), calibrate: false);

        var expected = (RunMemoryCases.FlightMicroseconds - 0.05) / RunMemoryCases.FlightMicroseconds;
        var ratio = entered.Seconds / plain.Seconds;

        output.WriteLine($"plain {plain.Seconds:G6} s, entered {entered.Seconds:G6} s, ratio {ratio:F4} against {expected:F4}");
        output.WriteLine(entered.Basis);

        Assert.InRange(ratio, expected - 0.02, expected + 0.02);
        Assert.Contains("5.95 us of diffusive phases", entered.Basis, StringComparison.Ordinal);

        // Half and half, where stepping the whole flight would be twice the answer.
        var halves = EstimateCommand.Execute(
            Model("tube-entered", document => document["sequence"] = JsonNode.Parse("""
                [ { "name": "fly", "duration": { "value": 3, "unit": "us" }, "mode": "trajectory" },
                  { "name": "drift", "duration": { "value": 3, "unit": "us" }, "mode": "diffusion" } ]
                """)),
            calibrate: false);

        output.WriteLine($"half as trajectories: {halves.Seconds / plain.Seconds:F4} of the diffusive tube");

        Assert.InRange(halves.Seconds / plain.Seconds, 0.48, 0.52);
        Assert.Contains("0.05 us as trajectories and 5.95 us as a density", entered.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A diffusive model whose sequence leaves the density for a trajectory phase is not billed
    /// density steps through that phase.
    /// </summary>
    [Fact]
    public void ADiffusiveModelIsNotSteppedThroughItsTrajectoryPhases()
    {
        var plain = EstimateCommand.Execute(Model("tube-plain"), calibrate: false);
        var leaving = EstimateCommand.Execute(
            Model("tube-plain", document => document["sequence"] = JsonNode.Parse("""
                [ { "name": "drift", "duration": { "value": 3, "unit": "us" } },
                  { "name": "fly", "duration": { "value": 3, "unit": "us" }, "mode": "trajectory" } ]
                """)),
            calibrate: false);

        var ratio = leaving.Seconds / plain.Seconds;

        output.WriteLine($"plain {plain.Seconds:G6} s, leaving {leaving.Seconds:G6} s, ratio {ratio:F4}");
        output.WriteLine(leaving.Basis);

        Assert.InRange(ratio, 0.48, 0.52);
        Assert.Contains("3 us of diffusive phases", leaving.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// Calibrated, the trajectory share keeps its line in the basis beside the density's, and the
    /// pilot is scaled to the time the run spends as trajectories.
    /// </summary>
    [Fact]
    public void BothSharesKeepTheirLineInTheBasis()
    {
        var entered = EstimateCommand.Execute(Model("tube-entered"), calibrate: true);

        output.WriteLine(entered.Basis);

        Assert.Contains("Trajectory: ", entered.Basis, StringComparison.Ordinal);
        Assert.Contains("diffusive run: ", entered.Basis, StringComparison.Ordinal);
        Assert.DoesNotContain("scaled to the declared", entered.Basis, StringComparison.Ordinal);
        Assert.Contains("the 0.05 us the run spends as trajectories", entered.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A hold-then-extract sequence pilots its trajectories through the extraction, not through
    /// the hold before it: two models differing only in the extraction field fly different
    /// pilots.
    /// </summary>
    /// <remarks>
    /// Flown from zero, both pilots would cross the same hold with nothing pushing, take the same
    /// steps, and be scaled to the extraction alike - the pilot timing a phase the trajectories
    /// never fly.
    /// </remarks>
    [Fact]
    public void AHoldThenExtractIsPilotedThroughTheExtraction()
    {
        string Extracting(double voltsPerMetre) => Model("tube-plain", document =>
        {
            document["parameters"] = JsonNode.Parse("""{ "pushField": { "value": 0.0, "unit": "V/m" } }""");
            document["fields"]![0] = JsonNode.Parse("""{ "type": "uniform", "field": { "expression": [ "pushField", "0", "0" ] } }""");
            document["sequence"] = JsonNode.Parse(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $$"""
                [ { "name": "hold", "duration": { "value": 3, "unit": "us" } },
                  { "name": "extract", "duration": { "value": 3, "unit": "us" }, "mode": "trajectory",
                    "set": { "pushField": { "value": {{voltsPerMetre}}, "unit": "V/m" } } } ]
                """));
        });

        // What the pilot flew: its steps and the time they covered, which the field decides.
        static string Pilot(string basis) =>
            System.Text.RegularExpressions.Regex.Match(basis, @"Trajectory: [\d,]+ steps over [\d.Ee+-]+ us").Value;

        var gentle = EstimateCommand.Execute(Extracting(2_000.0), calibrate: true);
        var strong = EstimateCommand.Execute(Extracting(2_000_000.0), calibrate: true);

        output.WriteLine(Pilot(gentle.Basis));
        output.WriteLine(Pilot(strong.Basis));

        Assert.Contains("phase 'extract'", gentle.Basis, StringComparison.Ordinal);
        Assert.NotEmpty(Pilot(gentle.Basis));
        Assert.NotEqual(Pilot(gentle.Basis), Pilot(strong.Basis));
    }

    /// <summary>
    /// A study over a trajectory model that enters a diffusive phase is one whole sequenced run an
    /// evaluation, whose flights are already in the model's figure and are not added again.
    /// </summary>
    [Fact]
    public void AStudyOverAnEnteringModelCountsItsFlightsOnce()
    {
        var model = Model("tube-entered", document =>
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }"""));
        var study = Write("study", $$"""
            { "schemaVersion": "0.1", "name": "entered", "model": "{{Path.GetFileName(model)}}",
              "figureOfMerit": "transitTime", "ions": 9,
              "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
            """);

        var costed = EstimateCommand.ForStudy(study, calibrate: false);

        output.WriteLine(costed.Basis);

        Assert.Contains("is one whole run of the model", costed.Basis, StringComparison.Ordinal);
        Assert.Equal(0.0, costed.TrajectorySeconds);
    }
}
