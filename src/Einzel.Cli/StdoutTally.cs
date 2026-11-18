namespace Einzel.Cli;

/// <summary>
/// Whether one call of <see cref="Program.Main"/> has written anything to standard output, so a
/// refusal raised after a verb wrote its own result does not add a second document to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The flag is the call's, not the writer's.</b> The first version wrapped the console's
/// writer in a counting writer for the length of the call and put the captured writer back on
/// the way out. Two calls in one process overlapping - parallel tests, the corpus gate - did that
/// out of order: the second wrapped the first's counter, so the second's output marked the first
/// as having written and suppressed the first's JSON refusal, and the first restoring the
/// original left the second writing past its own counter.
/// </para>
/// <para>
/// So a write marks whichever call is running on the writing context
/// (<see cref="AsyncLocal{T}"/>, which flows into the parallel loops a verb starts). The
/// counting writer is installed over the console's own once and shared by every call that finds
/// it there, and is taken down by the last of them to leave, and only if it is still the
/// console's writer then.
/// </para>
/// </remarks>
public sealed class StdoutTally : IDisposable
{
    private static readonly AsyncLocal<StdoutTally?> Current = new();
    private static readonly object Gate = new();

    // The counter last installed, if any: the console wraps whatever it is given in a synchronized
    // writer, so the counter is recognised by the writer the console handed back for it.
    private static Counter? _installed;

    private readonly StdoutTally? _outer;
    private readonly Counter _counter;
    private bool _written;
    private bool _disposed;

    private StdoutTally(Counter counter, StdoutTally? outer)
    {
        _counter = counter;
        _outer = outer;
    }

    /// <summary>Whether this call has written anything to standard output.</summary>
    public bool Written => Volatile.Read(ref _written);

    /// <summary>Begins counting this call's writes to standard output.</summary>
    /// <returns>The call's tally; dispose it when the call ends.</returns>
    public static StdoutTally Enter()
    {
        Counter counter;

        lock (Gate)
        {
            if (_installed is { } installed && ReferenceEquals(Console.Out, installed.AsConsole))
            {
                counter = installed;
            }
            else
            {
                counter = new Counter(Console.Out);
                Console.SetOut(counter);
                counter.AsConsole = Console.Out;
                _installed = counter;
            }

            counter.Users++;
        }

        var tally = new StdoutTally(counter, Current.Value);
        Current.Value = tally;
        return tally;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Current.Value = _outer;

        lock (Gate)
        {
            _counter.Users--;

            if (_counter.Users == 0 && ReferenceEquals(Console.Out, _counter.AsConsole))
            {
                Console.SetOut(_counter.Inner);

                if (ReferenceEquals(_installed, _counter))
                {
                    _installed = null;
                }
            }
        }
    }

    private static void Mark() => Current.Value?.MarkWritten();

    private void MarkWritten() => Volatile.Write(ref _written, true);

    /// <summary>The writer standing in for the console's own while any call is running.</summary>
    private sealed class Counter(TextWriter inner) : TextWriter
    {
        public TextWriter Inner { get; } = inner;

        public int Users { get; set; }

        public TextWriter? AsConsole { get; set; }

        public override System.Text.Encoding Encoding => Inner.Encoding;

        public override void Write(char value)
        {
            Mark();
            Inner.Write(value);
        }

        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Mark();
            }

            Inner.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            if (count > 0)
            {
                Mark();
            }

            Inner.Write(buffer, index, count);
        }

        public override void Flush() => Inner.Flush();
    }
}
