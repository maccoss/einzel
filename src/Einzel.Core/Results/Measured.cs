using System.Globalization;
using Einzel.Core.Units;

namespace Einzel.Core.Results;

/// <summary>
/// A quantitative result. The only way the engine reports a number.
/// </summary>
/// <remarks>
/// <para>
/// GRD-1, stated in full: "Every quantitative result carries value, units,
/// uncertainty or confidence interval, the ensemble size or convergence measure
/// behind it, and any active warnings. The API offers no way to obtain the value
/// alone."
/// </para>
/// <para>
/// That last sentence is the design constraint, and it is why this type exposes
/// no property returning the value. The only route to the magnitude is
/// <see cref="Deconstruct"/>, which hands back the uncertainty, the evidence, and
/// the warnings in the same call. A caller can still discard them, but only by
/// writing a discard — a visible, greppable act — rather than by reaching for a
/// convenience accessor that was never offered.
/// </para>
/// <para>
/// The spec explains the absolutism: an agent handed <c>R = 19,800</c> reports
/// 19,800. An agent handed that value with n = 1000, an interval of plus or minus
/// 400, and a warning that pseudopotential validity was exceeded over part of the
/// path can reason about whether to trust it. The rule is stated absolutely
/// because a scalar accessor "will be added by someone eventually, and then used
/// everywhere". <c>MeasuredApiSurfaceTests</c> enforces this by reflection so the
/// rule outlives the person who wrote it down.
/// </para>
/// <para>
/// Preview status (GRD-5), extension attribution (GRD-6), and defect taint
/// (GRD-11) ride in <see cref="Warnings"/> at
/// <see cref="WarningSeverity.Provenance"/> rather than as separate fields.
/// They behave identically to validity warnings — non-suppressible, propagating
/// to every export and figure by GRD-2 — so modelling them the same way means
/// one propagation path to get right instead of four.
/// </para>
/// </remarks>
public sealed class Measured
{
    private readonly Quantity _value;
    private readonly ValidityWarning[] _warnings;

    /// <summary>Creates a result envelope.</summary>
    /// <param name="value">The value, carrying its dimension.</param>
    /// <param name="uncertainty">The uncertainty interval. Required by GRD-1.</param>
    /// <param name="evidence">The ensemble size or convergence measure. Required by GRD-1.</param>
    /// <param name="warnings">Active warnings, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="evidence"/> is null.</exception>
    public Measured(
        Quantity value,
        UncertaintyInterval uncertainty,
        Evidence evidence,
        IEnumerable<ValidityWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        _value = value;
        _warnings = warnings?.ToArray() ?? [];
        Uncertainty = uncertainty;
        Evidence = evidence;
    }

    /// <summary>The uncertainty interval behind the value.</summary>
    public UncertaintyInterval Uncertainty { get; }

    /// <summary>The ensemble size or convergence measure behind the value.</summary>
    public Evidence Evidence { get; }

    /// <summary>Active warnings, in the order they were attached.</summary>
    public IReadOnlyList<ValidityWarning> Warnings => _warnings;

    /// <summary>
    /// The physical dimension of the value. Exposed because a caller routinely
    /// needs to know what kind of quantity this is — to pick a unit for display,
    /// or to reject a mismatched comparison — without needing the magnitude.
    /// </summary>
    public Dimension Dimension => _value.Dimension;

    /// <summary>
    /// Whether any attached warning reports a validity violation or a provenance
    /// taint, and therefore cannot be suppressed.
    /// </summary>
    public bool HasNonSuppressibleWarnings => _warnings.Any(w => !w.IsSuppressible);

    /// <summary>
    /// The only route to the value, and it hands back the whole envelope.
    /// </summary>
    /// <param name="value">The value, carrying its dimension.</param>
    /// <param name="uncertainty">The uncertainty interval.</param>
    /// <param name="evidence">The ensemble size or convergence measure.</param>
    /// <param name="warnings">Active warnings.</param>
    public void Deconstruct(
        out Quantity value,
        out UncertaintyInterval uncertainty,
        out Evidence evidence,
        out IReadOnlyList<ValidityWarning> warnings)
    {
        value = _value;
        uncertainty = Uncertainty;
        evidence = Evidence;
        warnings = _warnings;
    }

    /// <summary>
    /// Returns a copy carrying an additional warning. GRD-2: warnings propagate,
    /// so every transformation of a result must carry the originals forward.
    /// </summary>
    /// <param name="warning">The warning to attach.</param>
    /// <returns>A new envelope with the warning appended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="warning"/> is null.</exception>
    public Measured WithWarning(ValidityWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return new Measured(_value, Uncertainty, Evidence, [.. _warnings, warning]);
    }

    /// <summary>
    /// Renders the full envelope in a named unit: value, interval, evidence, and
    /// warning count. There is no formatting overload that omits them.
    /// </summary>
    /// <param name="unit">The unit symbol to express the value in.</param>
    /// <returns>A single-line rendering of the whole envelope.</returns>
    /// <exception cref="Errors.EinzelException">
    /// The unit is unknown or of the wrong dimension.
    /// </exception>
    public string Format(string unit)
    {
        var definition = UnitRegistry.Resolve(unit);
        return FormatWith(_value.In(unit), Uncertainty.WidthSi / 2.0 / definition.SiFactor, unit);
    }

    private string FormatWith(double magnitude, double halfWidth, string unitLabel)
    {
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{magnitude:G6} ± {halfWidth:G3} {unitLabel} ({Uncertainty.ConfidenceLevel:P0} CI)");

        text += Evidence switch
        {
            Evidence.Ensemble e => $", n = {e.EnsembleSize}" + (e.Converged ? string.Empty : ", NOT CONVERGED"),
            Evidence.Convergence c => string.Create(
                CultureInfo.InvariantCulture,
                $", converged in {c.Measure} at order {c.ObservedOrder:G3} of {c.NominalOrder:G3}"),
            Evidence.Analytic a => $", analytic: {a.Reference}",
            _ => string.Empty,
        };

        if (_warnings.Length > 0)
        {
            text += $" [{_warnings.Length} warning{(_warnings.Length == 1 ? string.Empty : "s")}]";
        }

        return text;
    }

    /// <summary>
    /// Renders the envelope in coherent SI units. Never throws: a dimension with
    /// no registered symbol falls back to SI base exponents.
    /// </summary>
    /// <returns>A single-line rendering.</returns>
    public override string ToString()
    {
        var symbol = SiSymbolFor(_value.Dimension);

        return symbol is null
            ? FormatWith(_value.SiValue, Uncertainty.WidthSi / 2.0, _value.Dimension.ToString())
            : Format(symbol);
    }

    private static string? SiSymbolFor(Dimension dimension)
    {
        foreach (var candidate in UnitRegistry.All)
        {
            if (candidate.Dimension == dimension && candidate.SiFactor == 1.0)
            {
                return candidate.Symbol;
            }
        }

        return null;
    }
}
