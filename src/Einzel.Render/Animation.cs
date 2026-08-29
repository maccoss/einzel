using System.Globalization;

using Einzel.Core.Errors;

namespace Einzel.Render;

/// <summary>
/// One stretch of simulated time, played back at a declared rate.
/// </summary>
/// <param name="UntilSeconds">
/// Simulated time this phase runs to, in seconds. Phases are cumulative, so the
/// first runs from the animation's start and each later one from its predecessor.
/// </param>
/// <param name="RateSiPerPlaybackSecond">
/// How much simulated time passes per second of playback, in seconds per second.
/// 1e-6 is one microsecond of flight per second on screen.
/// </param>
/// <param name="Label">
/// What this stretch is, shown beside the rate. A sequenced instrument's stage names
/// are the obvious thing to put here.
/// </param>
/// <remarks>
/// Declared in simulated time rather than playback time because that is the axis the
/// physics is defined on: a sequencer's stages, a mirror's turn-around, an extraction
/// pulse all have durations in seconds of flight. Playback duration is then derived -
/// span over rate - rather than being a second thing to keep consistent with the
/// first.
/// </remarks>
public sealed record AnimationPhase(
    double UntilSeconds,
    double RateSiPerPlaybackSecond,
    string? Label = null);

/// <summary>
/// A declared, explicitly non-linear mapping from simulated time to playback time.
/// </summary>
/// <remarks>
/// <para>
/// <b>RND-7 is unusually emphatic and this type is shaped by it.</b> An animation
/// "declares an explicit non-linear time mapping - playback rate per sequence phase -
/// and the current rate is displayed on screen throughout playback. Neither part is
/// optional. This is the animation equivalent of GRD-1: the artifact may compress, but
/// it may not hide that it compressed."
/// </para>
/// <para>
/// So there is no default rate and no way to omit the mapping: a spec with no phases
/// is refused rather than played at some convenient speed. The reason is the same one
/// §22 gives for the risk - six orders of magnitude of timescale cannot be shown
/// honestly at one rate, and <em>a viewer cannot detect the compression</em>. An ion
/// spends nanoseconds turning round in a mirror and hundreds of microseconds drifting;
/// an animation that skips the first to keep the second watchable has removed the part
/// the instrument was designed around, and nothing on screen would say so.
/// </para>
/// <para>
/// The stamp is not a spec option either. It is written by the renderer onto every
/// frame, for the same reason the QUALIFIED rule is drawn across a tainted figure: a
/// frame is the artifact most likely to be shown with none of its apparatus attached.
/// </para>
/// </remarks>
public sealed record AnimationSpec
{
    /// <summary>Version of this spec's shape, carried into the figure's provenance.</summary>
    public string AnimationSpecVersion { get; init; } = "0.1";

    /// <summary>Frames emitted per second of playback.</summary>
    public int FramesPerSecond { get; init; } = 30;

    /// <summary>Simulated time the animation starts at, in seconds.</summary>
    public double StartSeconds { get; init; }

    /// <summary>The phases, in order, each running to its own <c>UntilSeconds</c>.</summary>
    public IReadOnlyList<AnimationPhase> Phases { get; init; } = [];
}

/// <summary>One frame's place in both times, and the rate that got it there.</summary>
/// <param name="Index">Frame number, from zero.</param>
/// <param name="PlaybackSeconds">When it is shown, in seconds of playback.</param>
/// <param name="SimulatedSeconds">What instant of the flight it shows, in seconds.</param>
/// <param name="RateSiPerPlaybackSecond">The rate in force, in seconds per second.</param>
/// <param name="PhaseLabel">What the phase in force is called, or null.</param>
public readonly record struct AnimationFrame(
    int Index,
    double PlaybackSeconds,
    double SimulatedSeconds,
    double RateSiPerPlaybackSecond,
    string? PhaseLabel);

