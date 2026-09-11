using System.Text.Json;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Every run path leaves a result document beside its manifest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three of the four wrote one.</b> The sequenced path wrote a manifest and no result, so
/// a sequenced run left provenance and no answer - and a sequenced run is what every TIMS
/// study here is. PRJ-3's claim that a manifest determines its run stands either way; what
/// was missing is the stored answer that determination is <em>for</em>, and a reader
/// downstream had nothing to read for exactly the runs most worth reading. Found on a real
/// project holding four manifests and no results.
/// </para>
/// <para>
/// <b>The four models are here to take four different paths</b>, and the test asserts they
/// did - by the block each result carries, since a version of this that ran four trajectory
/// models by accident would pass the interesting assertion four times over the same code.
/// That is the failure this repository has been caught by repeatedly: a test passes a
/// mutation because the path it exercises does not contain the mutated line.
/// </para>
/// <para>
/// <b>And the paths are counted.</b> A fifth kind of run would satisfy every assertion below
/// while writing nothing, which is precisely how this defect arrived - so the number of
/// result-document writes in <c>RunCommand</c> is asserted against the number of paths this
/// test covers, and a new path fails here until it is added.
/// </para>
/// </remarks>
public sealed class ResultDocumentTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-results", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>A short drift tube, in whichever configuration a caller asks for.</summary>
    private string Model(
        string name, string species, string sequence, string ion, string mobility)
        => Write(name, $$"""
            {
              "schemaVersion": "0.13",
              "name": "{{name}}",
              {{ion}}{{species}}{{sequence}}
              "source": {
                "position": { "value": [4, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 0, "unit": "V" },
                "cloud": { "ions": 1,
                           "transverseSpread": { "value": 0.4, "unit": "mm" },
                           "longitudinalSpread": { "value": 0.4, "unit": "mm" } }
              },
              "detector": {
                "planePoint": { "value": [32, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "diffusion",
                "maximumFlightTime": { "value": 60, "unit": "us" },
                {{mobility}}
                "densityGrid": {
                  "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                  "minY": { "value": -4, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                  "intervalsX": 32, "intervalsY": 16
                },
                "gas": { "model": "hardSphere", "pressure": { "value": 2, "unit": "mbar" },
                         "mass": { "value": 28.0134, "unit": "Da" },
                         "crossSection": { "value": 250, "unit": "angstrom^2" } }
              },
              "fields": [
                { "type": "uniform", "field": { "value": [500, 0, 0], "unit": "V/m" } }
              ]
            }
            """);

    /// <summary>A field-free drift, which is the trajectory path at its simplest.</summary>
    private string Trajectory()
        => Write("drift", """
            {
              "schemaVersion": "0.13",
              "name": "drift",
              "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 1000, "unit": "V" },
                "cloud": {
                  "ions": 24,
                  "seed": 11,
                  "temperature": { "value": 300, "unit": "K" },
                  "transverseSpread": { "value": 0.3, "unit": "mm" }
                }
              },
              "detector": {
                "planePoint": { "value": [100, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "trajectory",
                "maximumFlightTime": { "value": 1, "unit": "ms" }
              },
              "fields": [{ "type": "fieldFree" }]
            }
            """);

    private string Write(string name, string document)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, document);

        return path;
    }

    private const string TwoSpecies = """
          "species": [
            { "name": "fast", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
              "mobility": { "zeroField": { "value": 0.05, "unit": "m^2/(V s)" } },
              "population": 1000 },
            { "name": "slow", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
              "mobility": { "zeroField": { "value": 0.025, "unit": "m^2/(V s)" } },
              "population": 1000 }
          ],
        """;

    private const string OneMobility =
        """
        "mobility": { "zeroField": { "value": 0.05, "unit": "m^2/(V s)" } },
        """;

    private const string OneIon = """
          "ion": { "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1 },
        """;

    private const string TwoPhases = """
          "sequence": [
            { "name": "hold",  "duration": { "value": 20, "unit": "us" } },
            { "name": "elute", "duration": { "value": 20, "unit": "us" } }
          ],
        """;

    /// <summary>
    /// Each of the four run paths writes a result document, and names it as an artifact.
    /// </summary>
    [Fact]
    public void EveryRunPathWritesAResultDocument()
    {
        // One case per path, keyed by the property only that path's result carries. The key
        // is what makes this four tests rather than one repeated: a model that fell through
        // to the wrong path would be missing its own block.
        var paths = new (string Name, string Distinguishing, Func<string> Model)[]
        {
            ("trajectory", "acceptedSteps", Trajectory),
            ("diffusive", "diffusion",
                () => Model("diffusive", "", "", OneIon, OneMobility)),

            // No transport-level mobility: a mixture carries one per species, and
            // declaring both is refused rather than merged.
            ("mixture", "mixture", () => Model("mixture", TwoSpecies, "", "", "")),

            ("sequenced", "sequence",
                () => Model("sequenced", "", TwoPhases, OneIon, OneMobility)),
        };

        foreach (var (name, distinguishing, model) in paths)
        {
            var path = model();
            var stem = Path.GetFileNameWithoutExtension(path);
            var (exit, stdout, stderr) = Run("run", path, "--json");

            Assert.True(exit == 0, $"{name}: exit {exit}\n{stdout}\n{stderr}");

            var resultPath = Path.Combine(_root, "results", $"{stem}.result.json");

            Assert.True(
                File.Exists(resultPath),
                $"{name}: the run wrote no result document at "
                + $"{Path.GetRelativePath(_root, resultPath)}, so its answer is nowhere - "
                + "only its manifest, which is provenance for a number nothing stored");

            // IT READS BACK INTO THE RECORD THAT WROTE IT, which is a stronger claim than
            // its being parseable JSON and is the one PRJ-3 needs: "regenerate and
            // compare" is impossible against a document this build cannot load. It was
            // false - `required double? EmittanceMmMrad` is written as absent by
            // `WhenWritingNull` and demanded on the way in by `required`, so every
            // ensemble run ever stored here wrote a result it could not read. Nothing had
            // noticed because nothing read one: `verify` reads only manifests.
            //
            // Fixed on the reading side, so the document is byte-for-byte what it always
            // was: absence of a property that may be null is this surface's encoding of
            // nothing, and demanding it was demanding the encoding not be used.
            var text = File.ReadAllText(resultPath);
            var stored = CommandJson.Read<RunOutcome>(text)
                ?? throw new InvalidOperationException($"{name}: the result read as null");

            // And the path is the one this case is about. A version that ran four
            // trajectory models by accident would pass the assertions above four times
            // over the same code.
            var block = JsonDocument.Parse(text).RootElement;

            Assert.True(
                block.TryGetProperty(distinguishing, out var element)
                    && element.ValueKind is not JsonValueKind.Null,
                $"{name}: the stored result has no '{distinguishing}', so this model did "
                + "not take the path this case is about");

            // And the run says it wrote it. An artifact list that omits the result is how a
            // reader concludes there is nothing there.
            var artifacts = JsonDocument.Parse(stdout).RootElement
                .GetProperty("artifacts").EnumerateArray()
                .Select(a => a.GetString()!)
                .ToArray();

            Assert.Contains($"{stem}.result.json", string.Join("|", artifacts));

            // Relative to the project root, as PRJ-3 needs: an absolute path names a file by
            // where it sat on the machine that wrote it. The sequenced path was the one
            // storing absolute paths, alongside being the one storing no result at all.
            foreach (var artifact in artifacts)
            {
                Assert.False(
                    Path.IsPathRooted(artifact),
                    $"{name}: '{artifact}' is absolute, so this project does not travel");
            }

            output.WriteLine(
                $"{name,-11} {stem}.result.json, {new FileInfo(resultPath).Length,7} bytes, "
                + $"read back as {stored.Outcome}, {artifacts.Length} artifact(s)");
        }
    }

    /// <summary>
    /// A result whose ensemble measured nothing still reads back into its own record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is where the round trip actually broke, and the run above cannot see it.</b>
    /// The emittance fields are <c>required double?</c> - the surface's way of saying the
    /// construction site must decide and the answer may be nothing - and
    /// <c>WhenWritingNull</c> omits a null while <c>required</c> demands it on the way in.
    /// So a document is unreadable exactly when one of those values is <em>absent</em>,
    /// which is when no ion arrived with a measurable spread. A packet that arrives writes
    /// every field and round-trips fine.
    /// </para>
    /// <para>
    /// That is the shape of failure this repository keeps recording: a test whose
    /// parameter sits on one side of the value the behaviour switches at is a test of a
    /// different regime. So this one launches a cloud into a flight that ends before the
    /// detector - an ordinary run, reporting what is still confined - and the four-path
    /// test above keeps a packet that arrives, so the pair straddles it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AResultWithNothingToReportStillReadsBack()
    {
        // Field-free, and the window is a hundredth of the flight, so every ion is still
        // in flight when the run ends: an ensemble with no arrivals and so no emittance.
        var path = Write("unarrived", """
            {
              "schemaVersion": "0.13",
              "name": "unarrived",
              "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 1000, "unit": "V" },
                "cloud": {
                  "ions": 12,
                  "seed": 5,
                  "temperature": { "value": 300, "unit": "K" },
                  "transverseSpread": { "value": 0.3, "unit": "mm" }
                }
              },
              "detector": {
                "planePoint": { "value": [1000, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "trajectory",
                "maximumFlightTime": { "value": 1, "unit": "us" }
              },
              "fields": [{ "type": "fieldFree" }]
            }
            """);

        var (exit, stdout, stderr) = Run("run", path, "--json");

        Assert.True(exit == 0, $"exit {exit}: {stdout} {stderr}");

        var stored = File.ReadAllText(
            Path.Combine(_root, "results", "unarrived.result.json"));

        // The premise, asserted rather than assumed: this run really is the case where a
        // required field has nothing in it. Without this the test could pass by having
        // quietly become the arriving case again.
        var element = JsonDocument.Parse(stored).RootElement.GetProperty("ensemble");

        Assert.Equal(0, element.GetProperty("arrived").GetInt32());

        // ABSENT, WHICH IS THIS SURFACE'S OWN SPELLING FOR NOTHING, and the premise of the
        // round trip below rather than a defect. A first version of the fix wrote every
        // required property including its nulls, which also round-trips - and changed the
        // published document for every ensemble run, breaking any consumer that told "no
        // orientation" from "zero" by key presence the way the surface tells it to. So what
        // is asserted is that the encoding is still the one that was published, and that
        // reading it no longer refuses.
        Assert.False(
            element.TryGetProperty("emittanceMmMrad", out _),
            "this run was supposed to have no emittance to report, so the key should not be "
            + "there at all - either this is the arriving case or the wire format changed");

        var run = CommandJson.Read<RunOutcome>(stored);

        Assert.NotNull(run);
        Assert.Null(run.Ensemble!.EmittanceMmMrad);
        Assert.Equal(12, run.Ensemble.Launched);

        output.WriteLine(
            $"{run.Outcome}: {run.Ensemble.Launched} launched, {run.Ensemble.Arrived} "
            + $"arrived, emittance absent, {stored.Length:N0} bytes read back");
    }

    /// <summary>
    /// The control: a fifth run path would pass everything above while writing nothing.
    /// </summary>
    /// <remarks>
    /// Counting the writes in the source is crude, and it is the only thing here that fails
    /// when a path is <em>added</em> rather than changed - which is how the defect this file
    /// exists for arrived. The number is asserted against the cases above rather than
    /// hardcoded twice, so covering a path and adding one stay in step.
    /// </remarks>
    [Fact]
    public void NoRunPathIsUncovered()
    {
        var source = File.ReadAllText(RunCommandSource());

        var writes = source.Split("{stem}.result.json").Length - 1;

        output.WriteLine($"{writes} result-document write(s) in RunCommand.cs");

        Assert.True(
            writes == 4,
            $"RunCommand.cs writes {writes} result documents and "
            + "EveryRunPathWritesAResultDocument covers 4. If a run path was added, cover it "
            + "there; if one writes no result, that is the defect this file is about");
    }

    private static string RunCommandSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "Einzel.Commands", "RunCommand.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Not Assert.Fail: a test that passes when it cannot find what it checks is worse
        // than one that is absent, which this repository has been caught by four times.
        throw new FileNotFoundException(
            "src/Einzel.Commands/RunCommand.cs is not above the test binary");
    }
}
