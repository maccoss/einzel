using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A run measured in minutes can be watched while it happens (§16, SPEC item 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam existed and nothing crossed it.</b> <c>IDensityProgress</c> has carried the
/// solver's live density buffers since the checkpoint writer was built, and the only
/// consumer turned them into a centroid, a width and a line of prose. So a person watching
/// a TIMS elution could read that the packet had reached 21.1 mm and could not see it - on
/// the one class of model where the whole difficulty is understanding what the instrument is
/// doing to a packet over twenty minutes.
/// </para>
/// <para>
/// <b>What is asserted here is the wiring rather than the physics.</b> The contouring, the
/// revolution and the orientation are the same functions a finished run draws with, tested
/// where they live; this file is about a frame being a complete bundle, arriving in order,
/// costing what it claims to cost, and - the one that could fail silently - not changing the
/// answer.
/// </para>
/// </remarks>
public sealed class WatchedRunTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-watched-run", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Model(string name, string body)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, $"{name}.json");

        File.WriteAllText(path, body);

        return path;
    }

    /// <summary>Collects frames, asking for one every so many steps.</summary>
    /// <remarks>
    /// A step count rather than a wall clock, because a test that reports on the clock
    /// reports a different number of times on a loaded machine and on an idle one - which is
    /// exactly the non-determinism the shell wants and a test cannot assert against.
    /// </remarks>
    private sealed class Collector(int everySteps) : ViewportCommand.IViewportProgress
    {
        public List<ViewportOutcome> Frames { get; } = [];

        public int Asked { get; private set; }

        public bool Wants(int steps, double timeSeconds)
        {
            Asked++;

            return everySteps > 0 && steps % everySteps == 0;
        }

        public void Reached(ViewportOutcome frame) => Frames.Add(frame);
    }

    /// <summary>A packet held still and then pushed, entirely in the diffusive description.</summary>
    /// <remarks>
    /// <para>
    /// <b>Sized so that the run has steps to watch</b>, which the routing fixture this is
    /// modelled on deliberately is not. There the hold is one step and the push is one, so a
    /// watch would report twice and a test of ordering would have nothing to order. Here the
    /// push runs at a field whose Courant limit is a fraction of a micrometre-scale cell, so
    /// the second phase is about a hundred steps while the first is still one.
    /// </para>
    /// <para>
    /// <b>The asymmetry is the point rather than an accident.</b> The hold has no field at
    /// all, so its step is diffusion-limited and enormous; the push is drift-limited and
    /// short. A sequenced run whose two phases differ hundredfold in step is exactly the
    /// shape a TIMS hold-and-ramp has, and it is what makes the phase boundary a place where
    /// a clock can be got wrong.
    /// </para>
    /// </remarks>
    private const string HoldThenPush = """
    {
      "schemaVersion": "0.13",
      "name": "hold-then-push",
      "parameters": {
        "push": { "value": 0, "unit": "V/m", "minimum": 0, "maximum": 2000000,
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
          "set": { "push": { "value": 1000000, "unit": "V/m" } } }
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
          "intervalsX": 512, "intervalsY": 64
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

    /// <summary>Every frame is a bundle a viewport could draw on its own.</summary>
    /// <remarks>
    /// <para>
    /// <b>A frame is a whole <see cref="ViewportOutcome"/> rather than a density</b>, because
    /// UI-1 puts the contouring on this side of the line. So the test of a frame is the test
    /// of a bundle: shells whose triangles index vertices that exist, the geometry that
    /// surrounds them, and the warnings that say what is being looked at.
    /// </para>
    /// <para>
    /// The instant is asserted to be <em>on</em> the frame, not merely somewhere in the run,
    /// because GRD-12 is the whole reason it is there: three shells of a packet say nothing
    /// without it, since a density that has spread for a microsecond and one that has spread
    /// for a millisecond are the same three shells at different sizes.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFrameIsABundleAViewportCouldDrawOnItsOwn()
    {
        var watcher = new Collector(everySteps: 10);
        var final = ViewportCommand.Watch(Model("hold-then-push", HoldThenPush), watcher);

        output.WriteLine($"{watcher.Frames.Count} frames of {watcher.Asked} steps asked");

        Assert.NotEmpty(watcher.Frames);

        foreach (var frame in watcher.Frames)
        {
            // RND-8: a density model has no paths, and the frame says so rather than
            // leaving an emptiness to be read as an instrument that lost everything.
            Assert.False(frame.ProducesTrajectories);
            Assert.Empty(frame.Trajectories);
            Assert.Contains(frame.Warnings, w => w.Code == "render.no-trajectories");

            // GRD-12: which instant this is, on the picture.
            Assert.NotNull(frame.DensityAtUs);
            Assert.Contains(frame.Warnings, w => w.Code == "render.density-at-instant");

            Assert.Equal(final.Conductors.Count, frame.Conductors.Count);

            foreach (var shell in frame.Density)
            {
                Assert.Equal(0, shell.VerticesMm.Count % 3);
                Assert.Equal(shell.VerticesMm.Count, shell.Normals.Count);
                Assert.Equal(0, shell.Triangles.Count % 3);
                Assert.All(
                    shell.Triangles, i => Assert.InRange(i, 0, (shell.VerticesMm.Count / 3) - 1));
            }
        }

        var drawn = watcher.Frames.Count(f => f.Density.Count > 0);

        output.WriteLine($"{drawn} of {watcher.Frames.Count} frames hold a packet worth drawing");

        // Not merely "a frame arrived": a run whose every frame was empty would satisfy
        // everything above and would show a person an empty box for twenty minutes.
        Assert.True(drawn > 0, "no frame of the run held a density worth drawing");
    }

    /// <summary>The frames advance along the run's own clock, in order.</summary>
    /// <remarks>
    /// The instants must be strictly increasing <b>and</b> must reach past the first phase,
    /// because a watch that reported the same instant forever and one that stopped at the
    /// first phase boundary both look like a working watch on any single frame.
    /// </remarks>
    [Fact]
    public void TheFramesAdvanceAlongTheRunsOwnClockAndCrossThePhaseBoundary()
    {
        var watcher = new Collector(everySteps: 10);

        ViewportCommand.Watch(Model("hold-then-push", HoldThenPush), watcher);

        var instants = watcher.Frames.Select(f => f.DensityAtUs!.Value).ToList();

        output.WriteLine(
            $"first {instants[0]:F3} us, last {instants[^1]:F3} us, {instants.Count} frames");

        for (var i = 1; i < instants.Count; i++)
        {
            Assert.True(
                instants[i] > instants[i - 1],
                $"frame {i} is at {instants[i]} us, not after frame {i - 1} at {instants[i - 1]}");
        }

        // THE FIRST FRAME, NOT THE LAST, AND THAT IS WHAT DISCRIMINATES. The hold is one
        // step, so every frame here falls in the second phase - and the transport reports
        // its own leg's clock, which starts again at zero. A frame carrying that would
        // begin near 4 us and climb to 40, monotone and entirely wrong, and asserting only
        // on the last instant would not see it. On the run's clock the first frame is past
        // the 40 us the hold ends at.
        Assert.True(
            instants[0] > 40.0,
            $"the first frame is at {instants[0]:F3} us, which is inside the 40 us hold that "
            + "had already finished - the leg's clock has been reported as the run's");

        Assert.True(
            instants[^1] > 40.0,
            $"the watch stopped at {instants[^1]:F3} us, inside the first of two 40 us phases");
    }

    /// <summary>Watching does not change what the run computes.</summary>
    /// <remarks>
    /// <para>
    /// <b>The claim that could fail silently.</b> A frame is built from the solver's own live
    /// buffer rather than a copy - which is what makes watching affordable at all - so
    /// anything written back would change the answer, and would change it in a way that
    /// looks like physics rather than like a bug.
    /// </para>
    /// <para>
    /// Compared vertex for vertex rather than by shell count or peak: a scheme perturbed at
    /// one node still produces the same number of shells at very nearly the same levels, and
    /// the discrepancy would be exactly the sort of small number this project has learned to
    /// explain away.
    /// </para>
    /// </remarks>
    [Fact]
    public void WatchingDoesNotChangeWhatTheRunComputes()
    {
        var path = Model("hold-then-push", HoldThenPush);

        var watched = ViewportCommand.Watch(path, new Collector(everySteps: 1));
        var unwatched = ViewportCommand.Watch(path, new Collector(everySteps: 0));

        Assert.Equal(unwatched.PeakDensityPerCubicMetre, watched.PeakDensityPerCubicMetre);
        Assert.Equal(unwatched.DensityAtUs, watched.DensityAtUs);
        Assert.Equal(unwatched.Density.Count, watched.Density.Count);

        output.WriteLine(
            $"peak {watched.PeakDensityPerCubicMetre:G17} watched, "
            + $"{unwatched.PeakDensityPerCubicMetre:G17} not");

        for (var k = 0; k < unwatched.Density.Count; k++)
        {
            var a = unwatched.Density[k];
            var b = watched.Density[k];

            Assert.Equal(a.DensityPerCubicMetre, b.DensityPerCubicMetre);
            Assert.Equal(a.VerticesMm.Count, b.VerticesMm.Count);

            for (var i = 0; i < a.VerticesMm.Count; i++)
            {
                Assert.Equal(a.VerticesMm[i], b.VerticesMm[i]);
            }
        }
    }

    /// <summary>The geometry is extracted once and every frame shares it.</summary>
    /// <remarks>
    /// <para>
    /// The cost claim, made checkable. Surface extraction over every conductor is the
    /// dominant cost of building a bundle - the shipped C-trap is 795,564 triangles - and
    /// doing it per frame would make watching cost more than the run.
    /// </para>
    /// <para>
    /// It is sound because the sequencer refuses a stage that moves an electrode: a stage may
    /// change what one holds, not where it is. <b>Reference equality rather than a
    /// comparison</b>, because two lists that happen to be equal would also pass while the
    /// work was being done again.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGeometryIsExtractedOnceAndEveryFrameSharesIt()
    {
        var watcher = new Collector(everySteps: 10);
        var final = ViewportCommand.Watch(Model("hold-then-push", HoldThenPush), watcher);

        Assert.NotEmpty(watcher.Frames);

        foreach (var frame in watcher.Frames)
        {
            Assert.Same(final.Conductors, frame.Conductors);
            Assert.Same(final.Equipotentials, frame.Equipotentials);
            Assert.Same(final.Ends, frame.Ends);
        }
    }

    /// <summary>A frame's warnings are its own rather than every earlier frame's.</summary>
    /// <remarks>
    /// One shared list would grow without bound over a run of hundreds of thousands of steps
    /// <b>and</b> would put every earlier frame's instant on the current frame's picture -
    /// so a viewer reading the provenance would find a dozen instants and no way to tell
    /// which one they were looking at.
    /// </remarks>
    [Fact]
    public void AFrameCarriesItsOwnInstantAndNotEveryEarlierOne()
    {
        var watcher = new Collector(everySteps: 10);

        ViewportCommand.Watch(Model("hold-then-push", HoldThenPush), watcher);

        Assert.True(watcher.Frames.Count > 1, "one frame cannot show accumulation");

        foreach (var frame in watcher.Frames)
        {
            Assert.Equal(1, frame.Warnings.Count(w => w.Code == "render.density-at-instant"));
        }

        // And the list does not grow: the last frame carries no more than the first.
        Assert.Equal(watcher.Frames[0].Warnings.Count, watcher.Frames[^1].Warnings.Count);
    }

    /// <summary>A trajectory model is refused, with the thing to do instead.</summary>
    /// <remarks>
    /// Not because it could not be watched, but because an ion flight finishes faster than a
    /// window could draw it part way through - so a watch would be a slower route to the
    /// answer the ordinary viewport already gives at once. AGT-3: the refusal names what to
    /// do rather than only what went wrong.
    /// </remarks>
    [Fact]
    public void ATrajectoryModelIsRefusedWithTheThingToDoInstead()
    {
        const string Flight = """
        {
          "schemaVersion": "0.3",
          "name": "free-flight",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 4000, "unit": "V" }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [100, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "maximumFlightTime": { "value": 100, "unit": "us" }
          }
        }
        """;

        var refusal = Assert.Throws<Core.Errors.EinzelException>(
            () => ViewportCommand.Watch(Model("free-flight", Flight), new Collector(1)));

        output.WriteLine(refusal.Error.Constraint);
        output.WriteLine(refusal.Error.Suggestion);

        Assert.Equal(Core.Errors.ErrorCodes.RegimeInvalid, refusal.Error.Code);
        Assert.Contains("trajectories", refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("viewport", refusal.Error.Suggestion!, StringComparison.Ordinal);
    }
}
