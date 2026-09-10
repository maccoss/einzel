using System.Text.Json;

using Einzel.Commands;
using Einzel.Project;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A run measured in hours says where it has got to before it ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap these close is an engineering one, and it made a study unrunnable.</b> A
/// driven diffusive window is set by a Courant limit against a ponderomotive gradient, so
/// it is hundreds of thousands of steps whatever each one costs. The TIMS front-end
/// sequence failed to finish three times — 4.75 CPU-hours, then 40 minutes, then 7.6
/// wall-hours ended by a Windows update rebooting the machine — and <em>nothing was
/// observed on any of the three</em>, so whether the estimate was low or the run did not
/// terminate could not be told apart. On a machine nobody fully controls, a run that
/// cannot be watched cannot be run.
/// </para>
/// <para>
/// <b>Two claims, and the second is the one that could go wrong quietly.</b> That something
/// is emitted before the end is visible the moment it works. That watching does not
/// <em>change</em> the answer is not: an observer handed a live buffer could perturb what
/// it records, and its output would then look exactly like a measurement. So the checkpoint
/// is checked against a run with no watcher, to the last bit.
/// </para>
/// </remarks>
public sealed class RunCheckpointTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-checkpoint", Guid.NewGuid().ToString("N"));

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
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>
    /// A packet held in a gas, in phases, so there is a timeline to be part way through.
    /// </summary>
    /// <remarks>
    /// Two holds rather than one, because half of what a checkpoint is for is the phases
    /// that <em>finished</em>: a run killed in the sixth phase of eight has five real
    /// measurements in it, and before this they went with the process.
    /// </remarks>
    private const string HeldInPhases = """
    {
      "schemaVersion": "0.6",
      "name": "held-in-phases",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [10, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 5, "unit": "V" },
        "cloud": {
          "ions": 100,
          "seed": 7,
          "temperature": { "value": 300, "unit": "K" },
          "transverseSpread": { "value": 0.5, "unit": "mm" },
          "longitudinalSpread": { "value": 0.5, "unit": "mm" }
        }
      },
      "sequence": [
        { "name": "first",  "duration": { "value": 30, "unit": "us" }, "mode": "diffusion" },
        { "name": "second", "duration": { "value": 30, "unit": "us" }, "mode": "diffusion" }
      ],
      "fields": [{ "type": "fieldFree" }],
      "detector": {
        "planePoint": { "value": [60, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "diffusion",
        "maximumFlightTime": { "value": 60, "unit": "us" },
        "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 32
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    private string Project()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "held.json");

        File.WriteAllText(path, HeldInPhases);

        return path;
    }

    private string Checkpoint => Path.Combine(_root, "results", "held.progress.json");

    /// <summary>It says where it has got to before it has finished.</summary>
    /// <remarks>
    /// The interval is small so that a run of a second reports as an eight-hour one would,
    /// which is the only way to test this without taking eight hours. What is asserted is
    /// the shape of the report rather than how many there were: how many arrive depends on
    /// how fast the machine is, and a count would be a test of the machine (Amendment 27).
    /// </remarks>
    [Fact]
    public void ItSaysWhereItHasGotToBeforeItEnds()
    {
        var (exit, _, stderr) = Run("run", Project(), "--progress", "0.001");

        Assert.Equal(0, exit);

        var reports = stderr.Split('\n')
            .Where(line => line.Contains(" of ", StringComparison.Ordinal)
                && line.Contains("steps", StringComparison.Ordinal))
            .ToArray();

        foreach (var report in reports.Take(4))
        {
            output.WriteLine(report.TrimEnd());
        }

        Assert.NotEmpty(reports);

        // Where in the sequence, which is a thing the transport stepping one leg cannot
        // know - so its presence says the phase structure reached the watcher.
        Assert.Contains("phase 1/2", stderr, StringComparison.Ordinal);
        Assert.Contains("phase 2/2", stderr, StringComparison.Ordinal);

        // And each phase says so when it finishes, whole.
        Assert.Contains("finished phase 1 of 2, first", stderr, StringComparison.Ordinal);
        Assert.Contains("finished phase 2 of 2, second", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The checkpoint is on disk while the run is going, and gone once it has finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The absence is the statement.</b> `results/` already holds two kinds of stored
    /// answer and a report command was written that read one of them, so a third file there
    /// that outlived its run would repeat that mistake on purpose. Removing it on success
    /// makes finding one mean "this run did not finish", which is the question somebody
    /// asks after a reboot — and it needs no timestamp comparison to answer.
    /// </para>
    /// <para>
    /// Checked by driving the writer as the solver does, because the mid-run state is by
    /// definition not observable from outside a finished run.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCheckpointOutlivesAKilledRunAndNotAFinishedOne()
    {
        Assert.Equal(0, Run("run", Project(), "--progress", "0.001").ExitCode);

        // The run finished, so its answer supersedes the checkpoint.
        Assert.False(File.Exists(Checkpoint), "a finished run left a checkpoint behind");
        Assert.True(File.Exists(Path.Combine(_root, "results", "held.result.json")));

        // AND THIS IS THE STATE A KILLED RUN IS FOUND IN, read while a run is actually in
        // it. Caught through the announcement rather than by constructing a density: the
        // mid-run document is what a reboot leaves behind, so reading a hand-made one
        // would be testing the writer against itself.
        string? caught = null;

        RunCommand.Execute(
            Path.Combine(_root, "models", "held.json"),
            new ProjectLayout(_root),
            exportVtu: false,
            timestampUtc: DateTimeOffset.UtcNow,
            progress: new RunProgress(
                0.001,
                _ => caught ??= File.Exists(Checkpoint) ? File.ReadAllText(Checkpoint) : null));

        Assert.NotNull(caught);

        var found = CommandJson.Read<RunCheckpointJson>(caught!)!;

        output.WriteLine(found.Note);
        output.WriteLine(
            $"{found.Phase} {found.PhaseIndex}/{found.PhaseCount} "
            + $"{found.AtUs:F3} of {found.OfUs:F1} us, {found.Steps} steps");

        // It says what it is, because whoever finds it after a reboot has no other way to
        // know - and a document in results/ that looked like an answer would be read as one.
        Assert.Contains("not its answer", found.Note, StringComparison.Ordinal);
        Assert.Contains("did not finish", found.Note, StringComparison.Ordinal);

        Assert.Equal("first", found.Phase);
        Assert.Equal(1, found.PhaseIndex);
        Assert.Equal(2, found.PhaseCount);
        Assert.Equal(30.0, found.OfUs, 6);

        // Part way through, which is the whole claim: this is a run's state before it has
        // one to report, so the instant it names is inside the phase rather than at its end.
        Assert.InRange(found.AtUs, 0.0, 30.0);

        var population = Assert.Single(found.Populations);

        Assert.True(population.Ions > 0.0);
        Assert.NotNull(population.SpreadMm);
        Assert.True(population.SpreadMm![0] > 0.0, "the packet had no width");

        // And it is gone again, because that run finished too.
        Assert.False(File.Exists(Checkpoint));
    }

    /// <summary>The phases that finished are in the checkpoint, whole.</summary>
    /// <remarks>
    /// This is the half that matters for a study rather than for a person watching. A
    /// relaxation curve is read by splitting a hold into phases so that every boundary
    /// reports a width; a run killed in the sixth phase has five real measurements in it,
    /// and the point of writing them down is that the process does not have to survive for
    /// them to be worth having.
    /// </remarks>
    [Fact]
    public void AFinishedPhaseIsKeptEvenThoughTheRunIsNot()
    {
        Directory.CreateDirectory(_root);

        var writer = new RunCheckpointWriter(
            Path.Combine(_root, "held.progress.json"),
            new RunProgress(0.0),
            Provenance);

        writer.Entering("first", 1, 2, 30e-6);
        writer.Completed(new PhaseOutcome(
            "first", "diffusion", 30e-6, 30e-6, 9876.0, 0,
            [21.09, 0.0], [0.7119, 0.1643], false, 0, [], 12, 1));

        var found = CommandJson.Read<RunCheckpointJson>(
            File.ReadAllText(Path.Combine(_root, "held.progress.json")))!;

        var phase = Assert.Single(found.Completed);

        Assert.NotNull(phase.SpreadMm);
        output.WriteLine($"{phase.Name} {phase.Mode} +- {phase.SpreadMm![0]:F4} mm");

        Assert.Equal("first", phase.Name);
        Assert.Equal(30.0, phase.EndsAtUs, 6);

        // The width, which is the whole subject of the study this was built for - and the
        // same field the result document and the report carry, through one conversion, so
        // a killed run's checkpoint and a finished run's result describe a phase alike.
        Assert.Equal(0.7119, phase.SpreadMm[0], 6);
        Assert.Equal(0.1643, phase.SpreadMm[1], 6);
    }

    /// <summary>Which run a hand-driven checkpoint belongs to.</summary>
    private static RunCheckpointProvenance Provenance => new(
        "models/held.json",
        "sha256:0000000000000000000000000000000000000000000000000000000000000000",
        "26.1.0+test",
        1,
        "a-machine",
        "2026-09-09T12:00:00.0000000+00:00");

    /// <summary>
    /// A run killed before it wrote a manifest still appears in the report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the case the whole checkpoint exists for, and it was very nearly
    /// unreachable.</b> The manifest is written <em>after</em> the run returns, because it
    /// records the transport modes the run actually used - so a run interrupted part way
    /// leaves a checkpoint and no manifest at all, and `verify` and the report both
    /// enumerate manifests. A reader for a state that cannot occur is the same defect
    /// `einzel report` was written to expose.
    /// </para>
    /// <para>
    /// So the checkpoint carries what a manifest would (PRJ-3), none of it derived from the
    /// outcome - which is what makes it writable before the first step - and the report
    /// lists an orphan on its own. <b>Never as current</b>, whatever the model hash says:
    /// `verify`'s question is whether a stored answer still stands and there is no stored
    /// answer, so reporting one as current would be the shape of answer that stops an
    /// investigation.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARunKilledBeforeItsManifestIsStillReported()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        File.WriteAllText(Path.Combine(_root, "models", "held.json"), HeldInPhases);
        Directory.CreateDirectory(Path.Combine(_root, "results"));

        var writer = new RunCheckpointWriter(Checkpoint, new RunProgress(0.0), Provenance);

        writer.Entering("first", 1, 2, 30e-6);
        writer.Completed(new PhaseOutcome(
            "first", "diffusion", 30e-6, 30e-6, 9876.0, 0,
            [21.09, 0.0], [0.7119, 0.1643], false, 0, [], 12, 1));

        Assert.True(File.Exists(Checkpoint));
        Assert.False(File.Exists(Path.Combine(_root, "results", "held.manifest.json")));

        var (exit, stdout, _) = Run("report", _root, "--json");

        Assert.Equal(0, exit);

        var report = JsonDocument.Parse(stdout).RootElement;

        var run = Assert.Single(report.GetProperty("runs").EnumerateArray().ToArray());

        // The provenance a manifest would have carried, off the checkpoint.
        Assert.Equal(Provenance.ModelHash, run.GetProperty("modelHash").GetString());
        Assert.Equal(Provenance.EngineVersion, run.GetProperty("engineVersion").GetString());
        Assert.Equal(Provenance.Machine, run.GetProperty("machine").GetString());
        Assert.Equal(Provenance.StartedUtc, run.GetProperty("createdUtc").GetString());

        // What it is, and what it is not.
        Assert.False(run.GetProperty("current").GetBoolean());
        Assert.Contains(
            "did not finish", run.GetProperty("unfinished").GetString()!,
            StringComparison.Ordinal);

        // And the phase it got through, which is the measurement worth keeping.
        var phase = Assert.Single(run.GetProperty("phases").EnumerateArray().ToArray());

        Assert.Equal("first", phase.GetProperty("name").GetString());
        Assert.Equal("0.7119", phase.GetProperty("axialSpreadMm").GetString());

        // AND THE COUNTS AGREE WITH THE DIAGNOSTICS, which is where this command has now
        // been wrong twice. An interrupted run is not one that "stored a manifest and no
        // result document ... Re-running the model stores one" - that is advice for a
        // different problem, and following it would restart a run that is working.
        Assert.Equal(0, report.GetProperty("withoutResult").GetInt32());
        Assert.Equal(1, report.GetProperty("interrupted").GetInt32());

        var codes = report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        output.WriteLine(string.Join(", ", codes));

        Assert.Contains("report.run-interrupted", codes);
        Assert.DoesNotContain("report.manifest-without-result", codes);

        // It reaches the page too, with its own state on it - and the terminal, which is
        // where a person actually meets it (CLI-2 puts these on stderr).
        var plain = Run("report", _root);

        Assert.Equal(0, plain.ExitCode);
        Assert.Contains("did not finish", plain.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "stored no result document", plain.Stderr, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(_root, "report.html"));

        Assert.Contains("interrupted", page, StringComparison.Ordinal);
        Assert.Contains("The timeline it walked", page, StringComparison.Ordinal);
    }

    /// <summary>Watching a run does not change its answer.</summary>
    /// <remarks>
    /// <para>
    /// <b>The claim that could fail quietly.</b> The observer is handed the solver's own
    /// live density buffer rather than a copy — which is what makes reporting cheap enough
    /// to do at all — so a consumer that wrote into it, or a solver that took a different
    /// step because somebody was looking, would produce numbers that still look like
    /// measurements. And <em>which</em> steps report is set by the wall clock, so if the
    /// answer depended on it at all it would not even be reproducible.
    /// </para>
    /// <para>
    /// Reported to the last digit rather than to a tolerance: these are two runs of one
    /// seeded model, so anything but equality is a defect and a tolerance would only hide
    /// how large one.
    /// </para>
    /// </remarks>
    [Fact]
    public void WatchingARunDoesNotChangeIt()
    {
        var model = Project();

        var silent = Sequence(Run("run", model, "--json", "--progress", "0").Stdout);
        var watched = Sequence(Run("run", model, "--json", "--progress", "0.001").Stdout);

        output.WriteLine(watched);

        Assert.Equal(silent, watched, StringComparer.Ordinal);

        static string Sequence(string stdout)
            => JsonDocument.Parse(stdout).RootElement.GetProperty("sequence")
                .GetRawText();
    }

    /// <summary>Asking for no progress writes nothing and says nothing.</summary>
    /// <remarks>
    /// A run of a few seconds does not want a checkpoint, and a caller scripting many of
    /// them wants stderr for its own diagnostics. `--progress 0` is that, and it has to
    /// leave <em>no</em> file: a checkpoint written and then removed is indistinguishable
    /// from one never written, right up until the run is killed between the two.
    /// </remarks>
    [Fact]
    public void SilenceCanBeAskedFor()
    {
        var (exit, _, stderr) = Run("run", Project(), "--progress", "0");

        Assert.Equal(0, exit);
        Assert.False(File.Exists(Checkpoint));
        Assert.DoesNotContain("left in this phase", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("finished phase", stderr, StringComparison.Ordinal);
    }

    /// <summary>An interval that is not a number is refused rather than guessed at.</summary>
    [Fact]
    public void AnIntervalThatIsNotOneIsRefused()
    {
        var (exit, _, stderr) = Run("run", Project(), "--progress", "often");

        Assert.Equal(1, exit);
        Assert.Contains("--progress takes an interval in seconds", stderr, StringComparison.Ordinal);
    }
}
