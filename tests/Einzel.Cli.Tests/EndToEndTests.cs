using System.Text.Json;
using Einzel.Cli;
using Einzel.Commands;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// The Phase 1 acceptance criterion, rehearsed at the scale Stage 2 reaches: a
/// model is edited as text in a plain folder, run from the command surface, and
/// the answer comes back qualified and inspectable.
/// </summary>
/// <remarks>
/// These drive <see cref="Program.Main"/> itself rather than the command objects,
/// because the things most likely to break the agent loop — exit codes, which
/// stream output lands on, whether <c>--json</c> parses — live in the surface,
/// not in the engine.
/// </remarks>
public sealed class EndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

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
            var exitCode = Program.Main(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private string InitProject()
    {
        var (exitCode, _, _) = Run("init", _root);
        Assert.Equal(0, exitCode);
        return Path.Combine(_root, "models", "reflectron.json");
    }

    [Fact]
    public void InitCreatesAProjectThatIsJustADirectory()
    {
        var (exitCode, stdout, _) = Run("init", _root);
        var project = new ProjectLayout(_root);

        Assert.Equal(0, exitCode);
        Assert.Contains("Created project", stdout, StringComparison.Ordinal);

        foreach (var directory in project.TrackedDirectories)
        {
            Assert.True(Directory.Exists(directory), $"{directory} was not created");
        }

        Assert.True(File.Exists(project.AgentsFile));
        Assert.True(File.Exists(Path.Combine(project.Models, "reflectron.json")));

        // PRJ-4: a plain folder is the default. Nothing here touches git.
        Assert.False(File.Exists(Path.Combine(_root, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".git")));
    }

    [Fact]
    public void GeneratedAgentsFileIsVersionStampedAndDelimited()
    {
        InitProject();
        var contents = File.ReadAllText(new ProjectLayout(_root).AgentsFile);

        // AGD-1: the platform layer is generated and clearly delimited, so the
        // hand-written layer survives regeneration.
        Assert.Contains(AgentsFile.BeginMarker, contents, StringComparison.Ordinal);
        Assert.Contains(AgentsFile.EndMarker, contents, StringComparison.Ordinal);
        Assert.Equal(EngineBuild.Version, AgentsFile.RecordedVersion(contents));
    }

    [Fact]
    public void ValidateAcceptsTheShippedExample()
    {
        var model = InitProject();
        var (exitCode, stdout, stderr) = Run("validate", model);

        Assert.Equal(0, exitCode);
        Assert.Contains("OK", stdout, StringComparison.Ordinal);
        Assert.Empty(stderr);
    }

    [Fact]
    public void RunReproducesTheAnalyticFlightTime()
    {
        var model = InitProject();
        var (exitCode, stdout, _) = Run("run", model, "--json");

        Assert.Equal(0, exitCode);

        var outcome = JsonSerializer.Deserialize<JsonElement>(stdout);
        var flight = outcome.GetProperty("flightTime");

        // The closed form for this geometry: 10.1805057179 us.
        Assert.Equal("us", flight.GetProperty("unit").GetString());
        Assert.Equal(10.1805057179, flight.GetProperty("value").GetDouble(), 1e-5);

        // ACC-4, energy drift budgeted at 1 ppm.
        Assert.True(outcome.GetProperty("maximumRelativeEnergyDrift").GetDouble() < 1e-6);
        Assert.Equal("StopConditionMet", outcome.GetProperty("outcome").GetString());
    }

    [Fact]
    public void EveryReportedNumberArrivesQualified()
    {
        // GRD-1 at the surface: the wire form cannot carry a value without its
        // uncertainty, evidence, and warnings, because it is built by
        // deconstructing the envelope.
        var model = InitProject();
        var (_, stdout, _) = Run("run", model, "--json");

        var flight = JsonSerializer.Deserialize<JsonElement>(stdout).GetProperty("flightTime");

        Assert.True(flight.TryGetProperty("value", out _));
        Assert.True(flight.TryGetProperty("unit", out _));
        Assert.True(flight.TryGetProperty("uncertainty", out var uncertainty));
        Assert.True(flight.TryGetProperty("evidence", out var evidence));
        Assert.True(flight.TryGetProperty("warnings", out _));

        Assert.True(uncertainty.TryGetProperty("confidenceLevel", out _));
        Assert.Equal("convergence", evidence.GetProperty("kind").GetString());
        Assert.Equal("integrator tolerance", evidence.GetProperty("measure").GetString());
    }

    [Fact]
    public void HumanOutputNeverShowsTheValueWithoutItsUncertainty()
    {
        var model = InitProject();
        var (_, stdout, _) = Run("run", model);

        var line = stdout.Split('\n').Single(l => l.Contains("flight time", StringComparison.Ordinal));

        Assert.Contains("10.180", line, StringComparison.Ordinal);
        Assert.Contains("+/-", line);
        Assert.Contains("us", line, StringComparison.Ordinal);
    }

    [Fact]
    public void RunWritesAManifestThatDeterminesTheRun()
    {
        var model = InitProject();
        Run("run", model);

        var manifestPath = Path.Combine(_root, "results", "reflectron.manifest.json");
        Assert.True(File.Exists(manifestPath));

        var manifest = RunManifest.FromJson(File.ReadAllText(manifestPath))!;

        // PRJ-3: model hash, engine version, transport mode, solver behaviour.
        Assert.StartsWith("sha256:", manifest.ModelHash, StringComparison.Ordinal);
        Assert.Equal(EngineBuild.Version, manifest.EngineVersion);
        Assert.Equal(EngineBuild.SolverBehaviourVersion, manifest.SolverBehaviourVersion);
        Assert.Equal("trajectory", manifest.TransportMode);
        Assert.Equal("scalar", manifest.ComputePath);
    }

    [Fact]
    public void EditingTheModelChangesItsHash()
    {
        // GRD-10 rests on this: model drift is detectable because the manifest
        // records what the model was.
        var model = InitProject();
        Run("run", model);

        var manifestPath = Path.Combine(_root, "results", "reflectron.manifest.json");
        var before = RunManifest.FromJson(File.ReadAllText(manifestPath))!.ModelHash;

        File.WriteAllText(model, File.ReadAllText(model).Replace(
            "\"value\": 50, \"unit\": \"mm\"", "\"value\": 55, \"unit\": \"mm\"", StringComparison.Ordinal));

        Run("run", model);
        var after = RunManifest.FromJson(File.ReadAllText(manifestPath))!.ModelHash;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void VtuExportProducesAPolylineWithProvenance()
    {
        var model = InitProject();
        Run("run", model, "--vtu");

        var vtu = Path.Combine(_root, ".einzel", "reflectron.trajectory.vtu");
        Assert.True(File.Exists(vtu));

        var contents = File.ReadAllText(vtu);

        Assert.Contains("<VTKFile type=\"UnstructuredGrid\"", contents, StringComparison.Ordinal);

        // GRD-12: the artifact carries enough of its own history to be assessed.
        Assert.Contains("engine: ", contents, StringComparison.Ordinal);
        Assert.Contains("model: sha256:", contents, StringComparison.Ordinal);

        // Cell type 4 is VTK_POLY_LINE.
        Assert.Contains("Name=\"types\"", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportedTrajectoryHasNoDuplicatedVertices()
    {
        var model = InitProject();
        Run("run", model, "--vtu");

        var contents = File.ReadAllText(Path.Combine(_root, ".einzel", "reflectron.trajectory.vtu"));
        var start = contents.IndexOf("<Points>", StringComparison.Ordinal);
        var end = contents.IndexOf("</Points>", StringComparison.Ordinal);

        var points = contents[start..end]
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && (char.IsDigit(l[0]) || l[0] == '-'))
            .ToArray();

        Assert.True(points.Length > 10, $"expected a sampled trajectory, got {points.Length} points");

        for (var i = 1; i < points.Length; i++)
        {
            Assert.False(
                points[i] == points[i - 1],
                $"duplicate vertex at index {i}: {points[i]} — a zero-length polyline segment");
        }
    }

    [Fact]
    public void AnInvalidModelFailsWithADistinctExitCodeAndErrorsOnStderr()
    {
        // CLI-2 and CLI-3: results on stdout, diagnostics on stderr, exit codes
        // meaningful per failure class.
        var model = InitProject();
        File.WriteAllText(model, File.ReadAllText(model).Replace(
            "\"unit\": \"kV\"", "\"unit\": \"mm\"", StringComparison.Ordinal));

        var (exitCode, stdout, stderr) = Run("validate", model);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("UNITS_INCOMPATIBLE", stderr, StringComparison.Ordinal);

        // AGT-3: the error is a recovery instruction, not a complaint.
        Assert.Contains("at         /", stderr, StringComparison.Ordinal);
        Assert.Contains("try        ", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownTransportModeNamesTheOnesThatExist()
    {
        // Both modes REG-1 declares are now built, so a name that is not one of them
        // is a spelling error rather than a statement about the physics - and the two
        // deserve different exit codes, because one is fixed by editing a word and
        // the other is not fixed at all.
        var model = InitProject();
        File.WriteAllText(model, File.ReadAllText(model).Replace(
            "\"mode\": \"trajectory\"", "\"mode\": \"statisticalDiffusion\"", StringComparison.Ordinal));

        var (exitCode, _, stderr) = Run("validate", model);

        Assert.Equal(1, exitCode);
        Assert.Contains("SCHEMA_INVALID", stderr, StringComparison.Ordinal);
        Assert.Contains("diffusion", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEnergyAcceptanceSweepIsFlatToFirstOrder()
    {
        // The whole point of the shipped geometry, exercised the way an agent
        // would: edit one number in the document, re-run, compare.
        var model = InitProject();
        var times = new List<double>();

        foreach (var fraction in new[] { -0.05, 0.0, 0.05 })
        {
            File.WriteAllText(model, File.ReadAllText(model).Replace(
                "\"energyFraction\": 0",
                $"\"energyFraction\": {fraction.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                StringComparison.Ordinal));

            var (exitCode, stdout, _) = Run("run", model, "--json");
            Assert.Equal(0, exitCode);

            times.Add(JsonSerializer.Deserialize<JsonElement>(stdout)
                .GetProperty("flightTime").GetProperty("value").GetDouble());

            File.WriteAllText(model, File.ReadAllText(model).Replace(
                $"\"energyFraction\": {fraction.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                "\"energyFraction\": 0",
                StringComparison.Ordinal));
        }

        // The first-order term cancels at the focus, so what is left is exactly
        // the second-order aberration, and it has a closed form. Writing
        // T = (T0/2)[(1+e) + 1/(1+e)] with e the fractional velocity offset gives
        //
        //     T(e)/T0 - 1 = e^2 / (2(1+e))
        //
        // Note the asymmetry: a symmetric plus or minus five percent in *energy*
        // is not symmetric in velocity, because v goes as the square root of E.
        // So the two arrivals do not coincide, and the residual difference is not
        // a surviving first-order term — it is the second-order term evaluated at
        // two different magnitudes of e. Asserting the arrivals are equal would be
        // asserting the wrong physics.
        static double PredictedShift(double energyFraction)
        {
            var e = Math.Sqrt(1.0 + energyFraction) - 1.0;
            return e * e / (2.0 * (1.0 + e));
        }

        var nominal = times[1];

        foreach (var (fraction, measured) in new[] { (-0.05, times[0]), (0.05, times[2]) })
        {
            var predicted = nominal * (1.0 + PredictedShift(fraction));
            var error = Math.Abs(measured - predicted) / predicted;

            Assert.True(
                error < 1e-6,
                $"at {fraction:P0} energy the arrival was {measured:G12} us against a predicted "
                + $"{predicted:G12} us, a relative error of {error:E3}");
        }

        // And the second-order term really is second order: about 3e-4 for a five
        // percent energy spread, not the 2.5e-2 a first-order geometry would give.
        var shift = ((times[0] + times[2]) / 2.0 - nominal) / nominal;
        Assert.True(shift is > 1e-4 and < 1e-3, $"second-order shift {shift:E3} is outside the expected 3e-4");
    }
}
