using Einzel.Core.Units;

namespace Einzel.Core.Results;

/// <summary>
/// The uncertainty attached to a reported value, as an interval at a stated
/// confidence level.
/// </summary>
/// <remarks>
/// <para>
/// Required by GRD-1. There is deliberately no "exact" or "unknown" interval:
/// an analytic result still has a numerical error bound, and a value whose
/// uncertainty genuinely cannot be characterised should not be reported at all.
/// A zero-width interval is representable, but only by stating it, which is a
/// claim someone can be held to.
/// </para>
/// <para>
/// The interval is absolute rather than fractional, and stored in SI, so it
/// composes with <see cref="Quantity"/> arithmetic without a second conversion.
/// </para>
/// </remarks>
public readonly record struct UncertaintyInterval
{
    private UncertaintyInterval(double lowerSi, double upperSi, double confidenceLevel)
    {
        LowerSi = lowerSi;
        UpperSi = upperSi;
        ConfidenceLevel = confidenceLevel;
    }

    /// <summary>Lower bound of the interval, in SI.</summary>
    public double LowerSi { get; }

    /// <summary>Upper bound of the interval, in SI.</summary>
    public double UpperSi { get; }

    /// <summary>
    /// The confidence level the interval is quoted at, as a fraction. ACC-5
    /// specifies 0.95 for Class S transmission.
    /// </summary>
    public double ConfidenceLevel { get; }

    /// <summary>Width of the interval, in SI.</summary>
    public double WidthSi => UpperSi - LowerSi;

    /// <summary>
    /// A symmetric interval, value plus or minus a half-width.
    /// </summary>
    /// <param name="value">The central value.</param>
    /// <param name="halfWidth">The half-width; must be non-negative and of the same dimension.</param>
    /// <param name="confidenceLevel">Confidence level as a fraction, in (0, 1].</param>
    /// <returns>The interval.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The half-width is negative, or the confidence level is outside (0, 1].
    /// </exception>
    public static UncertaintyInterval Symmetric(Quantity value, Quantity halfWidth, double confidenceLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(halfWidth.SiValue);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confidenceLevel, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(confidenceLevel, 1.0);

        // Dimension agreement is enforced by the subtraction.
        var lower = value - halfWidth;
        var upper = value + halfWidth;
        return new UncertaintyInterval(lower.SiValue, upper.SiValue, confidenceLevel);
    }

    /// <summary>An explicit, possibly asymmetric interval.</summary>
    /// <param name="lower">Lower bound.</param>
    /// <param name="upper">Upper bound; must not be below <paramref name="lower"/>.</param>
    /// <param name="confidenceLevel">Confidence level as a fraction, in (0, 1].</param>
    /// <returns>The interval.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The bounds are inverted, or the confidence level is outside (0, 1].
    /// </exception>
    public static UncertaintyInterval Between(Quantity lower, Quantity upper, double confidenceLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confidenceLevel, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(confidenceLevel, 1.0);

        // Dimension agreement is enforced by the comparison.
        if (upper < lower)
        {
            throw new ArgumentOutOfRangeException(
                nameof(upper), upper.SiValue, "upper bound is below the lower bound");
        }

        return new UncertaintyInterval(lower.SiValue, upper.SiValue, confidenceLevel);
    }
}
