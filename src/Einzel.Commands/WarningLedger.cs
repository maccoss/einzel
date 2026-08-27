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
    private readonly HashSet<string> _thisEvaluation = new(StringComparer.Ordinal);
    private int _evaluations;

    private sealed record Entry(ValidityWarning Warning, int Count);

    /// <summary>How many evaluations have been counted.</summary>
    public int Evaluations => _evaluations;

    /// <summary>Records a warning against the evaluation in progress.</summary>
    /// <param name="warning">What the evaluation earned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="warning"/> is null.</exception>
    /// <remarks>
    /// An evaluation that earns the same code twice - one field warning per ion in
    /// an ensemble, say - counts once, because the unit being counted is the
    /// evaluation and not the emission.
    /// </remarks>
    public void Add(ValidityWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        if (!_thisEvaluation.Add(warning.Code))
        {
            return;
        }

        _byCode[warning.Code] = _byCode.TryGetValue(warning.Code, out var existing)
            ? existing with { Count = existing.Count + 1 }
            : new Entry(warning, 1);
    }

    /// <summary>Closes the evaluation in progress and opens the next.</summary>
    public void EndEvaluation()
    {
        _evaluations++;
        _thisEvaluation.Clear();
    }

    /// <summary>What the evaluations earned, most widespread first.</summary>
    /// <remarks>
    /// Each message says how many evaluations earned it, because "on 3 of 1000
    /// draws" and "on 1000 of 1000 draws" are the difference between a corner of
    /// the tolerance box and a study that should be thrown away.
    /// </remarks>
    public IReadOnlyList<ValidityWarning> Collected =>
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
