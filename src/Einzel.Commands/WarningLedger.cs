using Einzel.Core.Results;

namespace Einzel.Commands;

/// <summary>
/// Collects the warnings a study's evaluations earn, so they reach its result.
/// </summary>
/// <remarks>
/// <para>
/// GRD-2 says a warning propagates through the engine, the command layer, the CLI
/// and the exported file, and is not suppressible above threshold. A sweep broke
/// that at one seam: the evaluator a driver ranks by returns a bare double, so
/// every warning the underlying flight earned - a field that missed its tolerance,
/// a mode outside its validity, an ion that never landed - was discarded at the
/// boundary between the figure of merit and the driver. A thousand draws could all
/// be outside validity and the study would report a distribution and nothing else.
/// </para>
/// <para>
/// The ledger is the sink that seam needed. Distinct by code rather than one entry
/// per draw, because a thousand copies of the same sentence is not a thousand
/// facts, and it counts how many evaluations earned each so a reader can tell one
/// unlucky draw from a study that was wrong throughout.
/// </para>
/// </remarks>
public sealed class WarningLedger
{
    private readonly Dictionary<string, Entry> _byCode = new(StringComparer.Ordinal);
    private readonly System.Threading.Lock _gate = new();
    private int _evaluations;

    private sealed record Entry(ValidityWarning Warning, int Count);

    /// <summary>How many evaluations have been counted.</summary>
    public int Evaluations
    {
        get
        {
            lock (_gate)
            {
                return _evaluations;
            }
        }
    }

    /// <summary>Opens a scope for one evaluation's warnings.</summary>
    /// <returns>The sink that evaluation reports to, and closes when it ends.</returns>
    /// <remarks>
    /// <para>
    /// <b>An evaluation owns its own scope, and that is what makes the count right under
    /// concurrency.</b> The unit being counted is the evaluation rather than the emission -
    /// an ensemble figure builds the field once per ion and earns the same code twenty-one
    /// times - so the deduplication has to be scoped to one evaluation and nothing else.
    /// </para>
    /// <para>
    /// A single shared set works only while evaluations are strictly sequential. Run two at
    /// once and it fails twice over: the dictionary and the set race, and, worse, one
    /// evaluation's warning silently suppresses another's identical code, so a study that
    /// was wrong throughout reports having been wrong on half its draws. That second failure
    /// leaves no trace - it is a plausible number, which is the kind this project keeps
    /// having to hunt down.
    /// </para>
    /// </remarks>
    public EvaluationWarnings BeginEvaluation() => new(this);

    /// <summary>Counts one closed evaluation and merges what it earned.</summary>
    /// <remarks>
    /// The merge is the only place two evaluations meet, so it is the only place that
    /// locks - and it is O(distinct codes), which is single digits. Internal because an
    /// evaluation closes itself: see <see cref="EvaluationWarnings.Close"/>.
    /// </remarks>
    internal void Merge(IEnumerable<ValidityWarning> distinct)
    {
        lock (_gate)
        {
            _evaluations++;

            foreach (var warning in distinct)
            {
                if (!_byCode.TryGetValue(warning.Code, out var existing))
                {
                    _byCode[warning.Code] = new Entry(warning, 1);
                    continue;
                }

                // THE EXEMPLAR IS CHOSEN, NOT INHERITED FROM WHOEVER ARRIVED FIRST.
                //
                // A code is one fact but its message often carries the numbers of the
                // evaluation that raised it - CONVERGENCE_ORDER_BELOW_NOMINAL quotes the
                // observed order of its own draw. Keeping whichever arrived first was
                // deterministic only while evaluations ran in order; with them running at
                // once, two runs of the same seeded study reported the same counts and a
                // different example, and PRJ-3's whole claim is that a study reproduces
                // from its manifest.
                //
                // Ordinally least, because it needs to be stable rather than meaningful:
                // every draw that earned the code is equally entitled to be the example,
                // the count is what carries the information, and the sequential rule -
                // "whichever draw came first" - was no less arbitrary.
                var keep = string.CompareOrdinal(warning.Message, existing.Warning.Message) < 0
                    ? warning
                    : existing.Warning;

                _byCode[warning.Code] = new Entry(keep, existing.Count + 1);
            }
        }
    }

    /// <summary>What the evaluations earned, most widespread first.</summary>
    /// <remarks>
    /// Each message says how many evaluations earned it, because "on 3 of 1000
    /// draws" and "on 1000 of 1000 draws" are the difference between a corner of
    /// the tolerance box and a study that should be thrown away.
    /// </remarks>
    public IReadOnlyList<ValidityWarning> Collected
    {
        get
        {
            lock (_gate)
            {
                return
                [
                    .. _byCode.Values
                        .OrderByDescending(e => e.Count)
                        .ThenBy(e => e.Warning.Code, StringComparer.Ordinal)
                        .Select(e => e.Warning with
                        {
                            Message = $"{e.Warning.Message} (on {e.Count} of "
                                + $"{Math.Max(_evaluations, e.Count)} evaluations)",
                        }),
                ];
            }
        }
    }
}

/// <summary>One evaluation's distinct warning codes.</summary>
/// <remarks>
/// Confined to a single evaluation, which runs on one thread, so it needs no lock of its
/// own - the ledger locks where the scopes meet. Handed out by
/// <see cref="WarningLedger.BeginEvaluation"/> rather than constructed, so there is no way
/// to report into a scope no ledger will ever collect.
/// </remarks>
public sealed class EvaluationWarnings
{
    private readonly Dictionary<string, ValidityWarning> _distinct = new(StringComparer.Ordinal);
    private readonly WarningLedger _ledger;

    internal EvaluationWarnings(WarningLedger ledger) => _ledger = ledger;

    /// <summary>Records a warning against this evaluation.</summary>
    /// <param name="warning">What the evaluation earned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="warning"/> is null.</exception>
    /// <remarks>
    /// The same code twice in one evaluation is one fact, so the first wins and the rest
    /// are dropped.
    /// </remarks>
    public void Add(ValidityWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        _distinct.TryAdd(warning.Code, warning);
    }

    /// <summary>Closes this evaluation and counts it against the ledger that opened it.</summary>
    /// <remarks>
    /// The scope carries its own ledger so the two cannot be mismatched - with two studies
    /// running at once, a scope closed against the wrong ledger would move a count from one
    /// study to the other, which is the sort of error that reads as a plausible number.
    /// </remarks>
    public void Close() => _ledger.Merge(_distinct.Values);
}
