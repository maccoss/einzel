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

    /// <summary>The figure of merit and the run agree over a declared cloud.</summary>
    /// <remarks>
    /// <para>
    /// <b>They did not.</b> `einzel run` counts what is still inside from the flight it
    /// did, over the declared cloud. The `confined` figure of merit — which is what
    /// `einzel test` and every study call — flew a deterministic energy scan instead and
    /// ignored the cloud entirely, where its sibling `transmission` had honoured one all
    /// along. So the two answered a question about confinement over two different
    /// populations.
    /// </para>
    /// <para>
    /// <b>It did not show on the shipped example, which is why it sat.</b>
    /// `paul-trap-held` declares no cloud: its source is at rest, so the scan collapses to
    /// a single ion and both routes fly the same one. A model with a cloud is what
    /// separates them, and this is that model — the same trap with twenty thermal ions.
    /// </para>
    /// <para>
    /// This is the third time in this project that one quantity computed two ways has
    /// diverged between `run` and `test`: once in a flight time, once because a declared
    /// gas reached only one path, and now this.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFigureAndTheRunAgreeOverADeclaredCloud()
    {
        var path = Materialise("paul-trap-held");

        var original = File.ReadAllText(path);

        // The trap already declares a cloud OF ONE ION, so widening it is the edit rather
        // than adding one. A first version of this test inserted a second "cloud" key and
        // System.Text.Json took the last of the two - the original - so the run flew one
        // ion and the agreement below was between two single-ion answers.
        //
        // Its guard was "the document contains cloud", which was already true. A check
        // that a string is PRESENT cannot see a no-op when the string was there before;
        // what has to be asserted is that the document CHANGED.
        // Twenty ions, and DELIBERATELY HOT ENOUGH THAT THE TRAP HOLDS ONLY PART OF
        // THEM. That is the only regime in which the two routes can differ: a trap that
        // holds everything reports 100% by either, and a trap that holds nothing reports
        // zero by either, so agreement there would prove nothing at all. At this
        // temperature the run holds 5% and the unfixed figure - which flies one nominal
        // launch rather than the cloud - says 100%.
        var document = original
            .Replace("\"ions\": 1,", "\"ions\": 20,", StringComparison.Ordinal)
            .Replace("\"value\": 300,", "\"value\": 300000,", StringComparison.Ordinal);

        Assert.NotEqual(original, document);

        File.WriteAllText(path, document);

        var run = Cli("run", path, "--json");

        var reported = JsonNode.Parse(run.Stdout)!["ensemble"]!["confined"]!["value"]!
            .GetValue<double>();

        var model = Core.Model.ModelValidator
            .Validate(Io.ModelJson.Parse(document))
            .Model!;

        var figure = Commands.FiguresOfMerit.Evaluator("confined")(model);

        output.WriteLine($"run reports {reported:P1}, the figure reports {figure:P1}");

        Assert.NotNull(figure);

        Assert.Equal(reported, figure!.Value, 6);

        // And the trap really is holding only part of the cloud, so the agreement above
        // is between two numbers that COULD have differed. Without this the test would
        // pass on a fully-confining trap where both routes trivially say 100%.
        Assert.InRange(reported, 0.001, 0.5);

        // And the cloud really was flown, rather than both routes collapsing to one ion
        // again - which would make the agreement above meaningless.
        Assert.Equal(
            20,
            JsonNode.Parse(run.Stdout)!["ensemble"]!["launched"]!.GetValue<int>());
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
