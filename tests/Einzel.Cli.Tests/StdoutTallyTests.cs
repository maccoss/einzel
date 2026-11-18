namespace Einzel.Cli.Tests;

/// <summary>
/// Two calls of the CLI overlapping in one process each know whether they themselves wrote to
/// standard output, and the console is left as it was found.
/// </summary>
/// <remarks>
/// <c>Main</c> swapped the console's writer for a counting one and put the captured writer back on
/// the way out, so overlapping calls restored in the wrong order: the second wrapped the first's
/// counter, its output marked the first as having written - suppressing the first's JSON
/// refusal - and a stale counter was left as the console's writer.
/// </remarks>
[Collection(nameof(RunsAlone))]
public sealed class StdoutTallyTests
{
    /// <summary>Overlapping calls on two threads, entered and left out of order.</summary>
    [Fact]
    public void OverlappingCallsKeepTheirOwnFlagAndRestoreTheConsole()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        var console = Console.Out;

        try
        {
            using var firstEntered = new ManualResetEventSlim();
            using var secondWrote = new ManualResetEventSlim();
            using var firstLeft = new ManualResetEventSlim();

            bool firstWritten = true, secondWrittenBeforeLeaving = false, secondWrittenAfter = false;

            var first = new Thread(() =>
            {
                var tally = StdoutTally.Enter();
                firstEntered.Set();
                secondWrote.Wait();
                firstWritten = tally.Written;
                tally.Dispose();
                firstLeft.Set();
            });

            var second = new Thread(() =>
            {
                firstEntered.Wait();
                using var tally = StdoutTally.Enter();
                Console.Out.Write("second's document");
                secondWrittenBeforeLeaving = tally.Written;
                secondWrote.Set();
                firstLeft.Wait();

                // The first has left; this call's writes still reach the console and still count.
                Console.Out.Write(" and more");
                secondWrittenAfter = tally.Written;
            });

            first.Start();
            second.Start();
            first.Join();
            second.Join();

            Assert.False(firstWritten, "the second call's output was counted as the first's");
            Assert.True(secondWrittenBeforeLeaving);
            Assert.True(secondWrittenAfter);
            Assert.Equal("second's document and more", captured.ToString());
            Assert.Same(console, Console.Out);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>
    /// What goes through the tally is byte for byte what the console's own writer would write: its
    /// line ending and its format provider, not a writer's defaults.
    /// </summary>
    [Fact]
    public void TheTallyWritesTheBytesTheConsoleWould()
    {
        var original = Console.Out;
        var comma = (System.Globalization.NumberFormatInfo)System.Globalization.CultureInfo.InvariantCulture.NumberFormat.Clone();
        comma.NumberDecimalSeparator = ",";
        var captured = new StringWriter(comma) { NewLine = "|\n" };
        Console.SetOut(captured);

        try
        {
            using (StdoutTally.Enter())
            {
                Console.Out.WriteLine("a");
                Console.Out.WriteLine(1.5);
                Console.Out.WriteLine();
                Console.Out.WriteLine('c');
                Console.Out.WriteLine(['d', 'e']);
            }

            Assert.Equal("a|\n1,5|\n|\nc|\nde|\n", captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>A call that writes nothing is not written, and one that writes is.</summary>
    [Fact]
    public void ACallKnowsWhetherItWrote()
    {
        var original = Console.Out;
        Console.SetOut(new StringWriter());

        try
        {
            using (var quiet = StdoutTally.Enter())
            {
                Assert.False(quiet.Written);
            }

            using var loud = StdoutTally.Enter();
            Console.Out.WriteLine("x");
            Assert.True(loud.Written);
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
