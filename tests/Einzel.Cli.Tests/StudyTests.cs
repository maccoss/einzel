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
        File.WriteAllText(model, File.ReadAllText(model).Replace(
            "\"value\": 50, \"unit\": \"mm\"",
            "\"value\": 51, \"unit\": \"mm\"",
            StringComparison.Ordinal));

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
        File.WriteAllText(model, File.ReadAllText(model).Replace(
            "\"value\": 50, \"unit\": \"mm\", \"minimum\": 5",
            "\"value\": 52, \"unit\": \"mm\", \"minimum\": 5",
            StringComparison.Ordinal));

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
        Assert.Equal(0, Run("init", _root).ExitCode);
        File.Delete(Path.Combine(_root, "tests", "reflectron.json"));

        var (exitCode, stdout, _) = Run("test", _root);

        Assert.Equal(0, exitCode);
        Assert.Contains("no tests", stdout, StringComparison.Ordinal);
    }
}
