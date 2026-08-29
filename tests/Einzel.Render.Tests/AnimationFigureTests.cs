using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Library;
using Einzel.Render;

using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// A flight drawn as a sequence of vector frames, headlessly.
/// </summary>
/// <remarks>
/// <para>
/// RND-7 asks for two things and calls neither optional: an explicit non-linear time
/// mapping, and the current rate displayed on screen throughout playback. The mapping
/// is tested in <see cref="TimeMappingTests"/>; here what is tested is that the rate
/// reaches every frame and that the frames are frames of the same instrument.
/// </para>
/// <para>
/// RND-1 applies as much as it does to a section: these run on Linux in CI with no
/// display attached, because a renderer that needs a window is a shell feature
/// wherever its code lives.
/// </para>
/// </remarks>
public sealed class AnimationFigureTests(ITestOutputHelper output)
{
    private const double Micro = 1e-6;

    private static CompiledModel Compile(string template)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read(template));
        var validation = ModelValidator.Validate(document, null);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    /// <summary>Two rates over one flight, with a slow stretch through the middle.</summary>
    private static AnimationSpec Mapping(double flightUs) => new()
    {
        FramesPerSecond = 12,
        Phases =
        [
            new AnimationPhase(0.4 * flightUs * Micro, 4.0 * Micro, "approach"),
            new AnimationPhase(0.6 * flightUs * Micro, 0.4 * Micro, "through the lens"),
            new AnimationPhase(flightUs * Micro, 4.0 * Micro, "away"),
        ],
    };

    /// <summary>
    /// Every frame is a frame of the same instrument, on the same page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The regression this exists for.</b> The first version handed each frame only
    /// the part of the flight drawn so far. An analytic model takes its extent from the
    /// flight, so every frame then chose its page from its own prefix - the scale
    /// changed frame to frame and the ion sat pinned to the edge of a box that grew to
    /// meet it. It reads as a camera following the ion rather than as an instrument
    /// being flown through, and nothing about a single frame reveals it.
    /// </para>
    /// <para>
    /// So the whole flight is handed over every time and an instant truncates it for
    /// drawing. What that buys is asserted directly: one page, one scale, all frames.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFrameIsDrawnOnTheSamePage()
    {
        var frames = AnimationRenderer.Render(
            AnalyticModels.Reflectron(), new RenderSpec { Equipotentials = 6 }, Mapping(51.0));

        output.WriteLine($"{frames.Count} frames");

        Assert.True(frames.Count > 20, $"only {frames.Count} frames");

        var width = frames[0].Figure.Scene.WidthMm;
        var height = frames[0].Figure.Scene.HeightMm;

        foreach (var frame in frames)
        {
            Assert.Equal(width, frame.Figure.Scene.WidthMm, 1e-12);
            Assert.Equal(height, frame.Figure.Scene.HeightMm, 1e-12);
        }

        output.WriteLine($"page {width:F1} by {height:F1} mm on all of them");
    }

    /// <summary>
    /// Every frame carries the rate, and the rate is the one in force there.
    /// </summary>
    /// <remarks>
    /// The half of RND-7 that is not about the mapping: "the current rate is displayed
    /// on screen throughout playback". Not on the first frame, not in the manifest
    /// beside them - on every frame, because a frame is the artifact most likely to be
    /// shown with none of its apparatus attached.
    /// </remarks>
    [Fact]
    public void EveryFrameCarriesTheRateInForceOnIt()
    {
        var frames = AnimationRenderer.Render(
            Compile("einzel-lens"), new RenderSpec { Equipotentials = 4 }, Mapping(20.0));

        var slow = 0;

        foreach (var frame in frames)
        {
            var stamp = frame.Figure.Scene.Texts.SingleOrDefault(t => t.Layer == "timebase");

            Assert.NotNull(stamp);
            Assert.Contains("per second of playback", stamp.Text, StringComparison.Ordinal);
            Assert.Contains("slower than real time", stamp.Text, StringComparison.Ordinal);

            // And it is this frame's rate rather than a single one stamped throughout,
            // which is the failure that would look identical on any one frame.
            if (frame.Frame.PhaseLabel == "through the lens")
            {
                Assert.Contains("400 ns of flight", stamp.Text, StringComparison.Ordinal);
                slow++;
            }
            else
            {
                Assert.Contains("4 µs of flight", stamp.Text, StringComparison.Ordinal);
            }
        }

        output.WriteLine($"{slow} of {frames.Count} frames are in the slow stretch");

        // The slow stretch is a fifth of the flight and most of the playback, which is
        // the whole reason RND-7 exists: one rate cannot show both.
        Assert.True(slow > frames.Count / 2, $"only {slow} of {frames.Count} frames were slow");
    }

    /// <summary>The drawn flight grows, and the marker is at its head.</summary>
    /// <remarks>
    /// A frame of an animation shows where the ion <em>is</em>. A polyline that grows
    /// says only where it has been, so the head is marked - and the marker has to sit on
    /// the end of the line rather than at the last recorded sample, or it would stutter
    /// at whatever cadence the adaptive integrator happened to keep points, which is
    /// fastest exactly where the physics is hardest.
    /// </remarks>
    [Fact]
    public void TheFlightGrowsAndTheIonIsMarkedAtItsHead()
    {
        var frames = AnimationRenderer.Render(
            AnalyticModels.Reflectron(), new RenderSpec { Equipotentials = 4 }, Mapping(51.0));

        var heads = new List<double>(frames.Count);

        foreach (var frame in frames)
        {
            var marker = frame.Figure.Scene.Paths.SingleOrDefault(p => p.Layer == "ion");

            Assert.NotNull(marker);
            Assert.True(marker.Closed, "the marker is a closed polygon");

            heads.Add(marker.Points.Average(p => p.X));
        }

        var first = heads[0];
        var furthest = heads.Max();
        var last = heads[^1];

        output.WriteLine(
            $"the ion goes from {first:F2} out to {furthest:F2} and back to {last:F2} mm");

        // A reflectron turns round, so the head is not monotone across the page - it
        // goes out and comes back, which is the thing worth asserting. Half the page
        // each way, and back to where it started, because this model's detector is at
        // its source.
        Assert.True(furthest - first > 50.0, $"the ion only reached {furthest:F2} mm");
        Assert.Equal(first, last, 2.0);

        // And it does turn round exactly once: the head advances, then retreats, with
        // no second reversal. A drawing that jumped back to the launch each frame would
        // pass the two assertions above and fail this one.
        var reversals = 0;

        for (var k = 1; k < heads.Count - 1; k++)
        {
            var before = heads[k] - heads[k - 1];
            var after = heads[k + 1] - heads[k];

            if (before > 1e-9 && after < -1e-9)
            {
                reversals++;
            }
        }

        Assert.Equal(1, reversals);
    }

    /// <summary>A model with no trajectories is refused rather than filmed.</summary>
    /// <remarks>
    /// RND-8 forbids drawing lines through a diffusive region, and a run reports the
    /// density it ended with rather than one per instant - so the frames would all be
    /// the same box and the film would show motion that was never computed. That is
    /// worse than no film: it looks like one.
    /// </remarks>
    [Fact]
    public void ADiffusiveModelIsRefused()
    {
        var lens = Compile("einzel-lens");
        var diffusive = lens with { TransportMode = "diffusion" };

        var failure = Assert.Throws<EinzelException>(() => AnimationRenderer.Render(
            diffusive, new RenderSpec(), Mapping(20.0)));

        output.WriteLine(failure.Error.Constraint);

        Assert.Equal("/transport/mode", failure.Error.Path);
        Assert.Contains("RND-8", failure.Error.Suggestion!, StringComparison.Ordinal);
    }

    /// <summary>An animation that draws no ion is refused, for the same reason.</summary>
    /// <remarks>
    /// The geometry and the field are identical on every frame - a moving field would
    /// need a solve per stage and is not built - so with the trajectory switched off the
    /// sequence is one drawing repeated. A film of nothing that looks like a film of
    /// something is worse than no film.
    /// </remarks>
    [Fact]
    public void AnAnimationWithNoTrajectoryIsRefused()
    {
        var failure = Assert.Throws<EinzelException>(() => AnimationRenderer.Render(
            AnalyticModels.Reflectron(),
            new RenderSpec { Trajectory = false },
            Mapping(51.0)));

        output.WriteLine(failure.Error.Constraint);

        Assert.Equal("/trajectory", failure.Error.Path);
    }

    /// <summary>An animation with no declared mapping never gets as far as drawing.</summary>
    [Fact]
    public void AnUndeclaredMappingIsRefusedBeforeAnythingIsDrawn()
    {
        var failure = Assert.Throws<EinzelException>(() => AnimationRenderer.Render(
            Compile("einzel-lens"), new RenderSpec(), new AnimationSpec()));

        Assert.Equal("/animation/phases", failure.Error.Path);
    }
}
