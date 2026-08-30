using Einzel.Core.Errors;
using Einzel.Render;

using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// The declared, explicitly non-linear mapping from flight time to playback time.
/// </summary>
/// <remarks>
/// <para>
/// RND-7 is unusually emphatic: an animation "declares an explicit non-linear time
/// mapping — playback rate per sequence phase — and the current rate is displayed on
/// screen throughout playback. Neither part is optional. This is the animation
/// equivalent of GRD-1: the artifact may compress, but it may not hide that it
/// compressed."
/// </para>
/// <para>
/// So what is tested here is arithmetic and refusals in equal measure. The arithmetic
/// is exact — a rate is a division and a frame time is a multiplication, with nothing
/// modelled — so anything worse than a part in 10^12 is a defect rather than a
/// tolerance.
/// </para>
/// </remarks>
public sealed class TimeMappingTests(ITestOutputHelper output)
{
    private const double Micro = 1e-6;

    /// <summary>One phase is the linear case, and it still has to declare its rate.</summary>
    /// <remarks>
    /// 10 µs of flight at 1 µs per second of playback is ten seconds on screen, which
    /// at 30 frames a second is 301 frames counting both ends. Every number here is
    /// arithmetic the code has no part in.
    /// </remarks>
    [Fact]
    public void OnePhaseIsLinearAndExact()
    {
        var spec = new AnimationSpec
        {
            FramesPerSecond = 30,
            Phases = [new AnimationPhase(10.0 * Micro, 1.0 * Micro, "drift")],
        };

        Assert.Equal(10.0, TimeMapping.PlaybackSeconds(spec), 1e-12);

        var frames = TimeMapping.Frames(spec);

        output.WriteLine($"{frames.Count} frames over {TimeMapping.PlaybackSeconds(spec):F3} s");

        Assert.Equal(301, frames.Count);
        Assert.Equal(0.0, frames[0].SimulatedSeconds, 1e-18);
        Assert.Equal(10.0 * Micro, frames[^1].SimulatedSeconds, 1e-18);

        // Frame k is at k/30 seconds of playback and so at k/30 microseconds of
        // flight, because the rate is one microsecond per second.
        foreach (var frame in frames)
        {
            Assert.Equal(frame.Index / 30.0, frame.PlaybackSeconds, 1e-12);
            Assert.Equal(frame.Index / 30.0 * Micro, frame.SimulatedSeconds, 1e-18);
            Assert.Equal("drift", frame.PhaseLabel);
        }
    }

    /// <summary>
    /// Two phases spanning a hundredfold in rate land exactly on their boundary.
    /// </summary>
    /// <remarks>
    /// The case the requirement exists for: an ion turning round in a mirror takes a
    /// microsecond and the drift after it takes a hundred, and one rate cannot show
    /// both. Slow for the turn-around, fast for the drift, and the two stretches take
    /// two seconds each — so the boundary is at playback 2.000 s and the frame there
    /// shows exactly 1 µs of flight.
    /// </remarks>
    [Fact]
    public void TwoPhasesMeetExactlyAtTheirBoundary()
    {
        var spec = new AnimationSpec
        {
            FramesPerSecond = 30,
            Phases =
            [
                new AnimationPhase(1.0 * Micro, 0.5 * Micro, "turn-around"),
                new AnimationPhase(101.0 * Micro, 50.0 * Micro, "drift"),
            ],
        };

        Assert.Equal(4.0, TimeMapping.PlaybackSeconds(spec), 1e-12);

        var frames = TimeMapping.Frames(spec);
        var boundary = frames.Single(f => Math.Abs(f.PlaybackSeconds - 2.0) < 1e-12);

        output.WriteLine(
            $"boundary frame {boundary.Index} at {boundary.PlaybackSeconds:F4} s shows "
            + $"{boundary.SimulatedSeconds / Micro:F6} us");

        Assert.Equal(1.0 * Micro, boundary.SimulatedSeconds, 1e-18);

        // A frame landing exactly on a boundary announces the INCOMING rate: it shows
        // the boundary instant and is followed by a frame's worth of playback at the
        // new speed, so naming the rate that has just stopped applying would be
        // announcing the wrong one.
        Assert.Equal("drift", boundary.PhaseLabel);
        Assert.Equal(50.0 * Micro, boundary.RateSiPerPlaybackSecond, 1e-18);

        // And the slow half really is a hundred times slower, which is the whole point.
        Assert.Equal(0.5 * Micro, frames[0].RateSiPerPlaybackSecond, 1e-18);
        Assert.Equal(101.0 * Micro, frames[^1].SimulatedSeconds, 1e-16);
    }

