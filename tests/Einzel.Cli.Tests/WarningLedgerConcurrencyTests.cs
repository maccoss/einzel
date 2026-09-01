using Einzel.Commands;
using Einzel.Core.Results;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The ledger counts evaluations, and has to keep counting them when they run at once.
/// </summary>
/// <remarks>
/// <para>
/// The unit being counted is the <b>evaluation</b>, not the emission: an ensemble figure
/// builds the field once per ion, so one draw can earn the same code twenty-one times and
/// still be one draw that earned it. That deduplication needs a scope, and the scope used
/// to be a single set shared by the whole study.
/// </para>
/// <para>
/// <b>Which is right only while evaluations are strictly sequential.</b> Run two at once and
/// it fails twice over — the dictionary and the set race, and, worse, one evaluation's
/// warning silently suppresses another's identical code. The second failure leaves no trace:
/// a study that was wrong throughout reports having been wrong on half its draws, which is a
/// plausible number and therefore the expensive kind.
/// </para>
/// </remarks>
public sealed class WarningLedgerConcurrencyTests(ITestOutputHelper output)
{
    private static ValidityWarning Warning(string code) =>
        new(code, code, WarningSeverity.Qualified);

    /// <summary>How many evaluations a code was counted against.</summary>
    private static int CountFor(WarningLedger ledger, string code)
    {
        var message = ledger.Collected.Single(w => w.Code == code).Message;

        // "code (on N of M evaluations)"
        var after = message[(message.IndexOf("(on ", StringComparison.Ordinal) + 4)..];

        return int.Parse(
            after.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>One evaluation earning a code many times is one evaluation.</summary>
    /// <remarks>
    /// The invariant the scope exists for, checked sequentially so the concurrent case
    /// below is measured against a known-correct answer rather than against itself.
    /// </remarks>
    [Fact]
    public void ManyEmissionsInOneEvaluationCountOnce()
    {
        var ledger = new WarningLedger();

        for (var draw = 0; draw < 7; draw++)
        {
            var evaluation = ledger.BeginEvaluation();

            // An ensemble figure's field warning, once per ion.
            for (var ion = 0; ion < 21; ion++)
            {
                evaluation.Add(Warning("field.unconverged"));
            }

            evaluation.Close();
        }

        output.WriteLine(ledger.Collected.Single().Message);

        Assert.Equal(7, ledger.Evaluations);
        Assert.Equal(7, CountFor(ledger, "field.unconverged"));
    }

    /// <summary>Evaluations running at once are still counted one each.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the assertion a shared scope cannot satisfy.</b> Every evaluation earns the
    /// same code, so the count must equal the evaluation count exactly — and with one shared
    /// set, whichever evaluation added the code first would suppress every other, leaving a
    /// count far below the truth.
    /// </para>
    /// <para>
    /// Not merely "does not throw": a racing dictionary usually does not throw either, it
    /// loses entries. The count is the observable that distinguishes the two.
    /// </para>
    /// </remarks>
    [Fact]
    public void ConcurrentEvaluationsAreEachCountedOnce()
    {
        const int Evaluations = 2_000;

        var ledger = new WarningLedger();

        Parallel.For(0, Evaluations, _ =>
        {
            var evaluation = ledger.BeginEvaluation();

            for (var ion = 0; ion < 8; ion++)
            {
                evaluation.Add(Warning("field.unconverged"));
            }

            // A second code on every other evaluation, so one count is the evaluation
            // total and the other is half of it - a single shared set would collapse
            // both to something much smaller and equal.
            if (_ % 2 == 0)
            {
                evaluation.Add(Warning("mobility.outside-fit"));
            }

            evaluation.Close();
        });

        foreach (var warning in ledger.Collected)
        {
            output.WriteLine(warning.Message);
        }

        Assert.Equal(Evaluations, ledger.Evaluations);
        Assert.Equal(Evaluations, CountFor(ledger, "field.unconverged"));
        Assert.Equal(Evaluations / 2, CountFor(ledger, "mobility.outside-fit"));
    }

    /// <summary>A scope counts against the ledger that opened it.</summary>
    /// <remarks>
    /// Two studies can run at once, and a scope closed against the wrong ledger would move a
    /// draw from one to the other — an error that reads as a plausible number rather than as
    /// a failure. The scope carries its own ledger, so the pairing is not something a caller
    /// can get wrong.
    /// </remarks>
    [Fact]
    public void AScopeCountsAgainstTheLedgerThatOpenedIt()
    {
        var first = new WarningLedger();
        var second = new WarningLedger();

        var a = first.BeginEvaluation();
        var b = second.BeginEvaluation();

        a.Add(Warning("only.first"));
        b.Add(Warning("only.second"));

        // Closed out of order, which is what concurrency does.
        b.Close();
        a.Close();

        output.WriteLine($"first  {first.Evaluations} evaluation(s): "
            + string.Join(", ", first.Collected.Select(w => w.Code)));
        output.WriteLine($"second {second.Evaluations} evaluation(s): "
            + string.Join(", ", second.Collected.Select(w => w.Code)));

        Assert.Equal(1, first.Evaluations);
        Assert.Equal(1, second.Evaluations);
        Assert.Equal("only.first", first.Collected.Single().Code);
        Assert.Equal("only.second", second.Collected.Single().Code);
    }

    /// <summary>The exemplar message does not depend on which evaluation finished first.</summary>
    /// <remarks>
    /// <para>
    /// <b>A code is one fact, but its message often carries the numbers of the evaluation
    /// that raised it</b> — <c>CONVERGENCE_ORDER_BELOW_NOMINAL</c> quotes the observed order
    /// of its own draw. Keeping whichever arrived first was deterministic only while
    /// evaluations ran in order.
    /// </para>
    /// <para>
    /// This is the regression that caught it: two runs of the same seeded sweep reported
    /// identical counts and identical figures, and a <i>different example order</i> in the
    /// warning text, so <c>ASweepIsReproducibleFromItsSeed</c> failed on a string nobody had
    /// thought of as a result. PRJ-3's claim is that a study reproduces from its manifest,
    /// and a warning is part of what it produced.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExemplarMessageDoesNotDependOnCompletionOrder()
    {
        // Every evaluation earns the same code with a different number in the message,
        // which is what a per-draw diagnostic looks like.
        static WarningLedger Run(bool reversed)
        {
            var ledger = new WarningLedger();
            var order = Enumerable.Range(0, 64);

            foreach (var draw in reversed ? order.Reverse() : order)
            {
                var evaluation = ledger.BeginEvaluation();

                evaluation.Add(new ValidityWarning(
                    "CONVERGENCE_ORDER_BELOW_NOMINAL",
                    $"observed order {draw * 0.01:G3} against nominal 2",
                    WarningSeverity.Qualified));

                evaluation.Close();
            }

            return ledger;
        }

        var forwards = Run(reversed: false).Collected.Single();
        var backwards = Run(reversed: true).Collected.Single();

        output.WriteLine($"forwards   {forwards.Message}");
        output.WriteLine($"backwards  {backwards.Message}");

        // Same study, same draws, opposite completion order - and one message. Under the
        // old rule these differ, because each run kept whatever it saw first.
        Assert.Equal(forwards.Message, backwards.Message);
    }
}
