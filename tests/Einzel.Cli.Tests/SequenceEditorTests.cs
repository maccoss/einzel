using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The instrument as a timed state machine (§16's sequence editor).
/// </summary>
public sealed class SequenceEditorTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-sequence", Guid.NewGuid().ToString("N"));

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

    private string Example(string name)
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Cli("new", path, "--from-example", name).ExitCode);

        return path;
    }

    /// <summary>A sequenced model reports its phases in order, with what each holds.</summary>
    [Fact]
    public void ASequencedModelReportsItsPhasesInOrder()
    {
        var outcome = SequenceCommand.Execute(Example("sequenced-extraction"));

        Assert.True(outcome.Sequenced);
        Assert.NotEmpty(outcome.Phases);

        foreach (var phase in outcome.Phases)
        {
            output.WriteLine(
                $"{phase.Name,-14} {phase.StartsAtUs,8:F3} to {phase.EndsAtUs,8:F3} us "
                + $"({phase.DurationUs,7:F3})  mode {phase.Mode,-10} "
                + $"{phase.ChangedCount} of {phase.Electrodes.Count} moved");

            foreach (var electrode in phase.Electrodes.Where(e => e.Changed))
            {
                output.WriteLine(
                    $"    -> {electrode.Name,-16} {electrode.PotentialVolts,10:F1} V DC, "
                    + $"{electrode.DriveAmplitudeVolts,8:F1} V drive");
            }
        }

        // Contiguous: each phase begins where the previous ended, so a reader can add the
        // durations and get the total rather than discovering a gap.
        for (var i = 1; i < outcome.Phases.Count; i++)
        {
            Assert.Equal(outcome.Phases[i - 1].EndsAtUs, outcome.Phases[i].StartsAtUs, 9);
        }

        Assert.Equal(outcome.Phases[^1].EndsAtUs, outcome.TotalUs, 9);
        Assert.All(outcome.Phases, p => Assert.True(p.DurationUs > 0.0));
    }

    /// <summary>The first phase moves nothing, because there is nothing before it.</summary>
    /// <remarks>
    /// A "changed" flag against the phase before is meaningless for the first one, and
    /// marking everything changed there would make the count useless exactly where a
    /// reader starts reading.
    /// </remarks>
    [Fact]
    public void TheFirstPhaseMovesNothing()
    {
        var outcome = SequenceCommand.Execute(Example("sequenced-extraction"));

        var first = outcome.Phases[0];

        output.WriteLine($"{first.Name}: {first.ChangedCount} moved of {first.Electrodes.Count}");

        Assert.Equal(0, first.ChangedCount);
        Assert.False(first.ModeChanged);
    }

    /// <summary>
    /// A phase that changes an excitation says which electrode, and only that one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The reason the view exists rather than a table.</b> A sequenced instrument repeats
    /// most of its state from phase to phase — a trap that holds at one voltage and pushes
    /// at another moves one electrode and leaves the rest — so a table repeating every
    /// setting buries the row that matters.
    /// </para>
    /// <para>
    /// The extraction example is exactly that: it holds everything at rest and then switches
    /// a field on, which is why it is the corpus's sequenced model.
    /// </para>
    /// </remarks>
    [Fact]
    public void APhaseThatChangesAnExcitationNamesTheElectrode()
    {
        var outcome = SequenceCommand.Execute(Example("sequenced-extraction"));

        var moving = outcome.Phases.Where(p => p.ChangedCount > 0).ToList();

        foreach (var phase in moving)
        {
            output.WriteLine(
                $"{phase.Name}: {string.Join(", ", phase.Electrodes.Where(e => e.Changed)
                    .Select(e => $"{e.Name} to {e.PotentialVolts:F1} V"))}");
        }

        Assert.NotEmpty(moving);

        // The marked set is exactly the set that differs, recomputed here from the phases
        // themselves. An upper bound on the count was the first version of this assertion
        // and it was wrong: the extraction moves BOTH its plates, to +500 and -500 V,
        // because a push-pull extraction is what it is. "Fewer than everything" is a
        // heuristic about instrument size rather than a statement about the diff.
        for (var i = 1; i < outcome.Phases.Count; i++)
        {
            var before = outcome.Phases[i - 1].Electrodes
                .ToDictionary(e => (e.Element, e.Name), e => (e.PotentialVolts, e.DriveAmplitudeVolts));

            foreach (var electrode in outcome.Phases[i].Electrodes)
            {
                var differs =
                    !before.TryGetValue((electrode.Element, electrode.Name), out var was)
                    || was.PotentialVolts != electrode.PotentialVolts
                    || was.DriveAmplitudeVolts != electrode.DriveAmplitudeVolts;

                Assert.Equal(differs, electrode.Changed);
            }
        }
    }

    /// <summary>A model with no sequence says so rather than showing an empty timeline.</summary>
    /// <remarks>
    /// An instrument holding one state for the whole run is the ordinary case, and it is a
    /// different thing from a sequence with nothing in it — which is what an empty table
    /// would suggest.
    /// </remarks>
    [Fact]
    public void AModelWithNoSequenceSaysSo()
    {
        var outcome = SequenceCommand.Execute(Example("single-stage-reflectron"));

        Assert.False(outcome.Sequenced);
        Assert.Empty(outcome.Phases);

        var said = Assert.Single(outcome.Warnings, w => w.Code == "sequence.none");

        output.WriteLine(said.Message);

        Assert.Contains("one state", said.Message, StringComparison.Ordinal);
    }

    /// <summary>The last phase holding after the end is said, not left to be assumed.</summary>
    /// <remarks>
    /// The sequencer holds the last state rather than switching everything off, and a reader
    /// of a timeline will otherwise assume the instrument stops when the table does. An ion
    /// still in flight would suddenly coast — a physics change disguised as a bookkeeping
    /// one.
    /// </remarks>
    [Fact]
    public void TheLastPhaseHoldingIsSaidRatherThanAssumed()
    {
        var outcome = SequenceCommand.Execute(Example("sequenced-extraction"));

        var said = Assert.Single(outcome.Warnings, w => w.Code == "sequence.last-phase-holds");

        output.WriteLine(said.Message);

        Assert.Contains(outcome.Phases[^1].Name, said.Message, StringComparison.Ordinal);
    }

    /// <summary>An analytic element's sequence is reported too (SEQ-1).</summary>
    /// <remarks>
    /// <para>
    /// <c>sequenced-uniform</c> is the corpus's only model whose timeline moves an
    /// <em>analytic</em> element, and it exists because a code review found the first fix
    /// for the lifted timeline reached only the solved ones. A model whose only elements are
    /// analytic compiled a timeline nothing consumed, making the sequence a silent no-op.
    /// </para>
    /// <para>
    /// It has no electrodes to list, so what is asserted is the timeline itself — a view
    /// that showed nothing for it would be repeating that defect in the presentation layer.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAnalyticElementsSequenceIsReportedToo()
    {
        var outcome = SequenceCommand.Execute(Example("sequenced-uniform"));

        Assert.True(outcome.Sequenced);
        Assert.True(outcome.Phases.Count >= 2);

        foreach (var phase in outcome.Phases)
        {
            output.WriteLine(
                $"{phase.Name,-12} {phase.StartsAtUs,8:F3} to {phase.EndsAtUs,8:F3} us, "
                + $"mode {phase.Mode}, {phase.Electrodes.Count} electrodes");
        }

        Assert.Equal(outcome.Phases[^1].EndsAtUs, outcome.TotalUs, 9);
    }
}
