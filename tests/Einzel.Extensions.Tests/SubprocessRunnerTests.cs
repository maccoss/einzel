using System.Text.Json.Nodes;
using Einzel.Core.Errors;
using Einzel.Extensions;
using Xunit.Abstractions;

namespace Einzel.Extensions.Tests;

/// <summary>
/// The default extension runner, end to end.
/// </summary>
/// <remarks>
/// These need a Python 3 on the path and say so loudly when there is not one, rather
/// than passing quietly. EXT-6 says a vendored interpreter ships with the
/// application and nothing is vendored yet, so "no interpreter" is a real state of
/// this build - and a test that goes green because it found nothing to test is the
/// silent cap this project refuses everywhere else.
/// </remarks>
public sealed class SubprocessRunnerTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-ext", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // Tolerant, because a test that fails in its own teardown reports the
        // teardown rather than the test. A killed extension can hold its working
        // directory for a moment on Windows even after the runner has waited for it.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static readonly string? Interpreter = SubprocessRunner.Discover();

    /// <summary>Fails with the reason rather than passing on an absent interpreter.</summary>
    private static void RequireInterpreter() =>
        Assert.True(
            Interpreter is not null,
            "no Python 3 was found on the path. EXT-6 wants a vendored interpreter and this "
            + "build discovers one instead, so extension tests need python3 installed. Run "
            + "'einzel doctor' to see what was found.");

    private string Install(string name, string manifest, string python)
    {
        var folder = Path.Combine(_root, "extensions", name);

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ExtensionCatalogue.ManifestName), manifest);
        File.WriteAllText(Path.Combine(folder, "extension.py"), python);

        return folder;
    }

    private ExtensionResult Run(string folder, JsonNode? input)
    {
        var manifest = ExtensionCatalogue.Read(
            Path.Combine(folder, ExtensionCatalogue.ManifestName));

        return new SubprocessRunner(Interpreter!)
            .Run(manifest, folder, input, Path.Combine(_root, "scratch"));
    }

    [Fact]
    public void AnObjectiveExtensionRoundTrips()
    {
        RequireInterpreter();

        var folder = Install(
            "penalty",
            """
            {
              "manifestVersion": "0.1",
              "name": "penalty",
              "version": "1.0.0",
              "description": "A objective that prefers a short instrument.",
              "kind": "objective",
              "trust": "sandboxed",
              "entry": "extension.py",
              "function": "run",
              "outputSchema": {
                "type": "object",
                "required": ["value"],
                "properties": { "value": { "type": "number" } }
              }
            }
            """,
            """
            def run(payload):
                length = payload["lengthMm"]
                resolution = payload["resolvingPower"]

                return {"value": length / max(resolution, 1.0), "lengthMm": length}
            """);

        var result = Run(folder, new JsonObject
        {
            ["lengthMm"] = 420.0,
            ["resolvingPower"] = 8347.0,
        });

        output.WriteLine($"returned {result.Output!.ToJsonString()}");
        output.WriteLine($"round trip {result.ElapsedMs:F1} ms");

        Assert.Equal(420.0 / 8347.0, result.Output["value"]!.GetValue<double>(), 1e-12);

        // GRD-6: the result says who computed it and cannot pass as first-party.
        Assert.Contains(result.Warnings, w => w.Code == "extension.attributed");
        Assert.Contains(result.Warnings, w => w.Message.Contains("penalty", StringComparison.Ordinal));

        // And the sandbox says what it did not do.
        var isolation = Assert.Single(result.Warnings, w => w.Code == "extension.isolation-incomplete");

        Assert.False(isolation.IsSuppressible);
    }

    [Fact]
    public void OutputIsCheckedAgainstTheDeclaredSchema()
    {
        RequireInterpreter();

        // EXT-7. Without this the wrong shape becomes a null somewhere downstream
        // and the traceback points at the engine rather than at the extension.
        var folder = Install(
            "wrong-shape",
            """
            {
              "name": "wrong-shape",
              "version": "0.1.0",
              "kind": "objective",
              "entry": "extension.py",
              "outputSchema": {
                "type": "object",
                "required": ["value"],
                "properties": { "value": { "type": "number" } }
              }
            }
            """,
            """
            def run(payload):
                return {"score": 3.0}
            """);

        var failure = Assert.Throws<EinzelException>(() => Run(folder, new JsonObject()));

        output.WriteLine(failure.Error.ToString());

        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Contains("required property 'value'", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void ATracebackReachesTheCaller()
    {
        RequireInterpreter();

        // AGT-3: an error is a recovery instruction, and the only thing that says
        // what went wrong inside somebody's Python is their own traceback. Dropping
        // it in favour of "the extension failed" is the failure mode that makes
        // people stop writing extensions.
        var folder = Install(
            "throws",
            """
            {
              "name": "throws",
              "version": "0.1.0",
              "kind": "analysis",
              "entry": "extension.py"
            }
            """,
            """
            def run(payload):
                raise ValueError("the mirror separation is negative")
            """);

        var failure = Assert.Throws<EinzelException>(() => Run(folder, new JsonObject()));

        output.WriteLine(failure.Error.Suggestion);

        Assert.Contains("ValueError", failure.Error.Suggestion!, StringComparison.Ordinal);
        Assert.Contains("mirror separation is negative", failure.Error.Suggestion!, StringComparison.Ordinal);
    }

    /// <summary>The timeout in the manifest below, named so the assertions cannot drift.</summary>
    private const int TimeoutMs = 1200;

    [Fact]
    public void ARunawayExtensionIsKilledAtItsDeclaredTimeout()
    {
        RequireInterpreter();

        var folder = Install(
            "spins",
            """
            {
              "name": "spins",
              "version": "0.1.0",
              "kind": "objective",
              "entry": "extension.py",
              "resources": { "timeoutMs": 1200 }
            }
            """,
            """
            def run(payload):
                while True:
                    pass
            """);

        // Warmed, so the first call's file-system cache miss is nobody's measurement,
        // and measured, because what follows has to be scale-free.
        Bare();

        var bare = Cheapest(Bare);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var failure = Assert.Throws<EinzelException>(() => Run(folder, new JsonObject()));
        clock.Stop();

        var elapsed = clock.Elapsed.TotalMilliseconds;

        output.WriteLine($"interpreter start alone   {bare,8:F0} ms");
        output.WriteLine($"runaway killed after      {elapsed,8:F0} ms");
        output.WriteLine($"enforcement's own share   {elapsed - bare,8:F0} ms  "
            + $"against {TimeoutMs} declared");

        Assert.Equal(ErrorCodes.CostGateRefused, failure.Error.Code);

        // It waited. This catches a run that failed early for some other reason and
        // reported a timeout it never actually served.
        Assert.True(
            elapsed >= 0.8 * TimeoutMs,
            $"the runaway was stopped after {elapsed:F0} ms against a declared "
            + $"{TimeoutMs} ms, which is too early to have been the timeout");

        // AND IT WAS KILLED NEAR ITS DECLARED CEILING - measured over and above the cost
        // of starting an interpreter at all, which is not this platform's to control.
        //
        // The absolute version of this assertion failed on a Windows build agent at
        // 7,225 ms against a 6,000 ms ceiling, and that number is not a measurement of
        // the timeout: it is the timeout plus however long that agent takes to start
        // CPython, which here is 45-63 ms and on a loaded shared runner is seconds.
        //
        // The test twenty lines below already says this at length for PERF-7 - "a hard
        // assertion here would be a test of the build agent" - having got it wrong twice.
        // This one made the same mistake and nobody noticed, because it only shows on a
        // machine slow enough to separate the two costs.
        Assert.True(
            elapsed - bare < 5.0 * TimeoutMs,
            $"once the {bare:F0} ms of interpreter start is taken off, stopping the "
            + $"runaway took {elapsed - bare:F0} ms against a declared {TimeoutMs} ms. "
            + "That is the enforcement being slow rather than the machine being slow, "
            + "and a timeout that takes several times as long as it says is not a "
            + "resource bound");
    }

    [Fact]
    public void TheChildInheritsNoEnvironment()
    {
        RequireInterpreter();

        // A child that starts with the parent's environment starts with its
        // credentials, its proxy settings, and its PYTHONPATH. This is one of the
        // containment measures that can be enforced portably, so it is enforced
        // rather than listed as future work.
        Environment.SetEnvironmentVariable("EINZEL_SECRET_UNDER_TEST", "hunter2");

        try
        {
            var folder = Install(
                "peeks",
                """
                {
                  "name": "peeks",
                  "version": "0.1.0",
                  "kind": "analysis",
                  "entry": "extension.py"
                }
                """,
                """
                import os


                def run(payload):
                    return {
                        "sawSecret": "EINZEL_SECRET_UNDER_TEST" in os.environ,
                        "variables": len(os.environ),
                    }
                """);

            var result = Run(folder, new JsonObject());

            output.WriteLine($"the child saw {result.Output!["variables"]} environment variables");

            Assert.False(result.Output["sawSecret"]!.GetValue<bool>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EINZEL_SECRET_UNDER_TEST", null);
        }
    }

    [Fact]
    public void ASandboxedRoundTripCostsLittleMoreThanStartingTheInterpreter()
    {
        RequireInterpreter();

        // PERF-7 puts a sandboxed round trip under 50 ms, which is what sets the
        // granularity floor for EXT-4.
        //
        // WHAT THIS ASSERTS, AND WHY IT IS NOT THE 50.
        //
        // The 50 ms is not separable from process start, because on an ordinary machine
        // it IS process start: measured here, launching the interpreter and doing nothing
        // costs 45.0, 49.6, 53.9 and 58.2 ms across four runs. The budget straddles that
        // spread, so asserting it is a coin toss on the interpreter's own start cost -
        // and on a shared build agent, where a bare launch takes seconds, it is not a
        // measurement of this platform at all.
        //
        // Two earlier versions got this wrong in instructive ways. The first said exactly
        // the right thing in its comment - "a hard assertion here would be a test of the
        // build agent" - and then asserted 2,000 ms, which is a guess about the build
        // agent; it passed and failed on the same commit in two runs minutes apart. The
        // second gated the budget on the floor being under it, which checks the budget
        // precisely when the floor leaves no room for anything else - failing by
        // construction in the only cases it ran.
        //
        // So what is asserted is scale-free and is the platform's own property: a round
        // trip costs little more than starting the interpreter at all. That is EXT-4's
        // structural claim - the subprocess boundary is the cost, and the marshalling on
        // top of it is not - and it holds on a fast machine and a slow one alike. Measured
        // at 1.08x to 1.28x here.
        //
        // The absolute number is reported on every run, because PERF-7 is a requirement
        // and a requirement that nothing measures is one nobody knows the state of. What
        // the reporting says is which part of it belongs to this platform. See SPEC.md's
        // amendment on PERF-7.
        var folder = Install(
            "trivial",
            """
            {
              "name": "trivial",
              "version": "0.1.0",
              "kind": "objective",
              "entry": "extension.py"
            }
            """,
            """
            def run(payload):
                return {"value": 1.0}
            """);

        // Warmed, so the first call's file-system cache miss is nobody's measurement.
        Run(folder, new JsonObject());
        Bare();

        var round = Cheapest(() => Run(folder, new JsonObject()).ElapsedMs);
        var bare = Cheapest(Bare);

        output.WriteLine($"interpreter start alone   {bare,8:F1} ms");
        output.WriteLine($"sandboxed round trip      {round,8:F1} ms");
        output.WriteLine($"the platform's share      {round - bare,8:F1} ms  ({round / bare:F2}x)");

        // Cheapest of several, because both are floors: the runtime and the operating
        // system charge one-off costs to whichever window they fall in, so the minimum
        // is the statistic that describes the thing rather than the contention around
        // it. Same reasoning as AllocationDoesNotGrowWithStepCount.
        Assert.True(
            round < 3.0 * bare,
            $"a round trip cost {round:F0} ms against {bare:F0} ms to start the "
            + $"interpreter at all - {round / bare:F1}x, so the marshalling has become "
            + "the dominant cost rather than the process boundary");

        output.WriteLine(
            round < BudgetMs
                ? $"PERF-7's {BudgetMs} ms budget: met, at {round:F0} ms"
                : $"PERF-7's {BudgetMs} ms budget: NOT met, at {round:F0} ms - of which "
                    + $"{bare:F0} ms is starting the interpreter and {round - bare:F0} ms "
                    + "is this platform. Not asserted, because the budget is not "
                    + "separable from process start; see SPEC.md's amendment on PERF-7");
    }

    /// <summary>PERF-7's round-trip budget, in milliseconds.</summary>
    private const double BudgetMs = 50.0;

    /// <summary>What it costs to start the interpreter and do nothing, in milliseconds.</summary>
    /// <remarks>
    /// The same isolation flag the sandbox uses, so the two measurements differ by the
    /// work rather than by how the process was launched.
    /// </remarks>
    private static double Bare()
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(Interpreter!)
            {
                ArgumentList = { "-I", "-c", "pass" },
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

        process.WaitForExit();

        return started.Elapsed.TotalMilliseconds;
    }

    /// <summary>The cheapest of five measurements, in milliseconds.</summary>
    private static double Cheapest(Func<double> measure)
    {
        var best = double.MaxValue;

        for (var i = 0; i < 5; i++)
        {
            best = Math.Min(best, measure());
        }

        return best;
    }
}
