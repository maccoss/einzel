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
            phases, phase => phase.Name == "good" ? new(4.0, "flown", true, 2e-6) : new(0.0, "not measured", false, 0.0));

        // 4 s over 2 us is 2 s a microsecond, so the failed 3 us is charged 6 s.
        Assert.True(some.Measured);
        Assert.Equal(10.0, some.Seconds, 9);

        var none = EstimateCommand.PhasePilots(phases, _ => new(0.0, "not measured", false, 0.0));

        output.WriteLine(none.How);

        Assert.False(none.Measured);
        Assert.Contains("unknown, not zero", none.How, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pilot whose ion arrived before its phase was over cost what it did for the time it flew,
    /// so the rate another phase is charged at is per simulated second covered, not per phase.
    /// </summary>
    [Fact]
    public void ARateIsPerSecondTheFlightCovered()
    {
        var phases = new[]
        {
            new Core.Model.CompiledPhase("early", 2e-6, "trajectory", 2e-6, []),
            new Core.Model.CompiledPhase("bad", 3e-6, "trajectory", 5e-6, []),
        };

        // The ion arrived after 1 us of the 2 us phase, at a cost of 4 s: 4 s a microsecond, so
        // the failed 3 us is 12 s - not the 6 s that dividing by the whole phase made it.
        var cost = EstimateCommand.PhasePilots(
            phases, phase => phase.Name == "early" ? new(4.0, "arrived", true, 1e-6) : new(0.0, "not measured", false, 0.0));

        Assert.Equal(16.0, cost.Seconds, 9);
    }

    /// <summary>
    /// A hold after two short phases starts where the second ended, not at its own end less its
    /// duration, which here rounds to inside the extraction before it.
    /// </summary>
    /// <remarks>
    /// Against the same timeline with the extraction pushing nothing, so that whatever the
    /// extraction holds cannot matter to the hold: the two estimates must step the hold alike.
    /// Read in the extraction, the hold's first instant sees two megavolts a metre and is
    /// stepped thousands of times over.
    /// </remarks>
    [Fact]
    public void AHoldAfterShortPhasesIsReadFromWhereTheyEnded()
    {
        int Steps(int extractVolts, out Core.Model.CompiledModel model)
        {
            var path = Model("tube-plain", document =>
            {
                document["parameters"] = JsonNode.Parse("""{ "pushField": { "value": 0.0, "unit": "V/m" } }""");
                document["fields"]![0] = JsonNode.Parse("""{ "type": "uniform", "field": { "expression": [ "pushField", "0", "0" ] } }""");
                document["sequence"] = JsonNode.Parse("""
                    [ { "name": "settle", "duration": { "value": 0.1, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 1000, "unit": "V/m" } } },
                      { "name": "extract", "duration": { "value": 0.1, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": VOLTS, "unit": "V/m" } } },
                      { "name": "hold", "duration": { "value": 3.3, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
                    """.Replace("VOLTS", extractVolts.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            });

            model = Core.Model.ModelValidator.Validate(Io.ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path)).Model!;

            var estimate = EstimateCommand.Execute(path, calibrate: false);

            return int.Parse(
                System.Text.RegularExpressions.Regex.Match(estimate.Basis, @"about ([\d,]+) steps over").Groups[1].Value.Replace(",", ""),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        var pushed = Steps(2_000_000, out var model);
        var quiet = Steps(0, out _);

        // The hazard is real in these durations: the hold's end less its duration is inside the
        // extraction.
        Assert.True(
            model.Phases[2].EndsAtSeconds - model.Phases[2].DurationSeconds < model.Phases[1].EndsAtSeconds,
            "these durations round-trip, so the test no longer reaches the case it is about");

        output.WriteLine($"the hold estimated at {pushed} steps beside the extraction, {quiet} beside a quiet one");

        Assert.Equal(quiet, pushed);
    }

    /// <summary>
    /// A study over a model none of whose pilots could be flown carries that it was not measured
    /// and is gated, rather than billed without its flights.
    /// </summary>
    [Fact]
    public void AStudyWhosePilotsFailIsGatedNotBilledWithoutItsFlights()
    {
        var model = Model("tube-entered", document =>
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }"""));
        var study = Write("study", $$"""
            { "schemaVersion": "0.1", "name": "entered", "model": "{{Path.GetFileName(model)}}",
              "figureOfMerit": "transitTime", "ions": 9,
              "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
            """);

        EstimateOutcome costed;

        using (EstimateCommand.FailPilotsForTest())
        {
            costed = EstimateCommand.ForStudy(study, calibrate: true, threshold: 1e12);
        }

        output.WriteLine(costed.Basis);

        Assert.False(costed.TrajectoryMeasured);
        Assert.True(costed.AboveThreshold, "a study billed without its flights passed the gate");
        Assert.Contains("NOT MEASURED", costed.Basis, StringComparison.Ordinal);

        // The control: the same study with pilots that fly is measured and passes the same gate.
        var flown = EstimateCommand.ForStudy(study, calibrate: true, threshold: 1e12);

        Assert.True(flown.TrajectoryMeasured);
        Assert.False(flown.AboveThreshold);
    }

    /// <summary>
    /// A pilot that struck at once timed mostly its own setup, and the phases charged at its rate
    /// are not billed that setup per nanosecond of flight.
    /// </summary>
    [Fact]
    public void APilotThatStruckAtOnceDoesNotPriceTheRestAtItsSetup()
    {
        // Eight piloted phases of a millisecond, each struck after a nanosecond at 2 ms of setup,
        // and a ninth, shorter one charged at the dearest rate.
        var phases = Enumerable.Range(0, 9)
            .Select(i => new Core.Model.CompiledPhase($"p{i}", i < 8 ? 1e-3 : 0.5e-3, "trajectory", (i + 1) * 1e-3, []))
            .ToArray();

        var cost = EstimateCommand.PhasePilots(phases, _ => new(2e-3, "struck", true, 1e-9));

        output.WriteLine($"{cost.Seconds:G4} s: {cost.How}");

        // 16 ms piloted, and the ninth at 2 ms per tenth of a phase: 10 ms. At the struck flight's
        // own rate it would have been 1,000 s.
        Assert.Equal(0.026, cost.Seconds, 9);
    }

    /// <summary>
    /// A study over a sequenced model bills the flights its figure of merit flies, not the
    /// model's cloud: the flights per evaluation come from the study, as they do unsequenced.
    /// </summary>
    [Fact]
    public void ASequencedStudyBillsItsFiguresFlights()
    {
        var model = Model("tube-entered", document =>
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }"""));

        double PerEvaluation(int ions, out StudyEstimate study)
        {
            var path = Write($"study{ions}", $$"""
                { "schemaVersion": "0.1", "name": "entered", "model": "{{Path.GetFileName(model)}}",
                  "figureOfMerit": "transmission", "ions": {{ions}},
                  "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
                """);

            // Every flight costs a second, so the flight term is the count of flights billed.
            using (EstimateCommand.PilotsForTest(new(1.0, "a second, for a test", true, 1.0)))
            {
                var costed = EstimateCommand.ForStudy(path, calibrate: true, threshold: 1e12);

                study = costed.Study!;
                return costed.Study!.SecondsPerEvaluation;
            }
        }

        var many = PerEvaluation(1000, out var manyStudy);
        var few = PerEvaluation(10, out _);

        output.WriteLine($"{many:F3} s an evaluation at 1000 ions, {few:F3} at 10; {manyStudy.Members} members");

        Assert.Equal(1000, manyStudy.Members);
        Assert.Equal(990.0, many - few, 0);
    }

    /// <summary>
    /// A pilot that was measured and cost nothing to speak of is a measurement, not a failure:
    /// a cheap study is costed and passes the gate rather than being refused as unmeasured.
    /// </summary>
    [Fact]
    public void APilotThatCostNothingIsMeasured()
    {
        var model = Model("tube-entered", document =>
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }"""));
        var study = Write("study", $$"""
            { "schemaVersion": "0.1", "name": "entered", "model": "{{Path.GetFileName(model)}}",
              "figureOfMerit": "transitTime", "ions": 9,
              "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
            """);

        EstimateOutcome costed;

        using (EstimateCommand.PilotsForTest(new(0.0, "nothing, for a test", true, 1.0)))
        {
            costed = EstimateCommand.ForStudy(study, calibrate: true, threshold: 1e12);
        }

        output.WriteLine(costed.Basis);

        Assert.True(costed.TrajectoryMeasured);
        Assert.False(costed.AboveThreshold);
        Assert.DoesNotContain("NOT MEASURED", costed.Basis, StringComparison.Ordinal);
    }

    /// <summary>A test's pilot override, nested, gives back the one before it rather than none.</summary>
    [Fact]
    public void APilotOverrideRestoresTheOneBefore()
    {
        var phases = new[] { new Core.Model.CompiledPhase("p", 1e-6, "trajectory", 1e-6, []) };

        using (EstimateCommand.PilotsForTest(new(3.0, "outer", true, 1e-6)))
        {
            using (EstimateCommand.FailPilotsForTest())
            {
            }

            var model = Core.Model.ModelValidator.Validate(
                Io.ModelJson.Parse(File.ReadAllText(Model("tube-entered"))), null, _root).Model!;

            // Still the outer override: the flight is 3 s, not a real pilot and not a failure.
            var flown = EstimateCommand.Execute(Model("tube-entered"), calibrate: true, threshold: 1e12);

            Assert.True(flown.TrajectoryMeasured);
            Assert.Equal(3.0, flown.TrajectorySeconds, 9);
            Assert.NotEmpty(model.Phases);
        }
    }

    /// <summary>
    /// The drift limit is read only inside the diffusive phases: a phase with no length gives no
    /// instant, where its end less an ulp lay in the extraction before it.
    /// </summary>
    /// <remarks>
    /// Against the timeline rather than a model, because a model cannot carry such a phase as far
    /// as the estimate: its field refuses two phase boundaries at one instant.
    /// </remarks>
    [Fact]
    public void APhaseWithNoLengthIsNotReadInThePhaseBefore()
    {
        IReadOnlyList<Core.Model.CompiledRamp> none = [];
        Core.Model.CompiledPhase[] phases =
        [
            new("hold", 3e-6, "diffusion", 3e-6, none),
            new("extract", 3e-6, "trajectory", 6e-6, none),
            new("blip", 0.0, "diffusion", 6e-6, none),
            new("settle", 1e-6, "diffusion", 7e-6, none),
        ];

        var instants = EstimateCommand.DiffusiveInstants(phases);

        output.WriteLine(string.Join(", ", instants.Select(t => t.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));

        Assert.DoesNotContain(instants, t => t >= 3e-6 && t < 6e-6);
        Assert.Equal([0.0, Math.BitDecrement(3e-6), 6e-6, Math.BitDecrement(7e-6)], instants);
    }

    /// <summary>
    /// A study over a sequenced model bills what one evaluation flies: a figure read off the
    /// sequenced run flies the sequence's cloud, and any other flies its own ions outside it.
    /// </summary>
    /// <param name="figure">The study's figure of merit.</param>
    /// <param name="flights">The flights one evaluation makes.</param>
    [Theory]
    [InlineData("transitTime", 2000)]
    [InlineData("flightTime", 3)]
    public void ASequencedStudyBillsTheFlightsAnEvaluationMakes(string figure, int flights)
    {
        var model = Model("tube-entered", document =>
        {
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }""");
            document["source"]!["cloud"]!["ions"] = 2000;
        });
        var study = Write("study", $$"""
            { "schemaVersion": "0.1", "name": "entered", "model": "{{Path.GetFileName(model)}}",
              "figureOfMerit": "{{figure}}", "ions": 9,
              "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
            """);

        EstimateOutcome costed;

        using (EstimateCommand.PilotsForTest(new(1.0, "a second, for a test", true, 1.0)))
        {
            costed = EstimateCommand.ForStudy(study, calibrate: true, threshold: 1e12);
        }

        output.WriteLine(costed.Basis);

        Assert.Equal(flights, costed.Study!.Members);
    }

    /// <summary>
    /// An extension objective is billed by what its manifest declares: one handed a transit is
    /// handed a whole sequenced run per evaluation, and flies the sequence's cloud.
    /// </summary>
    /// <param name="declared">The built-in figure the extension's manifest declares.</param>
    /// <param name="flights">The flights one evaluation makes.</param>
    [Theory]
    [InlineData("transitTime", 2000)]
    [InlineData("flightTime", 9)]
    public void AnExtensionIsBilledByTheFiguresItDeclares(string declared, int flights)
    {
        // A project: the extension lives in its extensions directory, found by the models one.
        Directory.CreateDirectory(Path.Combine(_root, "models"));
        Assert.Equal(0, Program.Main(["ext", "register", "transitwatch", "--project", _root]));

        var manifestPath = Path.Combine(_root, "extensions", "transitwatch", "extension.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["figures"] = new JsonArray(declared);
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        var model = Model("tube-entered", document =>
        {
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }""");
            document["source"]!["cloud"]!["ions"] = 2000;
        });
        var study = Write("study", $$"""
            { "schemaVersion": "0.1", "name": "entered", "model": "{{Path.GetFileName(model)}}",
              "figureOfMerit": "ext:transitwatch", "ions": 9,
              "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
            """);

        EstimateOutcome costed;

        using (EstimateCommand.PilotsForTest(new(1.0, "a second, for a test", true, 1.0)))
        {
            costed = EstimateCommand.ForStudy(study, calibrate: true, threshold: 1e12);
        }

        output.WriteLine($"declares {declared}: {costed.Study!.Members} flights an evaluation");

        Assert.Equal(flights, costed.Study!.Members);
    }

    /// <summary>
    /// Under space charge a sequenced evaluation advances one packet, whatever the figure asks
    /// for, and is not re-billed as that many independent flights.
    /// </summary>
    [Fact]
    public void ASpaceChargeSequenceIsBilledAsItsPacket()
    {
        var model = Model("tube-entered", document =>
        {
            document["parameters"] = JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }""");
            document["transport"]!["spaceCharge"] = "direct";
        });

        double PerEvaluation(int ions, out StudyEstimate estimated)
        {
            var path = Write($"study{ions}", $$"""
                { "schemaVersion": "0.1", "name": "charged", "model": "{{Path.GetFileName(model)}}",
                  "figureOfMerit": "transmission", "ions": {{ions}},
                  "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
                """);

            using (EstimateCommand.PilotsForTest(new(1.0, "a second, for a test", true, 1.0)))
            {
                estimated = EstimateCommand.ForStudy(path, calibrate: true, threshold: 1e12).Study!;
            }

            return estimated.SecondsPerEvaluation;
        }

        var many = PerEvaluation(1000, out var manyStudy);
        var few = PerEvaluation(10, out _);

        output.WriteLine($"{many:F3} s an evaluation at 1000 ions, {few:F3} at 10, {manyStudy.Members} flights");

        Assert.Equal(1, manyStudy.Members);
        Assert.True(Math.Abs(many - few) < 1.0, $"a packet was billed as independent flights: {many - few:F1} s apart");
    }

    /// <summary>
    /// A pilot that cannot be flown through the coarsened field is flown again at the declared
    /// resolution before the gate refuses anything, and the basis says why.
    /// </summary>
    [Fact]
    public void ACoarsenedPilotThatFailsIsFlownAsDeclared()
    {
        var model = Model("tube-entered");
        var failed = new EstimateCommand.Pilot(0.0, "not measured: no field at that coarsening, for a test", false, 0.0);
        var flown = new EstimateCommand.Pilot(0.5, "half a second, for a test", true, 1.0);

        EstimateOutcome retried;

        using (EstimateCommand.PilotsForTestBy(coarsening => coarsening > 1.0 ? failed : flown))
        {
            retried = EstimateCommand.Execute(model, calibrate: true, threshold: 1e12);
        }

        output.WriteLine(retried.Basis);

        Assert.True(retried.TrajectoryMeasured);
        Assert.False(retried.AboveThreshold);
        Assert.Contains("flown at the declared resolution", retried.Basis, StringComparison.Ordinal);

        // The control: where the declared resolution fails too, the cost is unknown and gated.
        EstimateOutcome neither;

        using (EstimateCommand.PilotsForTestBy(_ => failed))
        {
            neither = EstimateCommand.Execute(model, calibrate: true, threshold: 1e12);
        }

        Assert.False(neither.TrajectoryMeasured);
        Assert.True(neither.AboveThreshold);
        Assert.Contains("nor at the declared resolution", neither.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed coarse pilot is not flown again at the declared resolution where that solve is
    /// itself dear: the estimate would be doing the run's whole solve to cost it. The failure is
    /// reported, and the gate refuses.
    /// </summary>
    /// <remarks>
    /// The Astral's declared volume solve takes seconds, over the second an estimate spends on a
    /// pilot. Asked of which coarsenings a pilot was requested at, so a declared-resolution attempt
    /// shows whatever it reports.
    /// </remarks>
    [Fact]
    public void ADearDeclaredSolveIsNotFlownForAPilot()
    {
        var model = Write("astral-" + Guid.NewGuid().ToString("N")[..6], Einzel.Library.DeviceTemplates.Read("astral-3d"));
        var failed = new EstimateCommand.Pilot(0.0, "not measured: no field at that coarsening, for a test", false, 0.0);
        var asked = new System.Collections.Concurrent.ConcurrentBag<double>();

        EstimateOutcome outcome;

        using (EstimateCommand.PilotsForTestBy(coarsening =>
        {
            asked.Add(coarsening);
            return failed;
        }))
        {
            outcome = EstimateCommand.Execute(model, calibrate: true, threshold: 1e12);
        }

        output.WriteLine(outcome.Basis);

        Assert.DoesNotContain(1.0, asked);
        Assert.False(outcome.TrajectoryMeasured);
        Assert.True(outcome.AboveThreshold);
        Assert.Contains("not flown at the declared resolution", outcome.Basis, StringComparison.Ordinal);
        Assert.DoesNotContain("FLOOR", DeclaredPart(outcome.Basis), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pilot flown at the declared resolution says it was flown through the run's own field, not
    /// that it is a floor from a coarser one.
    /// </summary>
    [Fact]
    public void ADeclaredPilotIsNotCalledAFloor()
    {
        // A ceiling short enough that the pilot's window ends before the ion arrives, so its cost
        // is scaled from part of the flight - the branch whose basis says what field it was flown in.
        var lens = Einzel.Library.DeviceTemplates.Read("einzel-lens");
        const string Declared = "\"maximumFlightTime\": { \"value\": 1, \"unit\": \"ms\" }";

        Assert.Contains(Declared, lens, StringComparison.Ordinal);

        var model = Write(
            "lens-" + Guid.NewGuid().ToString("N")[..6],
            lens.Replace(Declared, "\"maximumFlightTime\": { \"value\": 20, \"unit\": \"us\" }", StringComparison.Ordinal));
        var failed = new EstimateCommand.Pilot(0.0, "not measured: no field at that coarsening, for a test", false, 0.0);

        EstimateOutcome outcome;

        using (EstimateCommand.PilotsForTestBy(coarsening => coarsening > 1.0 ? failed : null))
        {
            outcome = EstimateCommand.Execute(model, calibrate: true, threshold: 1e12);
        }

        output.WriteLine(outcome.Basis);

        Assert.True(outcome.TrajectoryMeasured);
        Assert.Contains("flown at the declared resolution", outcome.Basis, StringComparison.Ordinal);
        Assert.Contains("solved at the declared mesh", outcome.Basis, StringComparison.Ordinal);
        Assert.DoesNotContain("FLOOR", DeclaredPart(outcome.Basis), StringComparison.Ordinal);
    }

    /// <summary>
    /// What a declared-resolution solve is estimated to cost counts one solve per basis channel,
    /// which a driven geometry has more than one of.
    /// </summary>
    /// <remarks>
    /// The funnel's RF and its DC chain put the rings up and down in different proportions, so
    /// they are two patterns and two solves over the same mesh; one solve per element billed half
    /// of it.
    /// </remarks>
    [Fact]
    public void ADeclaredSolveIsChargedPerBasisChannel()
    {
        var document = Einzel.Library.DeviceTemplates.Read("ion-funnel");
        var model = Core.Model.ModelValidator.Validate(Io.ModelJson.Parse(document)).Model!;
        var plane = model.Fields.Single(f => f.Solve is not null).Solve!;
        var channels = Fields.Solved.GeometryBuilder.SolveChannels(plane).Count;
        var nodes = Fields.Solved.GeometryBuilder.BuildGrid(plane).NodeCount;

        var seconds = EstimateCommand.DeclaredSolveSeconds(model, 1e6, 1e6);

        output.WriteLine($"{nodes:N0} nodes, {channels} channels: {seconds:G6} s at a second a node");

        Assert.True(channels > 1, "the geometry must solve more than one channel for this to discriminate");
        Assert.Equal((double)nodes * channels, seconds, 1e-9 * nodes * channels);
    }

    // The basis from where it says the pilot was flown as declared.
    private static string DeclaredPart(string basis)
    {
        var at = basis.IndexOf("declared resolution", StringComparison.Ordinal);
        return at < 0 ? string.Empty : basis[at..];
    }

    /// <summary>
    /// A phase whose pilot could not be measured is charged at the dearest rate measured, per
    /// simulated second the measured pilot actually flew rather than per second of its phase.
    /// </summary>
    [Fact]
    public void AnUnmeasuredPhaseIsChargedPerSecondFlown()
    {
        var phases = new[]
        {
            new Core.Model.CompiledPhase("early", 2e-6, "trajectory", 2e-6, []),
            new Core.Model.CompiledPhase("bad", 3e-6, "trajectory", 5e-6, []),
        };

        var cost = EstimateCommand.PhasePilots(
            phases, phase => phase.Name == "early" ? new(4.0, "arrived", true, 1e-6) : new(0.0, "not measured", false, 0.0));

        // Four seconds for the microsecond flown before the ion arrived is four million seconds a
        // simulated second, and the failed phase's three microseconds charged at that: sixteen.
        // Per second of its phase it would have been ten.
        Assert.Equal(16.0, cost.Seconds, 9);
    }

    /// <summary>Two phases declared alike are two phases, and both are piloted.</summary>
    [Fact]
    public void PhasesDeclaredAlikeAreEachPiloted()
    {
        IReadOnlyList<Core.Model.CompiledRamp> none = [];
        var alike = new Core.Model.CompiledPhase("same", 1e-6, "trajectory", 1e-6, none);
        var flown = 0;

        var cost = EstimateCommand.PhasePilots([alike, alike], _ => { flown++; return new(1.0, "flown", true, 1e-6); });

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
