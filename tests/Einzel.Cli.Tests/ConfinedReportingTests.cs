using System.Text.Json.Nodes;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A trap that works and a trap that lost everything both transmit nothing, so a run has
/// to report what is still inside.
/// </summary>
/// <remarks>
/// <para>
/// A trapped ion by definition never arrives anywhere, so transmission reads zero for a
/// trap doing exactly its job. Without a second number the terminal shows the alarming
/// figure and not the descriptive one, and <c>paul-trap-held</c> — a shipped example that
/// behaves as designed — reads as a total failure.
/// </para>
/// <para>
/// <b>Counted from the flight the run already did</b>, not by calling the <c>confined</c>
/// figure of merit, which re-flies the whole ensemble. Two implementations of one quantity
/// is the defect that made <c>run</c> and <c>test</c> disagree twice in this project — the
/// second time because a declared gas reached only one of them.
/// </para>
/// </remarks>
public sealed class ConfinedReportingTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-confined", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
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

    private string Materialise(string example)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", $"{example}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, "--from-example", example).ExitCode);
        }

        return path;
    }

    private double Confined(string example)
    {
        var run = Cli("run", Materialise(example), "--json");

        // NOT asserting exit 0, and that is a finding rather than a concession. A run
        // that ends at its flight-time limit exits 4, so A TRAP THAT WORKS EXITS WITH A
        // FAILURE CODE - the third time here that the exit logic has been a list of the
        // outcomes known when it was written rather than the question "did this run finish
        // what it was asked to do". It was fixed that way once for diffusive runs and once
        // for sequenced ones. The fix for traps needs to tell "held its ions" from "lost
        // its beam", which is not the same judgement, so it is written down rather than
        // guessed at here.
        var confined = JsonNode.Parse(run.Stdout)!["ensemble"]!["confined"]!["value"]!
            .GetValue<double>();

        output.WriteLine($"{example,-20} confined {confined:P1}");

        return confined;
    }

    /// <summary>A held trap and an ejected one are told apart by what is still inside.</summary>
    /// <remarks>
    /// <b>Both halves, because neither is worth anything alone.</b> Reporting 100% confined
    /// for the held trap proves nothing if the ejected one reports it too — that would just
    /// be a run that never notices anything leaving. The two examples differ in one number,
    /// the drive amplitude, and bracket the published stability boundary from either side.
    /// </remarks>
    [Fact]
    public void AHeldTrapAndAnEjectedOneAreToldApart()
    {
        var held = Confined("paul-trap-held");
        var ejected = Confined("paul-trap-ejected");

        Assert.Equal(1.0, held, 6);
        Assert.Equal(0.0, ejected, 6);
    }

    /// <summary>The terminal says it, and only when there is something to say.</summary>
    /// <remarks>
    /// A beamline holds nothing at the end of its run, so a line of zeros on every ordinary
    /// model would be noise on the surface CLI-2 keeps for results. The line earns its place
    /// by being absent where it would say nothing.
    /// </remarks>
    [Fact]
    public void TheLineAppearsForATrapAndNotForABeamline()
    {
        var trap = Cli("run", Materialise("paul-trap-held"));
        var beamline = Cli("run", Materialise("slit-transmission"));

        Assert.Contains("confined", trap.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("confined", beamline.Stdout, StringComparison.Ordinal);

        output.WriteLine(
            trap.Stdout.Split('\n').First(l => l.Contains("confined", StringComparison.Ordinal)));
    }
}
