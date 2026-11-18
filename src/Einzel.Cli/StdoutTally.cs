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

    // Every call on the writing flow, the innermost and each it is nested in: a nested call's
    // output is the outer call's output too, and marking only the innermost let the outer write a
    // second JSON document after it.
    //
    // Stopping at the first call already marked, because marking is a whole chain at once: a
    // marked call's outer calls were marked with it. So after a call's first write each further
    // one - a character at a time, through some writers - costs one read of the flow's call and
    // one read of its flag, where it walked the chain and wrote every flag in it again.
    private static void Mark()
    {
        for (var tally = Current.Value; tally is not null && !tally.Written; tally = tally._outer)
        {
            tally.MarkWritten();
        }
    }

    private void MarkWritten() => Volatile.Write(ref _written, true);

    /// <summary>The writer standing in for the console's own while any call is running.</summary>
    /// <remarks>
    /// Writes the bytes the wrapped writer would: its line ending, its format provider and its
    /// encoding, not a <see cref="TextWriter"/>'s defaults. A base <c>WriteLine</c> appends this
    /// writer's own line ending, which was the platform's - CR LF through a writer set to LF.
    /// </remarks>
    private sealed class Counter : TextWriter
    {
        public Counter(TextWriter inner)
            : base(inner.FormatProvider)
        {
            Inner = inner;
            CoreNewLine = inner.NewLine.ToCharArray();
        }

        public TextWriter Inner { get; }

        public override IFormatProvider FormatProvider => Inner.FormatProvider;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string NewLine
        {
            get => Inner.NewLine;
            set
            {
                Inner.NewLine = value;
                CoreNewLine = Inner.NewLine.ToCharArray();
            }
        }

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

        public override void Write(ReadOnlySpan<char> buffer)
        {
            if (!buffer.IsEmpty)
            {
                Mark();
            }

            Inner.Write(buffer);
        }

        public override void WriteLine(string? value)
        {
            Mark();
            Inner.WriteLine(value);
        }

        public override void WriteLine()
        {
            Mark();
            Inner.WriteLine();
        }

        public override void Flush() => Inner.Flush();
    }
}
