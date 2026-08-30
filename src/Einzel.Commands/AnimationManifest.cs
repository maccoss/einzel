using Einzel.Render;

namespace Einzel.Commands;

/// <summary>One frame's place in the schedule, as written to disk.</summary>
public sealed record AnimationFrameJson
{
    /// <summary>Frame number, from zero.</summary>
    public required int Index { get; init; }

    /// <summary>The file this frame was written to, relative to the manifest.</summary>
    public required string File { get; init; }

    /// <summary>When it is shown, in seconds of playback.</summary>
    public required double PlaybackSeconds { get; init; }

    /// <summary>What instant of the flight it shows, in seconds.</summary>
    public required double SimulatedSeconds { get; init; }

    /// <summary>The rate in force, in seconds of flight per second of playback.</summary>
    public required double RateSiPerPlaybackSecond { get; init; }

    /// <summary>The phase in force, or null.</summary>
    public string? Phase { get; init; }
}

/// <summary>The schedule an animation was drawn on.</summary>
/// <remarks>
/// <para>
/// Written beside the frames because the mapping is the thing that has to be
/// auditable. Every frame carries its rate on the page, which RND-7 requires; this
/// carries the whole schedule, so a reader can check that the compression is what the
/// spec said rather than taking the stamps on trust one frame at a time.
/// </para>
/// <para>
/// It is also what a player needs. Frames are equally spaced in <em>playback</em> time
/// and not in flight time, so anything replaying them has to know the frame rate, and
/// anything indexing into them by flight time has to know the mapping.
/// </para>
/// </remarks>
public sealed record AnimationManifest
{
    /// <summary>The model that was animated.</summary>
    public required string Model { get; init; }

    /// <summary>Content hash of the model document (PRJ-3).</summary>
    public required string ModelHash { get; init; }

    /// <summary>Frames per second of playback.</summary>
    public required int FramesPerSecond { get; init; }

    /// <summary>Total playback duration, in seconds.</summary>
    public required double PlaybackSeconds { get; init; }

    /// <summary>The format the frames were written in.</summary>
    public required string Format { get; init; }

    /// <summary>Every frame, in order.</summary>
    public required IReadOnlyList<AnimationFrameJson> Frames { get; init; }
}
