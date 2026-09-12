using System.Text.Json;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport.Collisions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A figure of a sequenced model is a figure of what the sequence does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both render arms decided what to draw from the model's DECLARED transport mode
/// and then called the wholly diffusive solver.</b> Neither had ever routed through
/// <see cref="SequencedRun"/>, so a model with a timeline was drawn as though it had
/// none - the exit potential of an elution ramp never moved, the packet it should have
/// released stayed parked, and the command exited 0 with nothing anywhere saying the
/// sequence had been skipped.
/// </para>
/// <para>
/// It was found by the clock rather than by a test: a 126-frame animation of an 8 ms
/// elution came back in 25 seconds, where the run it was supposed to be filming takes
/// thirteen minutes. The frames were a real density, drawn correctly, of an instrument
/// nobody had described.
/// </para>
/// <para>
/// That is the same question <c>einzel run</c>'s fork was taught to stop asking, and
/// the second path it had to be corrected in - so what is asserted here is the
/// <em>routing</em>, end to end through the command surface, because the wiring is what
/// keeps breaking rather than the computation.
/// </para>
/// </remarks>
public sealed class SequencedRenderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A packet held still and then pushed, entirely in the diffusive description.
    /// </summary>
    /// <remarks>
    /// The field is an expression over one parameter whose <b>base value is zero</b>, and
    /// only the second phase sets it. So a run that ignores the timeline computes a packet
    /// that merely diffuses about where it was seeded, and one that honors it computes a
    /// packet that is carried down the axis. The two are far apart rather than subtly
    /// different, which is what a routing test needs.
    /// </remarks>
    internal const string HoldThenPush = """
    {
      "schemaVersion": "0.13",
      "name": "hold-then-push",
      "parameters": {
        "push": { "value": 0, "unit": "V/m", "minimum": 0, "maximum": 100000,
                  "description": "axial field, switched on by the second phase" }
      },
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [8, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 0, "unit": "V" },
        "cloud": {
          "ions": 200, "seed": 7,
          "temperature": { "value": 300, "unit": "K" },
          "transverseSpread": { "value": 0.5, "unit": "mm" },
          "longitudinalSpread": { "value": 0.5, "unit": "mm" }
        }
      },
      "sequence": [
        { "name": "hold", "duration": { "value": 40, "unit": "us" }, "mode": "diffusion",
          "set": { "push": { "value": 0, "unit": "V/m" } } },
        { "name": "push", "duration": { "value": 40, "unit": "us" }, "mode": "diffusion",
          "set": { "push": { "value": 20000, "unit": "V/m" } } }
      ],
      "fields": [
        { "type": "uniform", "field": { "expression": ["push", "0", "0"], "unit": "V/m" } }
      ],
      "detector": {
        "planePoint": { "value": [40, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "diffusion",
        "maximumFlightTime": { "value": 1, "unit": "ms" },
        "mobility": { "zeroField": { "value": 0.0002, "unit": "m^2/(V s)" } },
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

    private static SequencedOutcome Run(string text, params double[] snapshotSeconds)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(text));
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        var model = validation.Model!;

        return SequencedRun.Execute(
            model,
            FieldAssembly.BuildReported(model).Field,
            BackgroundGas.FromModel(model.Gas),
            snapshotSeconds: snapshotSeconds.Length > 0 ? snapshotSeconds : null);
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

    [Fact]
    public void SnapshotsComeBackOnTheInstrumentsClock()
    {
        // Two inside the hold, two inside the push. The legs run on their own clocks -
        // each diffusive phase starts its solver at zero - so an instant that came back
        // leg-local would put the last frame of an eighty-microsecond run at forty.
        var outcome = Run(HoldThenPush, 10e-6, 30e-6, 50e-6, 80e-6);

        Assert.True(
            outcome.Snapshots.Count == 4,
            "asked for 4 instants and got " + string.Join(
                ", ",
                outcome.Snapshots.Select(s =>
                    $"{s.RequestedSeconds * 1e6:G6}->{s.AtSeconds * 1e6:G6}")) + " us");

        Assert.Equal([10e-6, 30e-6, 50e-6, 80e-6], outcome.Snapshots.Select(s => s.RequestedSeconds));

        // The solver lands on the first step at or after what was asked for, so the instant
        // recorded is at or past it - never meaningfully before, which would be a different
        // packet. The slack is one rounding of the run length and no more: a global instant
        // is rebuilt by accumulating phase starts, and a sum of durations is equal to the
        // instant it represents only up to the last bit.
        var slack = outcome.Phases[^1].EndsAtSeconds * 1e-12;

        foreach (var shot in outcome.Snapshots)
        {
            Assert.True(
                shot.AtSeconds >= shot.RequestedSeconds - slack,
                $"asked for {shot.RequestedSeconds * 1e6:G6} us and recorded "
                    + $"{shot.AtSeconds * 1e6:G6} us");
        }

        Assert.Equal(
            outcome.Snapshots.Select(s => s.AtSeconds).OrderBy(x => x),
            outcome.Snapshots.Select(s => s.AtSeconds));
    }

    [Fact]
    public void ThePacketMovesOnlyDuringThePhaseThatPushesIt()
    {
        // Asked at the phase BOUNDARIES, which is where the claim lives. On this mesh each
        // forty-microsecond leg is covered by a single step - the drift Courant limit over a
        // 0.625 mm cell at 4 m/s is 156 us - so an instant inside a leg is served at the leg's
        // end anyway, and writing the test as though it sampled mid-phase would be asserting
        // a resolution the run does not have.
        var outcome = Run(HoldThenPush, 0.0, 40e-6, 80e-6);

        Assert.Equal(3, outcome.Snapshots.Count);

        var seeded = outcome.Snapshots[0].Density.Centroid().X * 1e3;
        var held = outcome.Snapshots[1].Density.Centroid().X * 1e3;
        var pushed = outcome.Snapshots[2].Density.Centroid().X * 1e3;

        // Nothing carries the packet during the hold: that phase sets the field to zero and
        // diffusion is symmetric, so the centroid stays where it was seeded.
        Assert.Equal(8.0, seeded, 3);
        Assert.True(
            Math.Abs(held - seeded) < 1e-3,
            $"the held packet moved from {seeded:F6} mm to {held:F6} mm");

        // Then mu E for forty microseconds: 2e-4 m^2/(V s) at 20 kV/m is 4 m/s, so 0.16 mm.
        // THE DISCRIMINATOR. The field is an expression over a parameter whose base value is
        // zero, so a run that ignores the timeline never switches it on and this packet sits
        // at 8 mm for the whole eighty microseconds.
        Assert.Equal(8.16, pushed, 2);
    }

    [Fact]
    public void AnInstantInsideATrajectoryPhaseYieldsNoDensity()
    {
        // A trajectory phase has trajectories and not a density, so there is nothing to
        // hand back. Filling the gap with the nearest density would draw the packet
        // somewhere it never was, so the shortfall is left for the caller to see - which
        // is what the animation's own refusal is written against.
        var outcome = Run(SequencedRunTests.Model, 0.5e-6, 5e-6, 25e-6);

        Assert.All(
            outcome.Snapshots,
            shot => Assert.True(
                shot.RequestedSeconds >= 1e-6,
                "only the diffusive phase, which begins at 1 us, can serve a snapshot"));

        Assert.DoesNotContain(outcome.Snapshots, shot => shot.RequestedSeconds == 25e-6);
    }

    [Fact]
    public void RenderingASequencedModelFollowsItsTimeline()
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "hold-then-push.json");
        File.WriteAllText(path, HoldThenPush);

        var (exit, stdout, _) = Cli("render", "section", path, "--json");

        Assert.Equal(0, exit);

        // The stamped page, not a JSON field: GRD-12 puts the provenance on the
        // artifact, because a figure is the thing most likely to be shown with none
        // of the apparatus it came from attached.
        var page = File.ReadAllText(
            JsonDocument.Parse(stdout).RootElement.GetProperty("artifacts")[0].GetString()!);

        // THE TEETH. Route this back through `DiffusionRun.Execute` - which is what both
        // render arms did - and this line is absent, because that path never looks at a
        // sequence. Everything else about the figure is identical either way, which is
        // exactly why the defect survived: the drawing is correct, of the wrong run.
        Assert.Contains("sequenced run, 2 phases", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredTrajectoryModelWithADiffusivePhaseStillDrawsItsDensity()
    {
        // The gate used to be the model's DECLARED mode, so this model - trajectory by
        // declaration, with a diffusive phase in the middle - had a density and was drawn
        // with none, silently. Asking for the set of modes the run USES is the same
        // correction the run command's own fork needed.
        //
        // Asked at an instant inside that phase, because this sequence ENDS as trajectories:
        // there is no final density, and the figure says so rather than drawing an empty box.
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "trap-then-extract.json");
        File.WriteAllText(path, SequencedRunTests.Model);

        var (exit, stdout, _) = Cli("render", "section", path, "--at-us", "10", "--json");

        Assert.Equal(0, exit);

        var root = JsonDocument.Parse(stdout).RootElement;

        Assert.True(
            root.GetProperty("paths").TryGetProperty("density", out var contours)
                && contours.GetInt32() > 0,
            "the diffusive phase's density must be contoured");

        Assert.Contains(
            "sequenced run, 3 phases",
            File.ReadAllText(root.GetProperty("artifacts")[0].GetString()!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ARenderReportsWhereItsTransportHasGotTo()
    {
        // Correcting the routing made a render cost what the run costs, because it IS the
        // run - and the render verbs passed no observer, so the 8 ms elution film was twenty
        // minutes of silence and the 128 ms ramp would be most of a working day of it. That
        // is the gap `--progress` was built to close, one verb over.
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "hold-then-push.json");
        File.WriteAllText(path, HoldThenPush);

        var (exit, stdout, stderr) = Cli(
            "render", "section", path, "--json", "--progress", "0.0001");

        Assert.Equal(0, exit);

        // One line per phase, naming which of how many - the sequenced run's own structure,
        // which is what a reader watching a multi-hour render wants first.
        Assert.Contains("phase 1/2", stderr, StringComparison.Ordinal);
        Assert.Contains("phase 2/2", stderr, StringComparison.Ordinal);

        // CLI-2. A caller piping --json gets the result document and nothing else, however
        // long the transport behind the figure took to produce it.
        Assert.Contains("section", JsonDocument.Parse(stdout).RootElement
            .GetProperty("kind").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void ARenderLeavesNoCheckpoint()
    {
        // THE DESIGN DECISION, ASSERTED. A checkpoint says in its own words that the answer
        // will appear beside it when the run finishes and that finding the file means it did
        // not - and a render writes no result at all, so that document would be false on both
        // counts. `einzel report` reads exactly those files, so one left here would have it
        // describe a finished render as an interrupted run.
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "hold-then-push.json");
        File.WriteAllText(path, HoldThenPush);

        Assert.Equal(0, Cli("render", "section", path, "--progress", "0.0001").ExitCode);

        Assert.Empty(Directory.GetFiles(_root, "*.progress.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void ProgressIsRefusedRatherThanIgnoredWhenItIsNotANumber()
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "hold-then-push.json");
        File.WriteAllText(path, HoldThenPush);

        var (exit, _, stderr) = Cli("render", "section", path, "--progress", "soon");

        Assert.NotEqual(0, exit);
        Assert.Contains("--progress takes an interval in seconds", stderr, StringComparison.Ordinal);
    }
}
