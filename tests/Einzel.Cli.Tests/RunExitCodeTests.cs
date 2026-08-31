using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A run that did what it was asked exits zero, whatever became of the ion.
/// </summary>
/// <remarks>
/// <para>
/// The exit code used to be a list of the outcome names that meant success when the line
/// was written — widened once for diffusive runs and once for sequenced ones, and still
/// wrong. <b>Six of the thirty-seven shipped examples exited with a failure code while
/// behaving exactly as designed.</b> A rule that calls a sixth of the reference corpus
/// broken is measuring the wrong thing.
/// </para>
/// <para>
/// The six split into two kinds, and both are covered below because they fail for different
/// reasons: three <b>holds</b>, which end at the declared flight time because that is the
/// point of a trap, and three <b>deliberate losses</b>, which are the control halves of
/// pairs and exist to show an ion being lost.
/// </para>
/// </remarks>
public sealed class RunExitCodeTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-exit", Guid.NewGuid().ToString("N"));

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

    /// <summary>A run that ends at its declared hold is a completed run.</summary>
    /// <remarks>
    /// A trapped ion by definition never arrives anywhere; a thermalising packet has no
    /// preferred direction and reaches no detector; an orbiting ion is measured over its
    /// turns. All three are asked to run for a stated time and do exactly that.
    /// </remarks>
    [Theory]
    [InlineData("paul-trap-held")]
    [InlineData("thermalisation")]
    [InlineData("orbital-trap-frequency")]
    public void AHoldThatRunsToItsEndSucceeds(string example)
    {
        var run = Cli("run", Materialise(example));

        output.WriteLine($"{example,-24} exit {run.ExitCode}");

        Assert.Equal(0, run.ExitCode);
    }

    /// <summary>A run whose ion is deliberately lost is a completed run.</summary>
    /// <remarks>
    /// Each of these is the control half of a pair: a Paul trap above its ejection
    /// threshold, a quadrupole above its stability cut-off, a funnel with the RF switched
    /// off. Losing the ion is what they are for — it is what makes the other half of the
    /// pair mean something — and an ion striking an electrode is what an aperture is for.
    /// </remarks>
    [Theory]
    [InlineData("paul-trap-ejected")]
    [InlineData("quadrupole-rf-unstable")]
    [InlineData("ion-funnel-no-rf")]
    public void ADeliberateLossSucceeds(string example)
    {
        var run = Cli("run", Materialise(example));

        output.WriteLine($"{example,-24} exit {run.ExitCode}");

        Assert.Equal(0, run.ExitCode);
    }

    /// <summary>Neither of the two reworded warnings claims a validity violation.</summary>
    /// <remarks>
    /// <para>
    /// <b>Severity is not decoration.</b> A validity violation means the result was computed
    /// outside the validity of the model used, and GRD-3 makes that class unsuppressible by
    /// any caller in any mode. Raising it on models that behave exactly as designed teaches
    /// readers to ignore the one class that must never be ignored.
    /// </para>
    /// <para>
    /// Both are still reported, and both are still non-suppressible — only <c>Advisory</c>
    /// is suppressible. What changed is what they claim.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("paul-trap-held", "TRAJECTORY_INCOMPLETE")]
    [InlineData("slit-transmission", "PEAK_UNRESOLVED")]
    public void AModelBehavingAsDesignedRaisesNoValidityViolationOfItsOwn(
        string example, string code)
    {
        var run = Cli("run", Materialise(example));

        // STDERR, not stdout. CLI-2 keeps results on stdout and diagnostics on stderr, and
        // the first version of this test looked on stdout - it passed a manual check only
        // because that check had been run with 2>&1, which merges the two streams and
        // destroys the distinction being relied on. This project has recorded that exact
        // mistake once already.
        var line = run.Stderr
            .Split('\n')
            .FirstOrDefault(l => l.Contains(code, StringComparison.Ordinal));

        Assert.NotNull(line);

        output.WriteLine(line!.Trim());

        // Reported, so the reader is still told.
        Assert.Contains(code, line!, StringComparison.Ordinal);

        // But as a qualification of the result rather than a claim that the model was used
        // outside its validity.
        Assert.Contains("[Qualified]", line!, StringComparison.Ordinal);
        Assert.DoesNotContain("[ValidityViolation]", line!, StringComparison.Ordinal);
    }

    /// <summary>An ordinary beamline is unaffected.</summary>
    /// <remarks>
    /// The control. This change makes runs stop failing, which is exactly the direction in
    /// which an exit code becomes vacuous, so the ordinary case has to be pinned as well —
    /// and the rule that keeps it from being vacuous is tested exhaustively over every
    /// outcome in <c>TrajectoryCompletionTests</c>.
    /// </remarks>
    [Fact]
    public void AnOrdinaryFlightStillSucceedsAndAMalformedModelStillFails()
    {
        Assert.Equal(0, Cli("run", Materialise("single-stage-reflectron")).ExitCode);

        // And the code is not hardwired to zero: a model that cannot be read still fails.
        var missing = Path.Combine(_root, "models", "does-not-exist.json");

        Assert.NotEqual(0, Cli("run", missing).ExitCode);
    }
}
