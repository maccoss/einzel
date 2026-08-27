namespace Einzel.Core.Numerics;

/// <summary>
/// A running sum using Neumaier compensation, which recovers the low-order bits
/// that ordinary floating-point addition discards.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 8: "Flight time accumulates with Neumaier compensation." The
/// reason is specific to this problem. A 192 microsecond flight integrated in
/// steps of a few picoseconds is order 10^5 additions of a small increment onto
/// a growing total. Naive summation loses roughly one bit per doubling of the
/// term count, which at 10^5 terms costs about 17 bits — comfortably inside the
/// ACC-1 budget of 1 ppm, and entirely avoidable.
/// </para>
/// <para>
/// Neumaier rather than plain Kahan: Kahan loses the correction when the
/// incoming term is larger in magnitude than the running total, which happens on
/// the very first step and again after any analytic drift advance jumps the
/// total. Neumaier handles both orderings.
/// </para>
/// <para>
/// A mutable struct, deliberately. It lives in the integrator's inner loop, and
/// a class here would allocate per trajectory.
/// </para>
/// </remarks>
public struct CompensatedSum : IEquatable<CompensatedSum>
{
    private double _sum;
    private double _compensation;

    /// <summary>Adds a term to the running sum.</summary>
    /// <param name="value">The term to add.</param>
    public void Add(double value)
    {
        var updated = _sum + value;

        // Accumulate whichever operand's low-order bits were lost.
        _compensation += Math.Abs(_sum) >= Math.Abs(value)
            ? (_sum - updated) + value
            : (value - updated) + _sum;

        _sum = updated;
    }

    /// <summary>
    /// The sum, with the accumulated correction applied. Reading this does not
    /// disturb the running state, so it is safe to sample mid-loop.
    /// </summary>
    public readonly double Total => _sum + _compensation;

    /// <summary>
    /// The correction accumulated so far. Its magnitude is the amount of
    /// precision naive summation would have thrown away, which makes it a useful
    /// diagnostic when a timing budget is in question.
    /// </summary>
    public readonly double Compensation => _compensation;

    /// <inheritdoc/>
    public readonly bool Equals(CompensatedSum other) =>
        _sum.Equals(other._sum) && _compensation.Equals(other._compensation);

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is CompensatedSum other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() => HashCode.Combine(_sum, _compensation);

    /// <summary>Determines whether two running sums hold identical state.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(CompensatedSum left, CompensatedSum right) => left.Equals(right);

    /// <summary>Determines whether two running sums differ.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(CompensatedSum left, CompensatedSum right) => !left.Equals(right);

    /// <inheritdoc/>
    public override readonly string ToString() =>
        Total.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
}