/// <summary>
/// Turns a declared time mapping into the instants the frames show.
/// </summary>
public static class TimeMapping
{
    /// <summary>The frames a spec calls for.</summary>
    /// <param name="spec">The animation spec.</param>
    /// <returns>The frames, in order, starting at the declared start.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="EinzelException">
    /// The spec declares no phases, a non-positive frame rate, a phase that does not
    /// advance, or a rate that is not a positive finite number.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Frame times are computed from playback time, never accumulated.</b> A phase
    /// whose playback duration is not a whole number of frames would otherwise push a
    /// fractional frame of error into every phase after it, and a six-phase animation
    /// would drift visibly against its own declared mapping. Here each frame's instant
    /// comes from its own playback time by one lookup and one multiply, so the last
    /// frame is exactly as accurate as the first.
    /// </para>
    /// <para>
    /// <b>The final frame is forced onto the end.</b> A playback duration that is not a
    /// whole number of frames otherwise stops short of the last instant, which for a
    /// flight is precisely the instant a reader wants - the arrival, the ejection, the
    /// packet at the detector.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AnimationFrame> Frames(AnimationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        Validate(spec);

        // Cumulative playback time at the end of each phase. The span is in simulated
        // seconds and the rate is simulated seconds per playback second, so the
        // quotient is playback seconds - which is the whole arithmetic of the mapping.
        var boundaries = new double[spec.Phases.Count];
        var playback = 0.0;
        var previous = spec.StartSeconds;

        for (var i = 0; i < spec.Phases.Count; i++)
        {
            playback += (spec.Phases[i].UntilSeconds - previous)
                / spec.Phases[i].RateSiPerPlaybackSecond;

            boundaries[i] = playback;
            previous = spec.Phases[i].UntilSeconds;
        }

        var total = playback;
        var step = 1.0 / spec.FramesPerSecond;
        var count = (int)Math.Floor(total / step) + 1;

        var frames = new List<AnimationFrame>(count + 1);

        for (var k = 0; k < count; k++)
        {
            frames.Add(At(spec, boundaries, k * step, k));
        }

        // The end, if the frame grid did not already land on it. Never a duplicate:
        // the guard is that the last gridded frame is strictly short of the total.
        if (frames.Count == 0 || frames[^1].PlaybackSeconds < total)
        {
            frames.Add(At(spec, boundaries, total, frames.Count));
        }