    /// <summary>
    /// A playback duration that is not a whole number of frames still ends at the end.
    /// </summary>
    /// <remarks>
    /// The last instant of a flight is precisely the one a reader wants — the arrival,
    /// the ejection, the packet reaching the detector — so the grid is extended by one
    /// frame rather than stopping short of it. That extra frame is not a duplicate: it
    /// is added only when the last gridded frame is strictly earlier than the end.
    /// </remarks>
    [Fact]
    public void ThePlaybackEndsOnTheLastInstant()
    {
        // 1.05 s of playback at 30 fps: 32 gridded frames (0 to 31/30 = 1.0333 s),
        // then the end.
        var spec = new AnimationSpec
        {
            FramesPerSecond = 30,
            Phases = [new AnimationPhase(1.05 * Micro, 1.0 * Micro)],
        };

        var frames = TimeMapping.Frames(spec);

        output.WriteLine(
            $"{frames.Count} frames, last two at {frames[^2].PlaybackSeconds:F6} and "
            + $"{frames[^1].PlaybackSeconds:F6} s");

        Assert.Equal(33, frames.Count);
        Assert.Equal(31.0 / 30.0, frames[^2].PlaybackSeconds, 1e-12);
        Assert.Equal(1.05, frames[^1].PlaybackSeconds, 1e-12);
        Assert.Equal(1.05 * Micro, frames[^1].SimulatedSeconds, 1e-18);

        // Indices stay contiguous across the forced frame.
        for (var k = 0; k < frames.Count; k++)
        {
            Assert.Equal(k, frames[k].Index);
        }
    }

    /// <summary>
    /// Six phases do not accumulate error, because frame times are never accumulated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check that says the mapping is computed rather than stepped. Each phase here
    /// has a playback duration that is deliberately not a whole number of frames, so a
    /// version that advanced frame by frame would carry a fractional frame of error out
    /// of each one and be visibly adrift by the sixth.
    /// </para>
    /// <para>
    /// What is asserted is that the last frame lands on the last declared instant to a
    /// part in 10^12, and that every boundary lands on its own declared instant. Both
    /// are exact statements about a mapping made of one division and one multiplication.
    /// </para>
    /// </remarks>
    [Fact]
    public void ManyPhasesDoNotDrift()
    {
        var ends = new[] { 0.37, 1.13, 2.71, 5.29, 9.07, 14.63 };
        var rates = new[] { 0.017, 0.093, 0.31, 0.77, 1.9, 4.4 };

        var spec = new AnimationSpec
        {
            FramesPerSecond = 25,
            Phases = [.. ends.Zip(rates, (e, r) => new AnimationPhase(e * Micro, r * Micro))],
        };

        var frames = TimeMapping.Frames(spec);

        output.WriteLine($"{frames.Count} frames over "
            + $"{TimeMapping.PlaybackSeconds(spec):F4} s of playback");

        Assert.Equal(14.63 * Micro, frames[^1].SimulatedSeconds, 1e-12 * 14.63 * Micro);

        // Every declared instant is reached, and the frame that reaches it is the one
        // whose playback time is the cumulative boundary. Checked against the boundary
        // computed here, from the declared numbers, rather than against anything the
        // mapping returned.
        var playback = 0.0;
        var previous = 0.0;

        for (var i = 0; i < ends.Length; i++)
        {
            playback += (ends[i] - previous) * Micro / (rates[i] * Micro);
            previous = ends[i];

            const int Fine = 1_000_000;

            var at = TimeMapping.Frames(spec with { FramesPerSecond = Fine })
                .OrderBy(f => Math.Abs(f.PlaybackSeconds - playback))
                .First();

            // Within one frame's worth of simulated time, which is what the nearest
            // frame to a boundary can be and no better: the grid does not land on
            // boundaries. That is still discriminating by orders of magnitude - an
            // implementation that stepped frame to frame would be adrift by a
            // fractional frame per phase, accumulating, and by the sixth phase would
            // miss by many frames rather than by less than one.
            Assert.Equal(ends[i] * Micro, at.SimulatedSeconds, 2.0 * rates[i] * Micro / Fine);
        }

        // Strictly increasing in both times, which a mapping made of positive rates
        // must be and a stepped one can fail to be at a boundary.
        for (var k = 1; k < frames.Count; k++)
        {
            Assert.True(
                frames[k].SimulatedSeconds > frames[k - 1].SimulatedSeconds,
                $"frame {k} does not advance: {frames[k].SimulatedSeconds:E17} after "
                + $"{frames[k - 1].SimulatedSeconds:E17}");
        }
    }

