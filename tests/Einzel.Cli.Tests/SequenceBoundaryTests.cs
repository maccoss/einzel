using System.Text.Json.Nodes;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>Where one phase of a sequence ends and the next begins, nothing is lost or refused wrongly.</summary>
public sealed class SequenceBoundaryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-sequence-boundary", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Model(string sequence)
    {
        var document = JsonNode.Parse(RunMemoryCases.Document("tube-plain", 64))!;

        document["parameters"] = JsonNode.Parse("""{ "pushField": { "value": 0.0, "unit": "V/m" } }""");
        document["fields"]![0] = JsonNode.Parse("""{ "type": "uniform", "field": { "expression": [ "pushField", "0", "0" ] } }""");
        document["sequence"] = JsonNode.Parse(sequence);

        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8] + ".json");

        File.WriteAllText(path, document.ToJsonString());

        return path;
    }

    /// <summary>
    /// A trajectory leg whose field changes at its end, with its ions still in flight, runs to
    /// the boundary and hands its packet on, where it used to stop with a step-size underflow
    /// an attosecond short of the switch.
    /// </summary>
    /// <param name="settle">The first phase, in microseconds.</param>
    /// <param name="extract">The second, in microseconds.</param>
    /// <param name="hold">The diffusive third, in microseconds.</param>
    [Theory]
    [InlineData("0.1", "0.1", "3.3")]
    [InlineData("1", "1", "3")]
    public void ALegEndingAtAFieldChangeRunsToTheBoundary(string settle, string extract, string hold)
    {
        var path = Model($$"""
            [ { "name": "settle", "duration": { "value": {{settle}}, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 1000, "unit": "V/m" } } },
              { "name": "extract", "duration": { "value": {{extract}}, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 2000000, "unit": "V/m" } } },
              { "name": "hold", "duration": { "value": {{hold}}, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
            """);

        var model = Core.Model.ModelValidator.Validate(Io.ModelJson.Parse(File.ReadAllText(path)), null, _root).Model!;
        var (field, _) = Fields.FieldAssembly.BuildReported(model);

        var run = SequencedRun.Execute(model, field, Io.GasFlowImport.Resolve(model.Gas, _root));

        output.WriteLine(string.Join("; ", run.Phases.Select(p => $"{p.Name}: {p.Trajectories} trajectories, {p.Arrived} arrived")));

        Assert.Equal(3, run.Phases.Count);
    }

    /// <summary>
    /// A phase the run cannot resolve - its end, added up as the run adds it, equal to its start,
    /// or after it by less than the integrator's smallest step - is refused as a model error at its
    /// duration, where it used to pass validation and fail in the field as an internal error, or
    /// reach the run as a leg that underflows at once. A zero duration is refused at the same path.
    /// </summary>
    /// <param name="before">Each of the two phases before it, in microseconds.</param>
    /// <param name="duration">The third phase's duration, in microseconds.</param>
    /// <param name="says">What the refusal says.</param>
    /// <param name="mode">How the third phase is run.</param>
    [Theory]
    [InlineData("3", "1e-30", "its end is its start", "diffusion")]
    [InlineData("3", "1e-30", "its end is its start", "trajectory")]
    [InlineData("3", "0", "positive time", "diffusion")]
    public void APhaseThatEndsWhereItBeginsIsRefusedAtItsDuration(string before, string duration, string says, string mode)
    {
        var path = Model($$"""
            [ { "name": "hold", "duration": { "value": {{before}}, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } },
              { "name": "push", "duration": { "value": {{before}}, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 100, "unit": "V/m" } } },
              { "name": "blip", "duration": { "value": {{duration}}, "unit": "us" }, "mode": "{{mode}}", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
            """);

        foreach (var verb in new[] { "validate", "run" })
        {
            var (exit, stdout, stderr) = Run(verb, path, "--json");

            output.WriteLine($"{verb}: exit {exit}: {stdout.Trim()} {stderr.Trim()}");

            Assert.Equal(1, exit);

            var error = JsonNode.Parse(stdout)!["errors"]![0]!;

            Assert.Equal("VALUE_OUT_OF_BOUNDS", error["code"]!.GetValue<string>());
            Assert.Equal("/sequence/2/duration", error["path"]!.GetValue<string>());
            Assert.Contains(says, error["constraint"]!.GetValue<string>(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A phase refused for something other than its duration still lasts what it says, so the
    /// next phase's length is measured against the time it really starts at.
    /// </summary>
    /// <remarks>
    /// A tenth of a femtosecond after three seconds is no time at all - three plus 1e-17 is three -
    /// and after nothing it is a resolvable phase. Skipped in the running total because the phase
    /// before it named a mode that does not exist, the blip was measured from zero and passed.
    /// </remarks>
    [Fact]
    public void APhaseRefusedForItsModeStillCountsTowardTheNextOnesStart()
    {
        var path = Model("""
            [ { "name": "hold", "duration": { "value": 3, "unit": "s" }, "mode": "hover", "set": { "pushField": { "value": 0, "unit": "V/m" } } },
              { "name": "blip", "duration": { "value": 1e-17, "unit": "s" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
            """);

        var (exit, stdout, stderr) = Run("validate", path, "--json");

        output.WriteLine($"exit {exit}: {stdout.Trim()} {stderr.Trim()}");

        Assert.Equal(1, exit);

        var paths = JsonNode.Parse(stdout)!["errors"]!.AsArray().Select(e => e!["path"]!.GetValue<string>()).ToList();

        Assert.Contains("/sequence/0/mode", paths);
        Assert.Contains("/sequence/1/duration", paths);
    }

    /// <summary>
    /// One mistake on the parameter surface is one error, however many phases meet it; a ramp's two
    /// ends failing at different values are two.
    /// </summary>
    /// <param name="ramped">Whether one phase ramps the parameter rather than five setting it.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASurfaceMistakeIsReportedOnce(bool ramped)
    {
        // A length handed to a field: every phase that resolves the surface with it meets the
        // same mismatch at the parameter. And a ramp that takes a parameter where a derived one
        // cannot follow meets that at its end and again at its middle, at different values.
        var sequence = ramped
            ? """
              [ { "name": "ramp", "duration": { "value": 1, "unit": "us" }, "mode": "diffusion", "ramp": { "share": { "value": -3, "unit": "1" } } } ]
              """
            : "[ " + string.Join(", ", Enumerable.Range(0, 5).Select(k =>
                $$"""{ "name": "p{{k}}", "duration": { "value": 1, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 1, "unit": "mm" } } }""")) + " ]";

        var path = Model(sequence);
        var document = JsonNode.Parse(File.ReadAllText(path))!;
        document["parameters"]!["share"] = JsonNode.Parse("""{ "value": 0.5, "unit": "1" }""");
        document["parameters"]!["root"] = JsonNode.Parse("""{ "expression": "asinPi(share)", "unit": "1" }""");
        File.WriteAllText(path, document.ToJsonString());

        var (exit, stdout, stderr) = Run("validate", path, "--json");

        output.WriteLine($"exit {exit}: {stdout.Trim()} {stderr.Trim()}");

        Assert.Equal(1, exit);

        var atParameter = JsonNode.Parse(stdout)!["errors"]!.AsArray()
            .Where(e => e!["path"]!.GetValue<string>() == (ramped ? "/parameters/root" : "/parameters/pushField"))
            .Select(e => e!["constraint"]!.GetValue<string>())
            .ToList();

        // Five phases meeting one mismatch are one error. A ramp's end and middle meeting the same
        // derived parameter at two different values are two different failures, each said once:
        // what is merged is the same error, not the same place.
        Assert.Equal(ramped ? 2 : 1, atParameter.Count);
        Assert.Equal(atParameter.Count, atParameter.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A phase shorter than the trajectory integrator's smallest step validates and runs, flown as
    /// a separate trajectory leg between density phases and as one staged flight alike.
    /// </summary>
    /// <remarks>
    /// It used to be refused at its duration, because a leg that short underflowed at once. The
    /// integrator now reaches a ceiling or a switch closer than its smallest step, so what was a
    /// refusal of a working document is gone; what is still refused is a phase of no time at all.
    /// </remarks>
    /// <param name="separateLeg">Whether the short phase is a trajectory leg between density phases.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APhaseShorterThanTheSmallestStepRuns(bool separateLeg)
    {
        var around = separateLeg ? "diffusion" : "trajectory";
        var path = Model($$"""
            [ { "name": "hold", "duration": { "value": 0.5, "unit": "us" }, "mode": "{{around}}", "set": { "pushField": { "value": 1000, "unit": "V/m" } } },
              { "name": "blip", "duration": { "value": 1e-13, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 2000000, "unit": "V/m" } } },
              { "name": "after", "duration": { "value": 0.5, "unit": "us" }, "mode": "{{around}}", "set": { "pushField": { "value": 1000, "unit": "V/m" } } } ]
            """);

        var (validateExit, validateOut, _) = Run("validate", path, "--json");
        var (runExit, runOut, runErr) = Run("run", path, "--json");

        output.WriteLine($"validate: exit {validateExit}; run: exit {runExit}: {runErr.Trim()}");

        Assert.DoesNotContain("smallest step", validateOut, StringComparison.Ordinal);
        Assert.Equal(0, validateExit);
        Assert.Equal(0, runExit);
        Assert.DoesNotContain("StepSizeUnderflow", runOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// A phase refused as too short to resolve still has the rest of it checked, so a mistake in
    /// its mode is reported in the same round.
    /// </summary>
    [Fact]
    public void APhaseTooShortStillHasItsModeChecked()
    {
        var path = Model("""
            [ { "name": "hold", "duration": { "value": 3, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } },
              { "name": "blip", "duration": { "value": 1e-30, "unit": "us" }, "mode": "hover", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
            """);

        var (exit, stdout, stderr) = Run("validate", path, "--json");

        output.WriteLine($"exit {exit}: {stdout.Trim()} {stderr.Trim()}");

        Assert.Equal(1, exit);

        var paths = JsonNode.Parse(stdout)!["errors"]!.AsArray().Select(e => e!["path"]!.GetValue<string>()).ToList();

        Assert.Contains("/sequence/1/duration", paths);
        Assert.Contains("/sequence/1/mode", paths);
    }

    /// <summary>A duration that is not a finite number is refused as not finite, at the duration.</summary>
    /// <remarks>
    /// 1e306 hours is a finite number of hours and infinitely many seconds: the document's value is
    /// checked finite before it is converted, and the conversion overflows. Infinity is positive,
    /// and infinity minus the time before it is no shorter than the smallest step, so it passed
    /// both checks and reached the run as a phase with no end.
    /// </remarks>
    [Fact]
    public void ADurationThatIsNotFiniteIsRefused()
    {
        var path = Model("""
            [ { "name": "hold", "duration": { "value": 1e306, "unit": "h" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
            """);

        var (exit, stdout, stderr) = Run("validate", path, "--json");

        output.WriteLine($"exit {exit}: {stdout.Trim()} {stderr.Trim()}");

        Assert.Equal(1, exit);

        var error = JsonNode.Parse(stdout)!["errors"]![0]!;

        Assert.Equal("/sequence/0/duration", error["path"]!.GetValue<string>());
        Assert.Contains("finite", error["constraint"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The estimate's pilot through a phase flies the leg the run flies - the phase's end less
    /// where the run started it - not the declared duration, which differs from that by the
    /// rounding of the sum.
    /// </summary>
    /// <remarks>
    /// After 0.1 microseconds, 1.3 more ends 1.3000000000000003 microseconds later, so the
    /// second leg is an ulp longer than its declared duration; a pilot bounded by the duration stops short of the switch the leg
    /// lands on, and one bounded by a duration longer than the leg flies past it into a phase the
    /// leg never enters.
    /// </remarks>
    [Fact]
    public void APilotFliesTheLegTheRunFlies()
    {
        var path = Model("""
            [ { "name": "a", "duration": { "value": 0.1, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 1000, "unit": "V/m" } } },
              { "name": "b", "duration": { "value": 1.3, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 2000, "unit": "V/m" } } },
              { "name": "c", "duration": { "value": 0.3, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 3000, "unit": "V/m" } } } ]
            """);

        var model = Core.Model.ModelValidator.Validate(Io.ModelJson.Parse(File.ReadAllText(path)), null, _root).Model!;

        // As SequencedRun walks it: each leg from the end of the one before.
        var started = 0.0;
        var differs = 0;

        foreach (var phase in model.Phases)
        {
            var leg = phase.EndsAtSeconds - started;
            differs += leg != phase.DurationSeconds ? 1 : 0;

            Assert.Equal(leg, EstimateCommand.Leg(phase, Core.Model.CompiledPhase.StartOf(model.Phases, phase)));

            started = phase.EndsAtSeconds;
        }

        Assert.True(differs > 0, "the durations chosen must make some leg differ from its duration");
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
}
