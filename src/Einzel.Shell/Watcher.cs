using System.Diagnostics;

using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>
/// Feeds a viewport from a run that is still going, at a rate a person can watch.
/// </summary>
/// <param name="show">Told each frame, on whatever thread the run is on.</param>
/// <param name="every">How often a frame is wanted.</param>
/// <remarks>
/// <para>
/// <b>Which steps report is set by the wall clock, not by a step count.</b> A diffusive
/// step is a stability limit rather than a unit of time, so "every thousandth step" is a
/// different cadence on every model and on every mesh. Asking the clock gives one cadence
/// for all of them.
/// </para>
/// <para>
/// <b>Watching must not change the answer</b>, and the only thing standing behind that is
/// that this decides <em>when</em> to look and never touches what it is shown. The engine
/// hands over its own live buffers rather than a copy, which is what makes reporting cheap
/// enough to do at all - and would make a scribbling observer a defect nobody could
/// reproduce, since which steps report depends on how busy the machine was.
/// </para>
/// </remarks>
public sealed class Watcher(Action<ViewportOutcome> show, TimeSpan every)
    : ViewportCommand.IViewportProgress
{
    private readonly Stopwatch _since = Stopwatch.StartNew();

    // Null rather than a sentinel instant, and both wrong answers were tried. TimeSpan.Zero
    // refuses the FIRST frame, because the stopwatch also reads about zero there - a watch
    // that opens on an empty box and stays empty until an interval has passed, which reads
    // as a run that has not started. TimeSpan.MinValue then overflows the subtraction. What
    // is actually being asked is "has a frame been wanted yet", so it is asked directly.
    private TimeSpan? _last;

    /// <summary>The last frame that had a packet in it.</summary>
    /// <remarks>
    /// <b>The end of a run is empty whenever the ions arrived.</b> Applying the final frame
    /// unconditionally spends minutes drawing a packet and then replaces it with an empty
    /// box - the exact picture the density work exists to stop a diffusive model producing.
    /// So the last frame that held something is kept, and it is what the viewport is left
    /// showing.
    /// </remarks>
    public ViewportOutcome? LastWithPacket { get; private set; }

    /// <inheritdoc />
    public bool Wants(int steps, double timeSeconds)
    {
        var now = _since.Elapsed;

        if (_last is { } last && now - last < every)
        {
            return false;
        }

        _last = now;

        return true;
    }

    /// <inheritdoc />
    public void Reached(ViewportOutcome frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Density.Count > 0)
        {
            LastWithPacket = frame;
        }

        show(frame);
    }
}