        return frames;
    }

    /// <summary>The total playback duration a spec calls for, in seconds.</summary>
    /// <param name="spec">The animation spec.</param>
    /// <returns>The duration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="EinzelException">The spec is not valid.</exception>
    public static double PlaybackSeconds(AnimationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        Validate(spec);

        var total = 0.0;
        var previous = spec.StartSeconds;

        foreach (var phase in spec.Phases)
        {
            total += (phase.UntilSeconds - previous) / phase.RateSiPerPlaybackSecond;
            previous = phase.UntilSeconds;
        }

        return total;
    }

    /// <summary>
    /// How a rate reads on a frame: the honest unit, and the intuition beside it.
    /// </summary>
    /// <param name="rateSiPerPlaybackSecond">Simulated seconds per playback second.</param>
    /// <returns>The line to stamp.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The rate is not positive.</exception>
    /// <remarks>
    /// <para>
    /// Two readings of one number, because they answer different questions. The rate in
    /// time-per-second of playback is what lets a reader convert anything on screen back
    /// into flight time, and it carries a unit, which is what GRD-1 asks of every
    /// quantity this platform reports. The slow-down factor is the intuition - "this is
    /// half a million times slower than the instrument" - and on its own it is not
    /// enough, because it says nothing about how long the flight is.
    /// </para>
    /// <para>
    /// The unit is chosen from the magnitude rather than fixed, because an animation may
    /// span nanoseconds of turn-around and milliseconds of trapping in the same
    /// sequence, and "0.000001 ms" is a number a reader has to decode rather than read.
    /// </para>
    /// </remarks>
    public static string Describe(double rateSiPerPlaybackSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateSiPerPlaybackSecond);

        var (scale, unit) = rateSiPerPlaybackSecond switch
        {
            < 1e-9 => (1e12, "ps"),
            < 1e-6 => (1e9, "ns"),
            < 1e-3 => (1e6, "µs"),
            < 1.0 => (1e3, "ms"),
            _ => (1.0, "s"),
        };

        var invariant = CultureInfo.InvariantCulture;
        var amount = rateSiPerPlaybackSecond * scale;
        var factor = 1.0 / rateSiPerPlaybackSecond;

        return string.Create(
            invariant,
            $"{amount:G4} {unit} of flight per second of playback — {factor:N0}x slower than real time");
    }

    private static AnimationFrame At(
        AnimationSpec spec, double[] boundaries, double playbackSeconds, int index)
    {
        // The phase in force at this playback instant. Linear rather than binary
        // because an animation has a handful of phases, and a sequence with hundreds
        // is a different feature.
        //
        // A frame landing exactly on a boundary takes the INCOMING phase, which is
        // why the comparison is not strict. Such a frame shows the boundary instant
        // and is then followed by a frame's worth of playback at the next rate, so
        // announcing the outgoing rate on it would be announcing the one that has
        // just stopped applying. The simulated instant is the same either way.
        var phase = 0;

        while (phase < boundaries.Length - 1 && playbackSeconds >= boundaries[phase])
        {
            phase++;
        }

        var start = phase == 0 ? 0.0 : boundaries[phase - 1];
        var simulatedStart = phase == 0 ? spec.StartSeconds : spec.Phases[phase - 1].UntilSeconds;
        var rate = spec.Phases[phase].RateSiPerPlaybackSecond;

        return new AnimationFrame(
            index,
            playbackSeconds,
            simulatedStart + ((playbackSeconds - start) * rate),
            rate,
            spec.Phases[phase].Label);
    }

    private static void Validate(AnimationSpec spec)
    {
        // Refused rather than defaulted, and that is RND-7 rather than fastidiousness:
        // "neither part is optional". A default rate is exactly the hidden compression
        // the requirement exists to prevent, and it would be invisible because it would
        // look like a choice somebody made.
        if (spec.Phases.Count == 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/animation/phases",
                Constraint = "an animation declares at least one playback phase",
                Suggestion = "add {\"until\": {\"value\": ..., \"unit\": \"us\"}, \"rate\": "
                    + "{\"value\": ..., \"unit\": \"us/s\"}}. There is deliberately no default: "
                    + "RND-7 makes the time mapping explicit because six orders of magnitude of "
                    + "timescale cannot be shown honestly at one rate and a viewer cannot detect "
                    + "the compression",
            });
        }

        if (spec.FramesPerSecond <= 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/animation/framesPerSecond",
                Constraint = $"{spec.FramesPerSecond} frames per second is not a rate",
                Suggestion = "use a positive integer; 24, 25 and 30 are the usual choices",
            });
        }

        var previous = spec.StartSeconds;

        for (var i = 0; i < spec.Phases.Count; i++)
        {
            var phase = spec.Phases[i];

            if (!(phase.RateSiPerPlaybackSecond > 0.0)
                || double.IsInfinity(phase.RateSiPerPlaybackSecond))
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"/animation/phases/{i.ToString(CultureInfo.InvariantCulture)}/rate",
                    Constraint = $"a playback rate of {phase.RateSiPerPlaybackSecond:G6} is not a "
                        + "positive finite number",
                    Suggestion = "the rate is how much simulated time passes per second of "
                        + "playback, so it is positive; a zero would never advance and a negative "
                        + "one would run the flight backwards",
                });
            }

            if (!(phase.UntilSeconds > previous))
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"/animation/phases/{i.ToString(CultureInfo.InvariantCulture)}/until",
                    Constraint = $"phase {i.ToString(CultureInfo.InvariantCulture)} runs to "
                        + $"{phase.UntilSeconds:G6} s, which does not advance past "
                        + $"{previous:G6} s",
                    Suggestion = "phases are cumulative and each names the simulated time it runs "
                        + "TO, so they increase strictly. A phase that does not advance has no "
                        + "frames in it and is more likely a duration written where an end time "
                        + "belongs",
                });
            }

            previous = phase.UntilSeconds;
        }
    }
}