    /// <summary>An animation with no declared mapping is refused, not defaulted.</summary>
    /// <remarks>
    /// The half of RND-7 that is a refusal. A default rate is exactly the hidden
    /// compression the requirement exists to prevent, and it would be worse than an
    /// arbitrary one because it would look like a choice somebody made.
    /// </remarks>
    [Fact]
    public void AnAnimationWithNoPhasesIsRefused()
    {
        var failure = Assert.Throws<EinzelException>(
            () => TimeMapping.Frames(new AnimationSpec()));

        output.WriteLine(failure.Error.Constraint);

        Assert.Equal("/animation/phases", failure.Error.Path);
        Assert.Contains("RND-7", failure.Error.Suggestion!, StringComparison.Ordinal);
    }

    /// <summary>A phase that does not advance, and a rate that is not one.</summary>
    [Theory]
    [InlineData(0.0, 1.0, "/animation/phases/0/until")]
    [InlineData(-1.0, 1.0, "/animation/phases/0/until")]
    [InlineData(1.0, 0.0, "/animation/phases/0/rate")]
    [InlineData(1.0, -1.0, "/animation/phases/0/rate")]
    [InlineData(1.0, double.PositiveInfinity, "/animation/phases/0/rate")]
    public void ADegeneratePhaseIsRefused(double until, double rate, string path)
    {
        var failure = Assert.Throws<EinzelException>(() => TimeMapping.Frames(new AnimationSpec
        {
            Phases = [new AnimationPhase(until * Micro, rate * Micro)],
        }));

        Assert.Equal(path, failure.Error.Path);
    }

    /// <summary>A frame rate that is not a rate.</summary>
    [Fact]
    public void ANonPositiveFrameRateIsRefused()
    {
        var failure = Assert.Throws<EinzelException>(() => TimeMapping.Frames(new AnimationSpec
        {
            FramesPerSecond = 0,
            Phases = [new AnimationPhase(Micro, Micro)],
        }));

        Assert.Equal("/animation/framesPerSecond", failure.Error.Path);
    }

    /// <summary>The stamp says the rate twice, in the two ways a reader needs it.</summary>
    /// <remarks>
    /// The time-per-second reading is what converts anything on screen back into flight
    /// time and carries a unit, which is what GRD-1 asks of every quantity here. The
    /// slow-down factor is the intuition, and on its own says nothing about how long the
    /// flight is. The unit is chosen from the magnitude because one animation may span
    /// nanoseconds of turn-around and milliseconds of trapping.
    /// </remarks>
    [Theory]
    [InlineData(1e-6, "µs")]
    [InlineData(2.5e-9, "ns")]
    [InlineData(4e-13, "ps")]
    [InlineData(0.02, "ms")]
    [InlineData(3.0, "s")]
    public void TheStampCarriesBothReadings(double rate, string unit)
    {
        var text = TimeMapping.Describe(rate);

        output.WriteLine($"{rate:E2} -> {text}");

        Assert.Contains(unit + " of flight per second of playback", text, StringComparison.Ordinal);
        Assert.Contains("slower than real time", text, StringComparison.Ordinal);
    }
}
