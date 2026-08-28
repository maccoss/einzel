using System.Text.Json;
using Einzel.Cli;

namespace Einzel.Cli.Tests;

/// <summary>
/// Studies as files: a tolerance sweep and an optimisation, driven the way an
/// agent would drive them.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 13: "A study is a file in studies/ declaring perturbation channels
/// with distributions, a draw count, a seed, an ensemble specification, and
/// figures of merit to record. Output is one row per draw plus a sensitivity
/// ranking - the actual deliverable, since what is wanted is not only whether 100
/// to 300 microns suffices but which parameter binds first."
/// </para>
/// <para>
/// The sweep drivers take a function from a model to a number, which is what keeps
/// them device-agnostic. A file cannot carry a function, so what is really being
/// tested here is the seam: that a name in a document reaches the right evaluator,
/// with the right units on both sides of it.
/// </para>
/// </remarks>
public sealed class StudyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-study", Guid.NewGuid().ToString("N"));

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

    private string Project()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        Directory.CreateDirectory(Path.Combine(_root, "studies"));
        return _root;
    }

    private string WriteStudy(string name, string json)
    {
        var path = Path.Combine(_root, "studies", name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void AScanReportsACurveWithOneRowPerPoint()
    {
        // The third kind of study, and the operation every curve this engine has
        // produced was hand-written C# for. A sweep collapses a range into a
        // distribution and an optimiser reports only where it stopped; neither
        // answers what section 12's Class B asks, which is what the figure looks
        // like across a range.
        Project();

        var study = WriteStudy("scan.json", """
            {
              "name": "depth-scan",
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "scan": {
                "parameter": "turningDepth",
                "from": 20, "to": 200, "unit": "mm",
                "points": 10
              }
            }
            """);

        var (exitCode, stdout, _) = Run("scan", study, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.Equal("turningDepth", root.GetProperty("parameter").GetString());
        Assert.Equal("mm", root.GetProperty("unit").GetString());
        Assert.Equal("linear", root.GetProperty("spacing").GetString());
        Assert.Equal(10, root.GetProperty("succeeded").GetInt32());

        var points = root.GetProperty("points").EnumerateArray().ToList();

        Assert.Equal(10, points.Count);

        // In the scan's own unit and the figure's own unit, not raw SI, and in scan
        // order (CLI-6).
        Assert.Equal(20.0, points[0].GetProperty("value").GetDouble(), 1e-9);
        Assert.Equal(200.0, points[^1].GetProperty("value").GetDouble(), 1e-9);

        // A single-stage reflectron's flight time is 2L/v + 2v/a with a inversely
        // proportional to the penetration depth, so the curve is a straight line in
        // depth. That is a closed form, not a stored expectation - and a driver that
        // evaluated the unperturbed model at every point would give a flat one.
        var first = points[0].GetProperty("figureOfMerit").GetDouble();
        var last = points[^1].GetProperty("figureOfMerit").GetDouble();
        var middle = points[4].GetProperty("figureOfMerit").GetDouble();

        var slope = (last - first) / 180.0;

        Assert.Equal(first + (slope * (points[4].GetProperty("value").GetDouble() - 20.0)), middle, 1e-6);
        Assert.True(last > first);

        // GRD-7: a study references a manifest. Sweeps wrote results and no manifest
        // at all until recently, and a scan is no less worth regenerating.
        var artifacts = root.GetProperty("artifacts").EnumerateArray()
            .Select(a => a.GetString()!).ToList();

        Assert.Contains(artifacts, a => a.EndsWith(".scan.json", StringComparison.Ordinal));
        Assert.Contains(artifacts, a => a.EndsWith(".scan.manifest.json", StringComparison.Ordinal));

        foreach (var artifact in artifacts)
        {
            Assert.True(File.Exists(Path.Combine(_root, artifact)), artifact);
        }
    }

    [Fact]
    public void AScanPastADeclaredBoundKeepsGoingAndSaysWhy()
    {
        // Walking past what the template says is buildable is a legitimate thing to
        // ask a scan for - it is how you find where a design stops working. What is
        // not acceptable is a table half full of blanks with no explanation, which
        // reads as the solver failing rather than the model refusing.
        Project();

        var study = WriteStudy("past.json", """
            {
              "name": "past-the-bound",
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "scan": {
                "parameter": "turningDepth",
                "from": 150, "to": 250, "unit": "mm",
                "points": 5
              }
            }
            """);

        var (exitCode, stdout, stderr) = Run("scan", study, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        var points = root.GetProperty("points").EnumerateArray().ToList();

        Assert.Equal(5, points.Count);
        Assert.True(root.GetProperty("succeeded").GetInt32() < 5);

        // Per row, with the validator's own message rather than a blank.
        var refused = points.Last();

        Assert.False(refused.TryGetProperty("figureOfMerit", out var value)
            && value.ValueKind == JsonValueKind.Number);

        Assert.Contains(
            "VALUE_OUT_OF_BOUNDS", refused.GetProperty("failure").GetString()!, StringComparison.Ordinal);

        // And once, up front.
        Assert.Contains(
            root.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString() == "scan.outside-declared-bounds");

        Assert.DoesNotContain("Unhandled", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AScanRefusesADryRunTheSameWayTheOthersDo()
    {
        // CLI-4: --dry-run on every mutating command, saying what it would do and
        // writing nothing.
        Project();

        var study = WriteStudy("dry.json", """
            {
              "name": "dry",
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "scan": {
                "parameter": "turningDepth",
                "from": 40, "to": 60, "unit": "mm",
                "points": 3
              }
            }
            """);

        var (exitCode, stdout, _) = Run("scan", study, "--dry-run", "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        Assert.Empty(document.RootElement.GetProperty("artifacts").EnumerateArray());
        Assert.Empty(document.RootElement.GetProperty("points").EnumerateArray());

        Assert.False(
            File.Exists(Path.Combine(_root, "results", "dry.scan.json")),
            "a dry run wrote a result");
    }

    [Fact]
    public void ASweepSaysWhichParameterBindsFirst()
    {
        Project();

        var study = WriteStudy("tol.json", """
            {
              "name": "reflectron-tolerance",
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "draws": 40,
              "seed": 7,
              "channels": [
                { "parameter": "turningDepth", "halfWidth": 0.2, "unit": "mm" },
                { "parameter": "capPotential", "halfWidth": 5.0, "unit": "V" }
              ]
            }
            """);

        var (exitCode, stdout, _) = Run("sweep", study, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.Equal(40, root.GetProperty("draws").GetInt32());
        Assert.Equal(40, root.GetProperty("succeeded").GetInt32());

        // Reported in the figure's own unit, not raw SI. A nominal of 1.018e-5
        // printed beside the label "us" is a bare number wearing a unit.
        Assert.Equal("us", root.GetProperty("figureOfMerit").GetProperty("unit").GetString());
        Assert.Equal(10.1805, root.GetProperty("nominal").GetDouble(), 1e-3);

        // The deliverable: ordered by swing, largest first.
        var sensitivity = root.GetProperty("sensitivity");
        Assert.Equal(2, sensitivity.GetArrayLength());

        var swings = sensitivity.EnumerateArray().Select(c => c.GetProperty("swing").GetDouble()).ToArray();
        Assert.True(swings[0] >= swings[1], "the ranking is not ordered by swing");

        // A fifth of a millimetre of mirror depth moves the flight time further
        // than five volts on the cap does, which is the sort of thing the study
        // exists to establish rather than to assume.
        Assert.Equal(
            "turningDepth",
            sensitivity[0].GetProperty("parameter").GetString());

        // Swings share the figure's unit, so they are comparable with the nominal.
        Assert.True(swings[0] > 1e-3, $"a swing of {swings[0]:E3} us looks like raw SI rather than microseconds");
    }

    [Fact]
    public void ASweepIsReproducibleFromItsSeed()
    {
        // PRJ-3: a manifest fully determines its run. A tolerance study whose
        // result changes between runs cannot be compared against itself.
        Project();

        var study = WriteStudy("tol.json", """
            {
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "draws": 20,
              "seed": 3,
              "channels": [{ "parameter": "turningDepth", "halfWidth": 0.2, "unit": "mm" }]
            }
            """);

        var (_, first, _) = Run("sweep", study, "--json");
        var (_, again, _) = Run("sweep", study, "--json");

        Assert.Equal(first, again);
    }

    [Fact]
    public void AnOptimisationSearchesTheDeclaredParameters()
    {
        Project();

        var study = WriteStudy("focus.json", """
            {
              "name": "energy-focus",
              "model": "../models/reflectron.json",
              "figureOfMerit": "resolvingPower",
              "variables": [
                { "parameter": "capPotential", "minimum": 3.6, "maximum": 4.4, "unit": "kV" }
              ],
              "algorithm": "nelderMead",
              "maximumEvaluations": 25,
              "objectiveTolerance": 1e-4
            }
            """);

        var (_, stdout, _) = Run("optimise", study, "--json");

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        // The sense came from the figure of merit rather than the file: a
        // resolving power is better larger, and making the author restate that is
        // an invitation to state it wrongly.
        Assert.Equal("Maximise", root.GetProperty("sense").GetString());
        Assert.Equal(0, root.GetProperty("failures").GetInt32());

        var best = root.GetProperty("best").GetProperty("capPotential");

        // In volts, and inside the box it was given.
        Assert.Equal("V", best.GetProperty("unit").GetString());
        Assert.InRange(best.GetProperty("value").GetDouble(), 3600.0, 4400.0);

        // GRD-1 all the way to the file: the optimum carries its interval and the
        // evidence behind it.
        Assert.True(best.GetProperty("uncertainty").GetProperty("upper").GetDouble()
            >= best.GetProperty("uncertainty").GetProperty("lower").GetDouble());

        Assert.Equal("search", best.GetProperty("evidence").GetProperty("kind").GetString());

        // And it improved on where it started.
        var history = root.GetProperty("history");
        Assert.True(history.GetArrayLength() >= 1, "the search recorded no improvement at all");
    }

    [Fact]
    public void AnExhaustedBudgetIsAConvergenceFailureExitCode()
    {
        Project();

        var study = WriteStudy("tight.json", """
            {
              "model": "../models/reflectron.json",
              "figureOfMerit": "resolvingPower",
              "variables": [{ "parameter": "capPotential", "minimum": 3.6, "maximum": 4.4, "unit": "kV" }],
              "maximumEvaluations": 6,
              "parameterTolerance": 1e-12,
              "objectiveTolerance": 1e-12
            }
            """);

        var (exitCode, stdout, _) = Run("optimise", study, "--json");

        // A best-so-far is not an optimum, and a caller branching on the exit code
        // should not have to parse output to find that out.
        Assert.Equal(4, exitCode);

        using var document = JsonDocument.Parse(stdout);
        Assert.False(document.RootElement.GetProperty("converged").GetBoolean());
    }

    [Fact]
    public void AStudyNamingAnUnknownFigureSaysWhatThereIs()
    {
        Project();

        var study = WriteStudy("bad.json", """
            {
              "model": "../models/reflectron.json",
              "figureOfMerit": "spotSize",
              "channels": [{ "parameter": "turningDepth", "halfWidth": 0.2, "unit": "mm" }]
            }
            """);

        var (exitCode, _, stderr) = Run("sweep", study);

        Assert.Equal(1, exitCode);
        Assert.Contains("spotSize", stderr, StringComparison.Ordinal);
        Assert.Contains("resolvingPower", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AHalfWidthWithoutAUnitIsRefused()
    {
        // SI internally, units explicit at every boundary. A bare 0.1 could be a
        // tenth of a millimetre or a tenth of a metre, and an agent writing from
        // prose is the likeliest to leave it out.
        Project();

        var study = WriteStudy("bare.json", """
            {
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "channels": [{ "parameter": "turningDepth", "halfWidth": 0.2 }]
            }
            """);

        var (exitCode, _, stderr) = Run("sweep", study);

        Assert.Equal(1, exitCode);
        Assert.Contains("unit", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ADryRunComputesNothing()
    {
        Project();

        var study = WriteStudy("tol.json", """
            {
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "draws": 5000,
              "channels": [{ "parameter": "turningDepth", "halfWidth": 0.2, "unit": "mm" }]
            }
            """);

        // Five thousand draws would take a while. --dry-run returns at once and
        // writes nothing, which is what makes it useful for checking a study is
        // well formed before committing to it.
        var (exitCode, stdout, _) = Run("sweep", study, "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains("would sweep", stdout, StringComparison.Ordinal);

        // The results directory is part of the project layout and exists from
        // 'init'; what must not exist is a result in it.
        var results = Path.Combine(_root, "results");

        Assert.Empty(Directory.Exists(results)
            ? Directory.GetFiles(results, "*.sweep.json")
            : []);
    }

    [Fact]
    public void TheStudySchemaListsTheFiguresOfMeritThatExist()
    {
        // A study naming a figure this build does not compute is the commonest way
        // to write an invalid study, and it is not something the shape of the
        // document can express. The schema enumerates them.
        var (exitCode, stdout, _) = Run("schema", "--study");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var figure = document.RootElement.GetProperty("properties").GetProperty("figureOfMerit");

        var names = figure.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Contains("flightTime", names);
        Assert.Contains("resolvingPower", names);

        // And the study schema carries descriptions from its own assembly, not
        // only from the one the model types live in.
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("properties").GetProperty("model")
                .GetProperty("description").GetString()));
    }

    [Fact]
    public void AStudyPathIsRelativeToTheStudyNotTheWorkingDirectory()
    {
        // A study travels with its project and should mean the same thing from
        // anywhere inside it.
        Project();

        var study = WriteStudy("tol.json", """
            {
              "model": "../models/reflectron.json",
              "figureOfMerit": "flightTime",
              "draws": 4,
              "channels": [{ "parameter": "turningDepth", "halfWidth": 0.2, "unit": "mm" }]
            }
            """);

        var previous = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Assert.Equal(0, Run("sweep", study, "--json").ExitCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}

/// <summary>
/// Drift detection: whether a stored result is still the answer.
/// </summary>
/// <remarks>
/// GRD-10 asks for it in both directions and PRJ-3 is what makes it possible - a
/// manifest fully determines its run, so a result carries enough to say whether
/// the world has moved out from under it. Nothing is recomputed, because a check
/// costing as much as the run it checks would not get run.
/// </remarks>
public sealed class VerifyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-verify", Guid.NewGuid().ToString("N"));

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

    private string RunOnce()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "reflectron.json");
        Assert.Equal(0, Run("run", model).ExitCode);
        return model;
    }

    [Fact]
    public void AFreshResultStands()
    {
        RunOnce();

        var (exitCode, stdout, _) = Run("verify", _root, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.True(root.GetProperty("allCurrent").GetBoolean());

        var result = root.GetProperty("results")[0];
        Assert.True(result.GetProperty("modelMatches").GetBoolean());
        Assert.True(result.GetProperty("solverMatches").GetBoolean());
        Assert.Empty(result.GetProperty("drift").EnumerateArray());
    }

    [Fact]
    public void AModelEditedUnderneathAResultIsCaught()
    {
        // The commonest way a result stops being the answer, and the one that is
        // invisible from the file: the number is still there, still well formed,
        // and about a geometry that no longer exists.
        var model = RunOnce();
        Edit(model, "\"value\": 50,", "\"value\": 51,");

        var (exitCode, _, stderr) = Run("verify", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("STALE", stderr, StringComparison.Ordinal);
        Assert.Contains("edited", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeletedModelIsCaught()
    {
        var model = RunOnce();
        File.Delete(model);

        var (exitCode, _, stderr) = Run("verify", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("gone", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangedSolverBehaviourInvalidatesButAChangedBuildDoesNot()
    {
        // FLD-3 keeps these two apart on purpose: "after an engine update a cache
        // computed by the previous solver is silently wrong and nothing else would
        // catch it" - while a release that altered nothing physical must not
        // invalidate every result in every project.
        RunOnce();

        var path = Path.Combine(_root, "results", "reflectron.manifest.json");

        // Through the manifest type rather than by editing its text. The engine
        // version contains a '+', which JSON escapes as +, so a literal
        // string replace silently matches nothing - and a test that quietly
        // changes nothing passes for the wrong reason.
        var original = Einzel.Project.RunManifest.FromJson(File.ReadAllText(path))!;

        // A different build, same numerical behaviour: a note, not drift.
        File.WriteAllText(path, (original with { EngineVersion = "0.0.9-other" }).ToJson());

        var (buildCode, buildOut, _) = Run("verify", _root);
        Assert.Equal(0, buildCode);
        Assert.Contains("note:", buildOut, StringComparison.Ordinal);

        // Different numerical behaviour: drift.
        File.WriteAllText(
            path,
            (original with { SolverBehaviourVersion = original.SolverBehaviourVersion + 1 }).ToJson());

        var (solverCode, _, solverErr) = Run("verify", _root);
        Assert.Equal(1, solverCode);
        Assert.Contains("solver has changed behaviour", solverErr, StringComparison.Ordinal);
    }

    [Fact]
    public void AProjectWithNoResultsIsNotAFailure()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var (exitCode, stdout, _) = Run("verify", _root);

        Assert.Equal(0, exitCode);
        Assert.Contains("no stored results", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrites part of a model file, and fails if the text was not there.
    /// </summary>
    /// <remarks>
    /// A test that edits a file by string replacement and does not check that the
    /// replacement happened is a test that can silently stop testing anything.
    /// Three uses of this pattern failed at once when the shipped example was
    /// reformatted: the edit matched nothing, the model was unchanged, and each
    /// test reported the feature it was checking as broken.
    /// </remarks>
    private static void Edit(string path, string from, string to)
    {
        var before = File.ReadAllText(path);
        var after = before.Replace(from, to, StringComparison.Ordinal);

        Assert.NotEqual(before, after);

        File.WriteAllText(path, after);
    }

}

/// <summary>
/// A project's tests: expected results with assertion tolerances.
/// </summary>
/// <remarks>
/// EX-1 asks the corpus for "a prose description, expected results, and assertion
/// tolerances". What this really provides is the thing that separates editing a
/// model from guessing at one: an agent can establish that a change did not break
/// something.
/// </remarks>
public sealed class ProjectTestTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-projtest", Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void AFreshProjectShipsATestThatPasses()
    {
        // init to test with nothing in between. The expected value is a closed
        // form rather than something this engine produced once, so passing it
        // means the physics is right and not merely unchanged.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var (exitCode, stdout, _) = Run("test", _root, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.True(root.GetProperty("allPassed").GetBoolean());

        var assertion = root.GetProperty("tests")[0].GetProperty("assertions")[0];

        Assert.Equal("flightTime", assertion.GetProperty("figureOfMerit").GetString());
        Assert.Equal("us", assertion.GetProperty("unit").GetString());
        Assert.Equal(10.180505718, assertion.GetProperty("observed").GetDouble(), 1e-5);

        // ACC-1 is one part per million and this clears it by orders.
        Assert.True(assertion.GetProperty("relativeError").GetDouble() < 1e-8);
    }

    [Fact]
    public void BreakingTheGeometryBreaksTheTest()
    {
        // The assertion that matters about a test suite: that it fails.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "reflectron.json");
        Edit(model, "\"value\": 50,", "\"value\": 52,");

        var (exitCode, _, stderr) = Run("test", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("FAIL", stderr, StringComparison.Ordinal);
        Assert.Contains("flightTime", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestThatAssertsNothingIsATestThatFails()
    {
        // A green tick standing for no evidence is worse than a red one. An empty
        // expectation list would otherwise pass trivially and forever.
        Assert.Equal(0, Run("init", _root).ExitCode);

        File.WriteAllText(
            Path.Combine(_root, "tests", "empty.json"),
            """{ "model": "../models/reflectron.json", "expect": [] }""");

        var (exitCode, _, stderr) = Run("test", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("asserts nothing", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExpectationInTheWrongDimensionIsARefusalNotAFailure()
    {
        // A flight time expected in millimetres is not a wrong answer, it is a
        // wrong question, and reporting it as a failed assertion would send
        // someone looking at the physics.
        Assert.Equal(0, Run("init", _root).ExitCode);

        File.WriteAllText(
            Path.Combine(_root, "tests", "wrong-unit.json"),
            """
            {
              "model": "../models/reflectron.json",
              "expect": [{ "figureOfMerit": "flightTime", "value": 10.18, "unit": "mm", "tolerance": 1e-6 }]
            }
            """);

        var (exitCode, _, stderr) = Run("test", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("dimension", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AProjectWithNoTestsSaysSoRatherThanReportingSuccess()
    {
        // This used to be exit 0 with a note on stdout, and that was the same
        // vacuous truth 'einzel solve' had: All() over an empty list is true, so a
        // project with nothing to check reported that everything checked out. An
        // agent attempting the acceptance suite deleted a test file and got a clean
        // pass, which is the shape of answer that stops an investigation.
        Assert.Equal(0, Run("init", _root).ExitCode);
        File.Delete(Path.Combine(_root, "tests", "reflectron.json"));

        var (exitCode, _, stderr) = Run("test", _root);

        Assert.NotEqual(0, exitCode);

        // CLI-2: the reason goes to stderr, and it says where the tests were looked
        // for and how to get one, because "no tests" alone is a fact rather than a
        // recovery.
        Assert.Contains("no tests", stderr, StringComparison.Ordinal);
        Assert.Contains("einzel init", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrites part of a model file, and fails if the text was not there.
    /// </summary>
    /// <remarks>
    /// A test that edits a file by string replacement and does not check that the
    /// replacement happened is a test that can silently stop testing anything.
    /// Three uses of this pattern failed at once when the shipped example was
    /// reformatted: the edit matched nothing, the model was unchanged, and each
    /// test reported the feature it was checking as broken.
    /// </remarks>
    private static void Edit(string path, string from, string to)
    {
        var before = File.ReadAllText(path);
        var after = before.Replace(from, to, StringComparison.Ordinal);

        Assert.NotEqual(before, after);

        File.WriteAllText(path, after);
    }

}

/// <summary>
/// The preview tier: fast, deliberately inexact, and marked as such (GRD-5).
/// </summary>
public sealed class PreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-preview", Guid.NewGuid().ToString("N"));

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

    private string Model()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        return Path.Combine(_root, "models", "reflectron.json");
    }

    [Fact]
    public void APreviewIsMarkedAndTheMarkCannotBeSuppressed()
    {
        var (exitCode, stdout, _) = Run("preview", Model(), "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var flightTime = document.RootElement.GetProperty("flightTime");

        var warning = flightTime.GetProperty("warnings")
            .EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "result.preview-tier");

        Assert.False(warning.GetProperty("suppressible").GetBoolean());
        Assert.Equal("Provenance", warning.GetProperty("severity").GetString());

        // The taint rides on the number, so it travels wherever the number does
        // rather than depending on a caller having thought to look beside it.
        Assert.True(flightTime.GetProperty("value").GetDouble() > 0.0);
    }

    [Fact]
    public void APreviewIsCloseButNotQuotable()
    {
        var model = Model();

        var (_, preview, _) = Run("preview", model, "--json");
        var (_, full, _) = Run("run", model, "--json");

        using var previewDocument = JsonDocument.Parse(preview);
        using var fullDocument = JsonDocument.Parse(full);

        var quick = previewDocument.RootElement.GetProperty("flightTime").GetProperty("value").GetDouble();
        var exact = fullDocument.RootElement.GetProperty("flightTime").GetProperty("value").GetDouble();

        // Close enough to see that a change helped, which is the whole use.
        Assert.Equal(exact, quick, 1e-2);

        // And it ran at a looser tolerance than the model asked for, which is what
        // makes it a preview rather than just a run with the manifest left off.
        var used = previewDocument.RootElement.GetProperty("relativeTolerance").GetDouble();
        var requested = previewDocument.RootElement.GetProperty("requestedTolerance").GetDouble();

        Assert.True(used > requested, $"preview ran at {used:G3}, no looser than the model's {requested:G3}");
    }

    [Fact]
    public void APreviewWritesNothing()
    {
        // A tainted result sitting in results/ would be picked up by verify and
        // reported as current, which is the sort of quietly-wrong artifact the
        // manifest discipline exists to prevent.
        var model = Model();
        Assert.Equal(0, Run("preview", model).ExitCode);

        var results = Path.Combine(_root, "results");

        Assert.Empty(Directory.Exists(results) ? Directory.GetFiles(results) : []);
    }
}

/// <summary>
/// A model that launches a cloud, driven through the command surface.
/// </summary>
public sealed class CloudRunTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-cloud", Guid.NewGuid().ToString("N"));

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

    private const string TwoHundredIons = "{ \"ions\": 200, \"seed\": 4 }";

    private string WithCloud(string cloud)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "reflectron.json");
        var text = File.ReadAllText(path);

        File.WriteAllText(path, text.Replace(
            "\"energyFraction\": 0",
            "\"energyFraction\": 0,\n            \"cloud\": " + cloud,
            StringComparison.Ordinal));

        return path;
    }

    [Fact]
    public void AModelWithoutACloudReportsNoEnsemble()
    {
        // The default has to stay exactly what it was. A spread appearing on its
        // own would change every existing result silently, and a resolving power
        // quietly getting worse is indistinguishable from a bug.
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "reflectron.json");

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        Assert.False(
            document.RootElement.TryGetProperty("ensemble", out _),
            "a single-ion model reported an ensemble; a transmission of one out of one is not a statistic");
    }

    [Fact]
    public void ACloudReportsTransmissionAndBothPeakWidths()
    {
        var model = WithCloud("""
            {
              "ions": 400, "seed": 1,
              "temperature": { "value": 300, "unit": "K" },
              "transverseSpread": { "value": 0.3, "unit": "mm" },
              "energyFractionSpread": 0.01
            }
            """);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var ensemble = document.RootElement.GetProperty("ensemble");

        Assert.Equal(400, ensemble.GetProperty("launched").GetInt32());
        Assert.True(ensemble.GetProperty("arrived").GetInt32() > 0);

        // Both widths, because they disagree whenever the peak has a tail and a
        // reader given one of them beside a resolving power will reconcile the
        // wrong pair. The gap between them is the skew.
        var central = ensemble.GetProperty("centralWidthNs").GetDouble();
        var gaussian = ensemble.GetProperty("gaussianFwhmNs").GetDouble();
        var skew = ensemble.GetProperty("skewness").GetDouble();

        Assert.True(central > 0.0);
        Assert.True(gaussian > 0.0);

        // A single-stage mirror past its focus has a one-sided second-order tail,
        // so the Gaussian-equivalent width exceeds the central half and the skew
        // says which side the tail is on.
        Assert.True(gaussian > central, $"Gaussian {gaussian:F3} ns is not wider than central {central:F3} ns");
        Assert.True(skew > 0.5, $"a one-sided tail should show as positive skew; got {skew:F2}");

        // GRD-1 through to the file.
        Assert.Equal(
            "ensemble",
            ensemble.GetProperty("transmission").GetProperty("evidence").GetProperty("kind").GetString());
    }

    [Fact]
    public void AFirstOrderFocusSuppressesThermalSpreadQuadratically()
    {
        // Written expecting a square root and corrected by the measurement, which
        // is the useful kind of surprise.
        //
        // In a uniform extraction field the turn-around width goes as the thermal
        // velocity, so as the square root of temperature. This reflectron sits at
        // its FIRST-ORDER ENERGY FOCUS: the mirror is arranged so an ion launched
        // slightly fast goes deeper and takes longer there, cancelling the time it
        // saved in the drift. The first-order term is gone by construction, so
        // what a velocity spread leaves is the second-order term - the square of
        // the offset - and the width goes as the temperature itself.
        //
        // Sixteen times the temperature gives sixteen times the width, not four.
        // That is the focus doing its job, measured rather than assumed.
        double TurnAround(double kelvin)
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            var model = WithCloud($$"""
                {
                  "ions": 300, "seed": 4,
                  "temperature": { "value": {{kelvin}}, "unit": "K" }
                }
                """);

            var (_, stdout, _) = Run("run", model, "--json");
            using var document = JsonDocument.Parse(stdout);

            return document.RootElement.GetProperty("ensemble").GetProperty("turnAroundFwhmNs").GetDouble();
        }

        var cold = TurnAround(50.0);
        var hot = TurnAround(800.0);
        var ratio = hot / cold;

        // Linear in temperature, to a few per cent on three hundred sampled ions.
        // A square-root scaling here would mean the focus was not working, and a
        // resolving power computed through it would be optimistic.
        Assert.InRange(ratio, 13.0, 19.0);
    }

    [Fact]
    public void TheSameSeedGivesTheSameEnsemble()
    {
        var model = WithCloud("""
            { "ions": 200, "seed": 9, "temperature": { "value": 300, "unit": "K" } }
            """);

        var (_, first, _) = Run("run", model, "--json");
        var (_, again, _) = Run("run", model, "--json");

        using var a = JsonDocument.Parse(first);
        using var b = JsonDocument.Parse(again);

        Assert.Equal(
            a.RootElement.GetProperty("ensemble").GetProperty("gaussianFwhmNs").GetDouble(),
            b.RootElement.GetProperty("ensemble").GetProperty("gaussianFwhmNs").GetDouble());
    }

    [Fact]
    public void ADensePacketSaysThatSpaceChargeIsNotModelled()
    {
        // The hole this closes. Ten thousand ions is a number someone would type
        // without thinking - it is roughly what ACC-5 asks for to pin down a
        // transmission - and at this cloud size their mutual repulsion is already
        // past the flight-time budget. Nothing said so before.
        var model = WithCloud("""
            {
              "ions": 10000, "seed": 1,
              "transverseSpread": { "value": 0.3, "unit": "mm" }
            }
            """);

        var (_, stdout, stderr) = Run("run", model, "--json");

        using var document = JsonDocument.Parse(stdout);
        var ensemble = document.RootElement.GetProperty("ensemble");

        Assert.Equal(10000, ensemble.GetProperty("population").GetInt32());
        Assert.True(ensemble.GetProperty("spaceChargeTimingFraction").GetDouble() > 1e-6);

        // The limit is reported alongside, because "over budget" invites "by how
        // much can I load it".
        //
        // It used to read a few thousand here and it now reads a handful. The old
        // estimate converted the self-potential into a fractional energy spread,
        // which is the wrong mechanism by 527 times; what dominates in flight is
        // the packet expanding under its own charge. The direct pairwise sum in
        // Einzel.Transport.Interaction is what established that, and the range here
        // is loose on purpose - the point of the assertion is the order of
        // magnitude, not a digit.
        var limit = ensemble.GetProperty("spaceChargePopulationLimit").GetDouble();
        Assert.InRange(limit, 1.0, 100.0);

        var warning = ensemble.GetProperty("resolvingPower").GetProperty("warnings")
            .EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "spacecharge.ignored");

        Assert.False(warning.GetProperty("suppressible").GetBoolean());
        Assert.Equal("ValidityViolation", warning.GetProperty("severity").GetString());

        // Non-suppressible goes to stderr on the human path too.
        var (_, _, plain) = Run("run", model);
        Assert.Contains("space charge", plain, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASparsePacketIsQuietButStillSaysWhereTheEdgeIs()
    {
        // A number that only appears when it is bad teaches nobody where the edge
        // is, so the figure is reported either way and only the warning is
        // conditional.
        // Genuinely sparse, which is a stricter requirement than it used to be:
        // the corrected estimate makes 200 ions in a third of a millimetre a dense
        // packet, not a quiet one. Twenty ions spread over three millimetres is
        // sparse by the mechanism that actually governs - the surface acceleration
        // goes as the population over the radius squared, so both moves count.
        var model = WithCloud("""
            {
              "ions": 20, "seed": 1,
              "transverseSpread": { "value": 3.0, "unit": "mm" }
            }
            """);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var ensemble = document.RootElement.GetProperty("ensemble");

        Assert.True(
            ensemble.GetProperty("spaceChargeTimingFraction").GetDouble() < 1e-7,
            "this packet was meant to be sparse enough to say nothing");
        Assert.True(ensemble.GetProperty("spaceChargePopulationLimit").GetDouble() > 0.0);

        Assert.DoesNotContain(
            ensemble.GetProperty("resolvingPower").GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString()!.StartsWith("spacecharge", StringComparison.Ordinal));
    }

    [Fact]
    public void SamplingHarderDoesNotTriggerTheWarning()
    {
        // The distinction the fix exists for, end to end: ten thousand samples of
        // a single-ion experiment is a better statistic, not a denser bunch.
        var model = WithCloud("""
            {
              "ions": 10000, "population": 1, "seed": 1,
              "transverseSpread": { "value": 0.3, "unit": "mm" }
            }
            """);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var ensemble = document.RootElement.GetProperty("ensemble");

        Assert.Equal(10000, ensemble.GetProperty("launched").GetInt32());
        Assert.Equal(1, ensemble.GetProperty("population").GetInt32());
        Assert.Equal(0.0, ensemble.GetProperty("spaceChargeTimingFraction").GetDouble());

        Assert.DoesNotContain(
            ensemble.GetProperty("resolvingPower").GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString()!.StartsWith("spacecharge", StringComparison.Ordinal));
    }

    [Fact]
    public void AThermalPacketReportsAnEmittanceThatMatchesItsClosedForm()
    {
        // The emittance of a cloud with independently drawn position and velocity
        // is the product of their widths: the transverse spread as declared, and
        // the divergence, which is the thermal speed over the axial one. Both are
        // known before the run, so the reported figure is checkable rather than
        // merely plausible.
        const double SpreadMm = 0.3;
        const double TemperatureK = 300.0;

        var model = WithCloud("""
            {
              "ions": 3000, "seed": 5,
              "temperature": { "value": 300, "unit": "K" },
              "transverseSpread": { "value": 0.3, "unit": "mm" }
            }
            """);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var ensemble = document.RootElement.GetProperty("ensemble");

        var emittance = ensemble.GetProperty("emittanceMmMrad").GetDouble();

        // The reflectron returns the ion to its launch energy, so the axial speed
        // at the detector is the one the acceleration set. m/z 500 at 4 kV.
        const double BoltzmannSi = 1.380649e-23;
        const double MassSi = 500.0 * 1.66053906892e-27;
        const double ChargeSi = 1.602176634e-19;

        var axial = Math.Sqrt(2.0 * ChargeSi * 4000.0 / MassSi);
        var thermal = Math.Sqrt(BoltzmannSi * TemperatureK / MassSi);
        var exact = SpreadMm * (thermal / axial) * 1e3;

        Assert.InRange(emittance, exact * 0.94, exact * 1.06);

        // Round source, so the two planes should agree to sampling error rather
        // than differ systematically.
        var minor = ensemble.GetProperty("emittanceMinorMmMrad").GetDouble();
        Assert.InRange(minor / emittance, 0.9, 1.0);

        // Normalised is the same area against transverse momentum, so it is the
        // geometric one scaled by the axial speed over c.
        var normalised = ensemble.GetProperty("normalisedEmittanceMmMrad").GetDouble();
        Assert.InRange(normalised / emittance, axial / 299792458.0 * 0.99, axial / 299792458.0 * 1.01);

        // Diverging by the time it lands, having started at a waist.
        Assert.True(ensemble.GetProperty("packetTwissAlpha").GetDouble() < 0.0);
    }

    [Fact]
    public void APerfectlyParallelPacketReportsZeroAreaAndNoOrientation()
    {
        // Spatial spread with no temperature makes every ion exactly parallel,
        // which is a real packet with a real emittance of exactly zero. It is also
        // the case where the Twiss orientation is undefined - there is no ellipse
        // to be tilted - and an undefined orientation reaching the serialiser as a
        // not-a-number takes the whole document down, silently and only on --json.
        // The same failure has happened here before, on a different field.
        var model = WithCloud("""
            {
              "ions": 400, "seed": 2,
              "transverseSpread": { "value": 0.3, "unit": "mm" }
            }
            """);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        // Parsing at all is most of the assertion.
        using var document = JsonDocument.Parse(stdout);
        var ensemble = document.RootElement.GetProperty("ensemble");

        Assert.Equal(0.0, ensemble.GetProperty("emittanceMmMrad").GetDouble(), 12);

        // Absent rather than null: this surface omits what it did not measure, so
        // a consumer distinguishes "no orientation" from "zero" by the key not
        // being there. Zero is a real orientation and this packet does not have one.
        Assert.False(ensemble.TryGetProperty("packetTwissAlpha", out _));

        // The packet still has a size; it is only the area that vanished.
        Assert.True(ensemble.GetProperty("packetRadiusMm").GetDouble() > 0.0);
    }

    [Fact]
    public void LossesAreItemisedByNamedSurface()
    {
        // ACC-5: "Transmission itemized by loss surface and mechanism, with
        // intervals. Never 92 percent." The rectilinear trap is the device that
        // makes this checkable, because it has a slot the beam has to get through
        // and plates on either side of it that the rest lands on.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "trap.json");
        Assert.Equal(0, Run("new", model, "--from-template", "rectilinear-trap").ExitCode);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var ensemble = document.RootElement.GetProperty("ensemble");

        var launched = ensemble.GetProperty("launched").GetInt32();
        var arrived = ensemble.GetProperty("arrived").GetInt32();

        var losses = ensemble.GetProperty("losses").EnumerateArray().ToList();

        // The aperture has to lose something, or it is decorative and every
        // transmission figure from this device means nothing.
        Assert.NotEmpty(losses);

        // Every ion accounted for: through, or on a named surface. An itemisation
        // that does not add up is worse than none, because it reads as complete.
        Assert.Equal(launched, arrived + losses.Sum(l => l.GetProperty("ions").GetInt32()));

        // Named, not numbered. "frontPlateLeft" is a thing to move; "surface 3" is
        // not, and neither is 51 per cent.
        Assert.Contains(
            losses,
            l => l.GetProperty("surface").GetString()!.StartsWith("frontPlate", StringComparison.Ordinal));

        // Deterministic ordering (CLI-5): largest first.
        var counts = losses.Select(l => l.GetProperty("ions").GetInt32()).ToList();
        Assert.Equal(counts.OrderByDescending(c => c), counts);
    }

    [Fact]
    public void AModelWithNoLossesReportsAnEmptyItemisation()
    {
        // Empty is a statement, not an omission: this instrument lost nothing, as
        // against this instrument was not asked. The shipped reflectron has no
        // electrodes at all, so there is nothing to strike.
        var model = WithCloud(TwoHundredIons);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        Assert.Empty(document.RootElement.GetProperty("ensemble").GetProperty("losses").EnumerateArray());
    }

    [Fact]
    public void ADrivenModelStillProducesReadableJson()
    {
        // A driven field reports its energy drift as not-a-number, deliberately:
        // a field that does work on purpose has no conservation to diagnose, and
        // printing a number there would look like a diagnostic and mean nothing.
        //
        // JSON has no not-a-number, so before FiniteDoubleConverter this took the
        // whole document down at the serialiser, after the run had already
        // succeeded. That had happened three times before on three other fields.
        // The converter makes it a property of the surface: a non-finite double is
        // written as null, which is the policy the rest of the surface follows.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "filter.json");
        Assert.Equal(0, Run("new", model, "--from-template", "quadrupole-rf").ExitCode);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        // Absent, not zero. Zero drift is a real and excellent answer; this is the
        // absence of an answer, and a reader must be able to tell them apart.
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("maximumRelativeEnergyDrift").ValueKind);

        // The rest of the document survived, which is the point.
        Assert.True(document.RootElement.GetProperty("acceptedSteps").GetInt32() > 1000);
    }

    [Fact]
    public void APacketAtAPointIsRefusedAsUnbounded()
    {
        // Not a small error, an infinite one, and easy to write by declaring a
        // population without any spread.
        var model = WithCloud("""{ "ions": 500, "seed": 1 }""");

        var (_, stdout, _) = Run("run", model, "--json");

        using var document = JsonDocument.Parse(stdout);

        var warning = document.RootElement.GetProperty("ensemble")
            .GetProperty("resolvingPower").GetProperty("warnings")
            .EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "spacecharge.point-packet");

        Assert.False(warning.GetProperty("suppressible").GetBoolean());
        Assert.Contains("single point", warning.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ACloudIsRefusedWhenItLaunchesNothing()
    {
        var model = WithCloud("""{ "ions": 0 }""");

        var (exitCode, _, stderr) = Run("validate", model);

        Assert.Equal(1, exitCode);
        Assert.Contains("at least one ion", stderr, StringComparison.Ordinal);
    }
}
