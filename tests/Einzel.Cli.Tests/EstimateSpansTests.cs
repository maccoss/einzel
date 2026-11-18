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
        Assert.Contains("scaled to its 0.05 us, flown from the source through phase 'fly'", entered.Basis, StringComparison.Ordinal);
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
            System.Text.RegularExpressions.Regex.Match(basis, @"[\d,]+ steps over [\d.Ee+-]+ us").Value;

        var gentle = EstimateCommand.Execute(Extracting(2_000.0), calibrate: true);
        var strong = EstimateCommand.Execute(Extracting(2_000_000.0), calibrate: true);

        output.WriteLine(Pilot(gentle.Basis));
        output.WriteLine(Pilot(strong.Basis));

        Assert.Contains("phase 'extract'", gentle.Basis, StringComparison.Ordinal);
        Assert.NotEmpty(Pilot(gentle.Basis));
        Assert.NotEqual(Pilot(gentle.Basis), Pilot(strong.Basis));
    }

    /// <summary>
    /// Every trajectory phase of a sequence is piloted through its own field, not only the first:
    /// a cheap hold flown as trajectories before an expensive extraction bills the extraction too.
    /// </summary>
    [Fact]
    public void EveryTrajectoryPhaseIsPiloted()
    {
        var model = Model("tube-plain", document =>
        {
            document["parameters"] = JsonNode.Parse("""{ "pushField": { "value": 0.0, "unit": "V/m" } }""");
            document["fields"]![0] = JsonNode.Parse("""{ "type": "uniform", "field": { "expression": [ "pushField", "0", "0" ] } }""");
            document["sequence"] = JsonNode.Parse("""
                [ { "name": "cool", "duration": { "value": 1, "unit": "us" } },
                  { "name": "hold", "duration": { "value": 2, "unit": "us" }, "mode": "trajectory" },
                  { "name": "extract", "duration": { "value": 3, "unit": "us" }, "mode": "trajectory",
                    "set": { "pushField": { "value": 2000000, "unit": "V/m" } } } ]
                """);
        });

        var estimate = EstimateCommand.Execute(model, calibrate: true);

        output.WriteLine(estimate.Basis);

        Assert.Contains("phase 'hold'", estimate.Basis, StringComparison.Ordinal);
        Assert.Contains("phase 'extract'", estimate.Basis, StringComparison.Ordinal);
        Assert.Contains("flown from the source rather than from where the packet is", estimate.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A diffusive hold before an extraction is stepped at the hold's own stability limit: the
    /// estimate's step count for it is the run's, however hard the extraction pushes.
    /// </summary>
    [Fact]
    public void AHoldIsSteppedAtItsOwnLimitNotTheExtractions()
    {
        // Both orders: a hold before the extraction, where reading every boundary reached the
        // extraction's field; and one after it, where reading the bare field at zero did.
        foreach (var (first, second, hold) in new[] { ("hold", "extract", 0), ("extract", "hold", 1) })
        {
            var path = Model("tube-plain", document =>
            {
                document["parameters"] = JsonNode.Parse("""{ "pushField": { "value": 0.0, "unit": "V/m" } }""");
                document["fields"]![0] = JsonNode.Parse("""{ "type": "uniform", "field": { "expression": [ "pushField", "0", "0" ] } }""");
                document["sequence"] = JsonNode.Parse(
                    "[ " + Phase(first) + ", " + Phase(second) + " ]");
            });

            var estimate = EstimateCommand.Execute(path, calibrate: false);
            var estimated = int.Parse(
                System.Text.RegularExpressions.Regex.Match(estimate.Basis, @"about ([\d,]+) steps over").Groups[1].Value.Replace(",", ""),
                System.Globalization.CultureInfo.InvariantCulture);

            var model = Core.Model.ModelValidator.Validate(Io.ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path)).Model!;
            var (field, _) = Fields.FieldAssembly.BuildReported(model);
            var run = SequencedRun.Execute(model, field, Io.GasFlowImport.Resolve(model.Gas, Path.GetDirectoryName(path)!));
            var stepped = run.Phases[hold].DensitySteps;

            output.WriteLine($"{first} then {second}: estimated {estimated} steps for the hold; the run took {stepped}");

            Assert.Equal(stepped, estimated);
        }

        static string Phase(string name) => name == "hold"
            ? """{ "name": "hold", "duration": { "value": 3, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } }"""
            : """{ "name": "extract", "duration": { "value": 3, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 2000000, "unit": "V/m" } } }""";
    }

    /// <summary>
    /// A phase whose pilot fails is not billed nothing: it is charged at the dearest rate another
    /// pilot measured, and where none measured anything the cost is reported as unknown.
    /// </summary>
    [Fact]
    public void AFailedPilotIsNeverBilledNothing()
    {
        var phases = new[]
        {
            new Core.Model.CompiledPhase("good", 2e-6, "trajectory", 2e-6, []),
            new Core.Model.CompiledPhase("bad", 3e-6, "trajectory", 5e-6, []),
        };

        var some = EstimateCommand.PhasePilots(
            phases, phase => phase.Name == "good" ? (4.0, "flown", true) : (0.0, "not measured", false));

        // 4 s over 2 us is 2 s a microsecond, so the failed 3 us is charged 6 s.
        Assert.True(some.Measured);
        Assert.Equal(10.0, some.Seconds, 9);

        var none = EstimateCommand.PhasePilots(phases, _ => (0.0, "not measured", false));

        output.WriteLine(none.How);

        Assert.False(none.Measured);
        Assert.Contains("unknown, not zero", none.How, StringComparison.Ordinal);
    }

    /// <summary>Two phases declared alike are two phases, and both are piloted.</summary>
    [Fact]
    public void PhasesDeclaredAlikeAreEachPiloted()
    {
        IReadOnlyList<Core.Model.CompiledRamp> none = [];
        var alike = new Core.Model.CompiledPhase("same", 1e-6, "trajectory", 1e-6, none);
        var flown = 0;

        var cost = EstimateCommand.PhasePilots([alike, alike], _ => { flown++; return (1.0, "flown", true); });

        Assert.Equal(2, flown);
        Assert.Equal(2.0, cost.Seconds, 9);
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

    /// <summary>
    /// Calibrated, a study over a sequenced model re-flies its trajectory phases phase by phase,
    /// rather than flying from zero over the whole flight or leaving them at the declared values.
    /// </summary>
    [Fact]
    public void ACalibratedStudyOverASequencedModelPilotsItsPhases()
    {
        var model = Model("tube-entered", document =>
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }"""));
        var study = Write("study", $$"""
            { "schemaVersion": "0.1", "name": "entered", "model": "{{Path.GetFileName(model)}}",
              "figureOfMerit": "transitTime", "ions": 9,
              "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
            """);

        var costed = EstimateCommand.ForStudy(study, calibrate: true);

        output.WriteLine(costed.Basis);

        Assert.Contains("trajectory phases piloted phase by phase", costed.Basis, StringComparison.Ordinal);
        Assert.True(costed.TrajectorySeconds > 0.0, "the study's trajectory phases were not flown");
    }
}
