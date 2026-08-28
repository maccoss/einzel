using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Sweeps;

/// <summary>Which side of a threshold counts as being inside the region.</summary>
public enum BoundarySense
{
    /// <summary>Inside is where the figure is at or above the threshold.</summary>
    Above,

    /// <summary>Inside is where the figure is at or below the threshold.</summary>
    Below,
}

/// <summary>
/// What a boundary search found.
/// </summary>
/// <param name="Boundary">
/// The boundary, in SI, as a GRD-1 envelope whose interval <em>is</em> the final
/// bracket. That is the honest reading: bisection does not produce a value with an
/// error bar around it, it produces an interval known to contain the crossing, and
/// the midpoint is a convention.
/// </param>
/// <param name="LowSi">The inside end of the final bracket.</param>
/// <param name="HighSi">The outside end.</param>
/// <param name="Evaluations">How many figures of merit were computed.</param>
/// <param name="ResolvedFraction">
/// The bracket width as a fraction of the scanned range - the currency ACC-6 is
/// written in.
/// </param>
/// <param name="MetAccuracyTarget">Whether it reached the requested resolution.</param>
/// <param name="Probes">
/// How many points the confirmation walk tested outside the final bracket, looking
/// for a second crossing bisection cannot see.
/// </param>
/// <param name="SecondCrossingSi">
/// Where the walk found the predicate flip back, or null if it never did. Not null
/// means the located edge is one of several, so the bracket is a claim about this
/// bracket rather than about the region.
/// </param>
/// <param name="Warnings">What the search has to say about its own validity.</param>
public sealed record BoundaryResult(
    Measured Boundary,
    double LowSi,
    double HighSi,
    int Evaluations,
    double ResolvedFraction,
    bool MetAccuracyTarget,
    int Probes,
    double? SecondCrossingSi,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>
/// Locates the value of a parameter at which a figure of merit crosses a
/// threshold, by bisection.
/// </summary>
/// <remarks>
/// <para>
/// ACC-6: "Class B boundary resolution ≤ 1/500 of scan. Enough to resolve a mass
/// filter peak shape." A grid does not do this. <see cref="ParameterScan"/> reports
/// which of its intervals the figure moves fastest across, and to reach one part in
/// five hundred that way costs 501 evaluations; bisection costs
/// <c>log2(500) ≈ 9</c> after the bracket, because every evaluation halves the
/// remaining interval instead of adding one point to a grid.
/// </para>
/// <para>
/// The quantity §12 wants this for is a <strong>stability boundary</strong> — the
/// low-mass cut-off of a mass filter or an RF guide, where an ion stops arriving.
/// So "no figure of merit" counts as outside rather than as a failed evaluation:
/// an ion that never reaches the detector is the answer, and a search that treated
/// it as an error would stop exactly at the value it was looking for.
/// </para>
/// <para>
/// <strong>Bisection assumes the predicate flips once across the bracket.</strong>
/// That is true of a cut-off and false of a band, so a band is found by bracketing
/// each edge separately - which is a statement about how to use this, and is why
/// the bracket is declared rather than searched for. A bracket whose ends agree is
/// refused rather than guessed at, naming both ends and what each gave.
/// </para>
/// <para>
/// <strong>The assumption is checked rather than assumed.</strong> Every step of a
/// bisection is consistent with a single crossing by construction, so the search
/// path is no evidence at all - a band and a cut-off produce identical histories.
/// What separates them is a walk outward from the final bracket, at geometrically
/// growing offsets, asking whether the predicate ever flips back. It costs about
/// <c>log2</c> of the range over the bracket width, roughly doubling the search,
/// against a grid that would cost five hundred. Finding a flip is proof; not
/// finding one is not, and the warning says so with the point count either way -
/// REG-2's rule that a check made and passed must be visible, or a reader cannot
/// tell it from a check never made.
/// </para>
/// </remarks>
public static class BoundarySearch
{
    /// <summary>The resolution ACC-6 asks for: one part in five hundred of the scan.</summary>
    public const double AccuracyTarget = 1.0 / 500.0;

    /// <summary>Finds the boundary within a declared bracket.</summary>
    /// <param name="document">The model to vary.</param>
    /// <param name="axis">The parameter and the bracket. The point count is not used.</param>
    /// <param name="evaluate">Produces the figure of merit, or null where there is none.</param>
    /// <param name="threshold">The value the figure crosses.</param>
    /// <param name="sense">Which side of the threshold is inside.</param>
    /// <param name="resolution">
    /// The bracket width to stop at, as a fraction of the range. Defaults to
    /// <see cref="AccuracyTarget"/>.
    /// </param>
    /// <param name="maximumEvaluations">A ceiling, in case the predicate is not monotone.</param>
    /// <returns>The boundary and its bracket.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">
    /// The axis does not name a free parameter, or the bracket's ends are on the same
    /// side of the threshold.
    /// </exception>
    public static BoundaryResult Run(
        ModelDocument document,
        ScanAxis axis,
        Func<CompiledModel, double?> evaluate,
        double threshold,
        BoundarySense sense = BoundarySense.Above,
        double resolution = AccuracyTarget,
        int maximumEvaluations = 60)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution);

        var baseline = Compile(document);
        var parameter = axis.Bind(baseline.Parameters, "/boundary");

        var warnings = new List<ValidityWarning>();
        var evaluations = 0;

        bool Inside(double si)
        {
            evaluations++;

            var figure = Figure(document, axis.Parameter, si, parameter.Value.Dimension, evaluate);

            // No figure is outside, always. A cut-off is precisely the value at
            // which the ion stops arriving, so treating its absence as an error
            // would refuse to search for the thing being searched for.
            return figure is { } value
                && (sense == BoundarySense.Above ? value >= threshold : value <= threshold);
        }

        var lo = axis.From.SiValue;
        var hi = axis.To.SiValue;

        var insideLow = Inside(lo);
        var insideHigh = Inside(hi);

        if (insideLow == insideHigh)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/boundary",
                Constraint =
                    $"both ends of the bracket are {(insideLow ? "inside" : "outside")} the region: "
                    + $"{axis.Parameter} at {lo:G6} and at {hi:G6} SI both give a figure "
                    + $"{(insideLow ? "on the same side of" : "on the same side of")} {threshold:G6}",
                Suggestion = "a bisection needs the two ends to disagree. Run 'einzel scan' over the "
                    + "same range first to see where the figure changes, and bracket that interval. "
                    + "A stability band has two edges and needs one search for each",
            });
        }

        // Kept oriented so `lo` is always the inside end. Which physical direction
        // that is depends on the device - a low-mass cut-off is crossed going up in
        // q, a high-mass one going down - and the search should not care.
        if (!insideLow)
        {
            (lo, hi) = (hi, lo);
        }

        var span = Math.Abs(axis.To.SiValue - axis.From.SiValue);

        while (Math.Abs(hi - lo) / span > resolution && evaluations < maximumEvaluations)
        {
            var middle = 0.5 * (lo + hi);

            if (Inside(middle))
            {
                lo = middle;
            }
            else
            {
                hi = middle;
            }
        }

        var width = Math.Abs(hi - lo);
        var resolved = width / span;
        var met = resolved <= resolution;

        var (probes, secondCrossing) = Confirm(Inside, lo, hi, axis, width);

        if (secondCrossing is { } flip)
        {
            warnings.Add(new ValidityWarning(
                "boundary.multiple-crossings",
                $"the figure crosses the threshold again at {axis.Parameter} = {flip:G6} SI, outside "
                + "the bracket this search converged on. Bisection assumes one crossing and its own "
                + "path can never contradict that, so the located edge is one of several rather than "
                + "the edge of the region. A stability band has two, and a boundary softened by a "
                + "finite observation window - a hold time, a flight-time ceiling - has a ragged "
                + $"strip of them. Run 'einzel scan' across {axis.Parameter} to see the shape, and "
                + "bracket one edge at a time",
                WarningSeverity.ValidityViolation));
        }
        else if (probes > 0)
        {
            warnings.Add(new ValidityWarning(
                "boundary.single-crossing-checked",
                $"{probes} point(s) outside the bracket, at offsets growing geometrically to the "
                + "declared ends, all agreed with a single crossing. That is consistent with a clean "
                + "edge and is not proof of one: a flip narrower than the bracket would be stepped "
                + "over",
                WarningSeverity.Provenance));
        }

        if (!met)
        {
            warnings.Add(new ValidityWarning(
                "boundary.budget-exhausted",
                $"the search stopped after {evaluations} evaluations with the boundary bracketed to "
                + $"1 part in {1.0 / Math.Max(resolved, 1e-12):F0} of the range, short of the "
                + $"1 in {1.0 / resolution:F0} asked for. Bisection halves the bracket every "
                + "evaluation, so a budget this size should be ample - a predicate that is not "
                + "monotone across the bracket is the usual cause, and 'einzel scan' over the same "
                + "range will show it",
                WarningSeverity.ValidityViolation));
        }

        if (resolved > AccuracyTarget)
        {
            warnings.Add(new ValidityWarning(
                "boundary.below-acc6",
                $"the boundary is resolved to 1 part in {1.0 / Math.Max(resolved, 1e-12):F0} of the "
                + $"range, and ACC-6 asks for 1 in {1.0 / AccuracyTarget:F0}. A boundary quoted more "
                + "precisely than its bracket is a boundary quoted more precisely than it was "
                + "measured",
                WarningSeverity.Qualified));
        }

        var midpoint = 0.5 * (lo + hi);
        var dimension = parameter.Value.Dimension;

        var boundary = new Measured(
            Quantity.Si(midpoint, dimension),
            UncertaintyInterval.Between(
                Quantity.Si(Math.Min(lo, hi), dimension),
                Quantity.Si(Math.Max(lo, hi), dimension),
                1.0),
            new Evidence.Search(evaluations, met, width),
            warnings);

        return new BoundaryResult(
            boundary,
            Math.Min(lo, hi),
            Math.Max(lo, hi),
            evaluations,
            resolved,
            met,
            probes,
            secondCrossing,
            warnings);
    }

    /// <summary>
    /// Walks outward from the converged bracket looking for the predicate flipping
    /// back, which is the one thing bisection structurally cannot see.
    /// </summary>
    /// <remarks>
    /// Geometric offsets rather than a grid, for the same reason the search itself
    /// is a bisection: doubling reaches the end of the declared range in about nine
    /// steps either way, where a grid fine enough to be worth running would cost
    /// hundreds. Both directions are walked - a second crossing on the inside of the
    /// located edge means the region is not connected, which matters as much as one
    /// on the outside.
    /// </remarks>
    private static (int Probes, double? SecondCrossing) Confirm(
        Func<double, bool> inside, double lo, double hi, ScanAxis axis, double width)
    {
        if (!(width > 0.0))
        {
            return (0, null);
        }

        var lowEnd = Math.Min(axis.From.SiValue, axis.To.SiValue);
        var highEnd = Math.Max(axis.From.SiValue, axis.To.SiValue);

        var probes = 0;

        // `lo` is the inside end and `hi` the outside one, in whichever physical
        // direction that happens to be, so the walk is expressed in the search's own
        // orientation and works for a low-mass cut-off and a high-mass one without
        // knowing which it has.
        var outward = Math.Sign(hi - lo);

        foreach (var (from, direction, expected) in
                 new[] { (hi, outward, false), (lo, -outward, true) })
        {
            for (var step = width; ; step *= 2.0)
            {
                var at = from + (direction * step);

                if (at < lowEnd || at > highEnd)
                {
                    break;
                }

                probes++;

                if (inside(at) != expected)
                {
                    return (probes, at);
                }
            }
        }

        return (probes, null);
    }

    private static double? Figure(
        ModelDocument document,
        string parameter,
        double si,
        Dimension dimension,
        Func<CompiledModel, double?> evaluate)
    {
        var overrides = new Dictionary<string, Quantity>(StringComparer.Ordinal)
        {
            [parameter] = Quantity.Si(si, dimension),
        };

        try
        {
            var validation = ModelValidator.Validate(document, overrides);

            // A value the model refuses is outside the region, not an error. A
            // bracket that runs past a declared bound is a legitimate way to ask
            // where a design stops being buildable.
            return validation.IsValid ? evaluate(validation.Model!) : null;
        }
        catch (EinzelException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document, null);

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0]);
        }

        return validation.Model!;
    }
}
