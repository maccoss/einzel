using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// GRD-8's gate, and the three ways it misled a careful user. Found by an agent attempting
/// the acceptance suite, which shrank a 2000-draw study to 1500 to get past a number that
/// was itself twenty-six times too high.
/// </summary>
/// <remarks>
/// A gate that makes somebody make their work smaller is doing the opposite of its job. It
/// exists so that a multi-day run is not started by accident, not so that a one-second run
/// is abandoned.
/// </remarks>
public sealed class CostGateTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-cost-gate", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // The threshold is process-wide, so a test that moves it must put it back or the
        // next one in the same run inherits it.
        Einzel.Commands.EstimateCommand.Threshold = Einzel.Commands.EstimateCommand.ThresholdSeconds;

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Study(string figureOfMerit, int draws)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Run("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "studies", $"{figureOfMerit}-{draws}.json");

        File.WriteAllText(path, $$"""
        {
          "model": "../models/reflectron.json",
          "figureOfMerit": "{{figureOfMerit}}",
          "draws": {{draws}},
          "seed": 7,
          "maxParallelism": 1,
          "channels": [
            { "parameter": "turningDepth", "halfWidth": 0.2, "unit": "mm", "distribution": "uniform" },
            { "parameter": "capPotential", "halfWidth": 5, "unit": "V", "distribution": "uniform" }
          ]
        }
        """);

        return path;
    }

    /// <summary>
    /// A single-ion figure costs its convergence ladder, not the study's ion count. This
    /// charged 21 flights for the 3 the ladder flies - a sevenfold over-charge that put a
    /// one-second study over a thirty-second gate.
    /// </summary>
    [Fact]
    public void AConvergenceLadderIsChargedThreeFlightsNotTwentyOne()
    {
        var (exit, stdout, _) = Run("estimate", Study("flightTime", 2000));
        output.WriteLine(stdout.Trim());

        Assert.Equal(0, exit);
        Assert.Contains("3 trajectories", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("21 trajectories", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// And an ensemble figure still costs its ion count, which is the case the estimate was
    /// tuned against. Fixing one basis by breaking the other would be no fix.
    /// </summary>
    [Fact]
    public void AnEnsembleFigureIsStillChargedItsIonCount()
    {
        var (exit, stdout, _) = Run("estimate", Study("resolvingPower", 200));
        output.WriteLine(stdout.Trim());

        Assert.Equal(0, exit);
        Assert.Contains("21 trajectories", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal says the study is not blocked, because it is not: only `estimate` exits
    /// 3, and every study verb runs regardless. Saying only "this is above the threshold"
    /// on an exit code named cost-gate refusal reads as a prohibition, and was obeyed as one.
    /// </summary>
    [Fact]
    public void TheRefusalSaysTheStudyIsNotBlockedAndHowToMoveTheThreshold()
    {
        var (exit, _, stderr) = Run("estimate", Study("flightTime", 200_000));
        output.WriteLine(stderr.Trim());

        Assert.Equal(3, exit);
        Assert.Contains("not blocked", stderr, StringComparison.Ordinal);
        Assert.Contains("--threshold", stderr, StringComparison.Ordinal);
    }

    /// <summary>GRD-8 asks for a configurable threshold, and it was a constant.</summary>
    [Theory]
    [InlineData("3000", 0)]
    [InlineData("1", 3)]
    public void TheThresholdMoves(string seconds, int expected)
    {
        var (exit, _, stderr) = Run("estimate", Study("flightTime", 200_000), "--threshold", seconds);
        output.WriteLine($"--threshold {seconds} -> exit {exit}: {stderr.Trim()}");

        Assert.Equal(expected, exit);
    }

    /// <summary>A threshold that is not a positive number is refused, not silently ignored.</summary>
    [Theory]
    [InlineData("banana")]
    [InlineData("0")]
    [InlineData("-5")]
    public void AnUnusableThresholdIsRefused(string seconds)
    {
        var (exit, _, stderr) = Run("estimate", Study("flightTime", 2000), "--threshold", seconds);
        output.WriteLine($"--threshold {seconds} -> exit {exit}: {stderr.Trim()}");

        Assert.Equal(1, exit);
        Assert.Contains("positive number of seconds", stderr, StringComparison.Ordinal);
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
