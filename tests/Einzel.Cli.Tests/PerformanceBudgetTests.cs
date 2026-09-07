using System.Diagnostics;
using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The section 20 performance budgets that were carried as Unverified: plausible, and
/// nobody had measured them.
/// </summary>
/// <remarks>
/// <para>
/// <b>These report rather than assert, and the ceiling they do assert is deliberately
/// loose.</b> A wall-clock bound is a statement about a machine, not about the platform -
/// SPEC.md Amendment 27, learned when an extension-timing assertion passed and failed on the
/// same commit in two CI runs minutes apart, because what it was really measuring was
/// CPython's process start on whichever runner it landed on. So the number is printed, and
/// the assertion is at ten times the budget: enough to catch a change that makes something
/// an order of magnitude slower, not tight enough to be a test of the runner.
/// </para>
/// <para>
/// Measured in process rather than through the executable, which is what the budgets are
/// about. A CLI invocation of the single-ion case costs 266-286 ms on the development
/// machine, and roughly 230 of that is a self-contained build starting its own runtime -
/// PERF-8's separate budget, and not what PERF-2 is asking about. The shell drives the same
/// command objects in process, and that is where "interactive tuning must feel live" applies.
/// </para>
/// </remarks>
public sealed class PerformanceBudgetTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-budgets", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Project()
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Run("init", _root).ExitCode);
        }

        return Path.Combine(_root, "models", "reflectron.json");
    }

    /// <summary>Cheapest of several, which is the right statistic for a floor.</summary>
    /// <remarks>
    /// The runtime charges one-off costs - tiering, a first-call recompilation - to whichever
    /// window they land in, and the quantity being measured is how fast this can go rather
    /// than how fast it went once. The same reasoning the allocation test settled on after it
    /// failed inside the full parallel suite and passed alone.
    /// </remarks>
    private static double CheapestMilliseconds(Action work, int repetitions)
    {
        var best = double.MaxValue;

        for (var i = 0; i < repetitions; i++)
        {
            var watch = Stopwatch.StartNew();
            work();
            watch.Stop();
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    /// <summary>PERF-2: a single ion through cached fields, against a 100 ms budget.</summary>
    [Fact]
    public void ASingleIonIsInteractive()
    {
        var model = Project();

        var milliseconds = CheapestMilliseconds(
            () => Assert.Equal(0, Run("run", model, "--json").ExitCode), 7);

        output.WriteLine($"PERF-2  single ion, in process: {milliseconds:F1} ms "
            + "against a 100 ms budget");

        Assert.True(
            milliseconds < 1000.0,
            $"a single ion took {milliseconds:F0} ms in process against a 100 ms budget. The "
            + "assertion is at ten times that, so this is an order-of-magnitude regression "
            + "rather than a machine being slow");
    }

    /// <summary>PERF-4: a ten-thousand-ion ensemble, against a five-minute budget.</summary>
    /// <remarks>
    /// One repetition, because the budget is five minutes and the point of taking the
    /// cheapest of several is to remove warm-up noise from a measurement small enough for
    /// warm-up to matter. Here it is not.
    /// </remarks>
    [Fact]
    public void ATenThousandIonEnsembleIsAffordable()
    {
        var model = Project();

        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(model))!;
        document["source"]!["cloud"] = System.Text.Json.Nodes.JsonNode.Parse(
            """
            {
              "ions": 10000, "seed": 11,
              "temperature": { "value": 300, "unit": "K" },
              "transverseSpread": { "value": 0.5, "unit": "mm" },
              "energyFractionSpread": 0.01
            }
            """);
        File.WriteAllText(model, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var milliseconds = CheapestMilliseconds(
            () => Assert.Equal(0, Run("run", model, "--json").ExitCode), 1);

        output.WriteLine($"PERF-4  10,000 ions: {milliseconds / 1000.0:F2} s "
            + "against a 300 s budget");

        Assert.True(
            milliseconds < 300_000.0,
            $"a 10,000-ion ensemble took {milliseconds / 1000.0:F1} s against a 300 s budget");
    }

    /// <summary>
    /// PERF-9 and PERF-10 are about a figure carrying a thousand trajectories, and the vector
    /// renderer draws one.
    /// </summary>
    /// <remarks>
    /// Recorded as a test rather than as a note so the gap is discovered the moment it
    /// closes: this asserts the current behaviour, so adding bundle rendering makes it fail
    /// and whoever adds it is told to come and measure the two budgets.
    /// </remarks>
    [Fact]
    public void AVectorFigureStillDrawsOneTrajectoryRatherThanABundle()
    {
        var model = Project();

        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(model))!;
        document["source"]!["cloud"] = System.Text.Json.Nodes.JsonNode.Parse(
            """{ "ions": 1000, "seed": 11, "temperature": { "value": 300, "unit": "K" } }""");
        File.WriteAllText(model, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var figure = Path.Combine(_root, "figures", "bundle.svg");
        Directory.CreateDirectory(Path.GetDirectoryName(figure)!);

        var (exit, stdout, _) = Run("render", "section", model, "--out", figure);
        output.WriteLine(stdout.Trim());

        Assert.Equal(0, exit);

        // One trajectory, however many ions the source declares - the decimation line says
        // so, singular. When that changes, PERF-9's five-second budget and PERF-10's 5 MB
        // ceiling become measurable and this should be replaced by two tests that measure
        // them.
        Assert.Contains("trajectory ", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("trajectories", stdout, StringComparison.Ordinal);
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
