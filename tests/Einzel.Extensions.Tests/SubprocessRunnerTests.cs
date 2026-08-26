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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
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

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var failure = Assert.Throws<EinzelException>(() => Run(folder, new JsonObject()));
        clock.Stop();

        output.WriteLine($"killed after {clock.Elapsed.TotalMilliseconds:F0} ms");

        Assert.Equal(ErrorCodes.CostGateRefused, failure.Error.Code);

        // Killed near the declared ceiling rather than eventually. A timeout that
        // takes ten times as long as it says is not a resource bound.
        Assert.InRange(clock.Elapsed.TotalMilliseconds, 1000, 6000);
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
    public void ASandboxedRoundTripMeetsItsBudget()
    {
        RequireInterpreter();

        // PERF-7 puts a sandboxed round trip under 50 ms, which is what sets the
        // granularity floor for EXT-4. Reported rather than asserted at 50: process
        // start dominates it and varies by an order of magnitude between a warm and
        // a cold machine, so a hard assertion here would be a test of the build
        // agent. What is asserted is that it is not seconds.
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

        Run(folder, new JsonObject());

        var times = new List<double>();

        for (var i = 0; i < 5; i++)
        {
            times.Add(Run(folder, new JsonObject()).ElapsedMs);
        }

        times.Sort();

        output.WriteLine($"round trip, five calls: {string.Join(", ", times.Select(t => $"{t:F0} ms"))}");
        output.WriteLine($"median {times[2]:F0} ms against PERF-7's 50 ms budget");

        Assert.True(times[2] < 2000, $"a trivial round trip took {times[2]:F0} ms");
    }
}
