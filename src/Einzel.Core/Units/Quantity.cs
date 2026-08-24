using Einzel.Core.Errors;

namespace Einzel.Core.Units;

/// <summary>
/// A physical quantity: a double-precision magnitude held in SI, plus its
/// <see cref="Dimension"/>.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 9: internally SI, double precision, without exception. There is
/// deliberately no constructor taking a bare number without either a unit symbol
/// or an explicit dimension, because unit ambiguity is the commonest source of
/// silent wrongness and an agent building a model from prose is the actor most
/// likely to introduce it.
/// </para>
/// <para>
/// Storing SI internally rather than value-plus-unit means a conversion happens
/// once, at the boundary, instead of at every arithmetic step. That matters for
/// ACC-1: repeated multiplication by conversion factors is a systematic error
/// source in a 1 ppm budget.
/// </para>
/// </remarks>
public readonly struct Quantity : IEquatable<Quantity>, IComparable<Quantity>
{
    private Quantity(double siValue, Dimension dimension)
    {
        SiValue = siValue;
        Dimension = dimension;
    }

    /// <summary>The magnitude, in coherent SI units for <see cref="Dimension"/>.</summary>
    public double SiValue { get; }

    /// <summary>The physical dimension.</summary>
    public Dimension Dimension { get; }

    /// <summary>Constructs a quantity from a magnitude already expressed in SI.</summary>
    /// <param name="siValue">The magnitude in coherent SI units.</param>
    /// <param name="dimension">The physical dimension.</param>
    /// <returns>The quantity.</returns>
    public static Quantity Si(double siValue, Dimension dimension) => new(siValue, dimension);

    /// <summary>A dimensionless number: a ratio, efficiency, or angle in radians.</summary>
    /// <param name="value">The magnitude.</param>
    /// <returns>The quantity.</returns>
    public static Quantity Number(double value) => new(value, Dimension.Dimensionless);

    /// <summary>Zero, in the given dimension.</summary>
    /// <param name="dimension">The physical dimension.</param>
    /// <returns>A zero quantity.</returns>
    public static Quantity Zero(Dimension dimension) => new(0.0, dimension);

    /// <summary>
    /// Constructs a quantity from a magnitude and a unit symbol, converting to
    /// SI. This is the boundary conversion referred to in spec section 9.
    /// </summary>
    /// <param name="value">The magnitude, expressed in <paramref name="unit"/>.</param>
    /// <param name="unit">A unit symbol known to <see cref="UnitRegistry"/>.</param>
    /// <returns>The quantity, held in SI.</returns>
    /// <exception cref="EinzelException">
    /// <see cref="ErrorCodes.UnitsUnknown"/> when the symbol is not recognised.
    /// </exception>
    public static Quantity From(double value, string unit)
    {
        var definition = UnitRegistry.Resolve(unit);
        return new Quantity(value * definition.SiFactor, definition.Dimension);
    }

    /// <summary>
    /// Converts out of SI into a named unit. Callers must name the unit they
    /// want; there is no implicit "the number".
    /// </summary>
    /// <param name="unit">A unit symbol known to <see cref="UnitRegistry"/>.</param>
    /// <returns>The magnitude expressed in <paramref name="unit"/>.</returns>
    /// <exception cref="EinzelException">
    /// <see cref="ErrorCodes.UnitsUnknown"/> when the symbol is not recognised, or
    /// <see cref="ErrorCodes.UnitsIncompatible"/> when it has the wrong dimension.
    /// </exception>
    public double In(string unit)
    {
        var definition = UnitRegistry.Resolve(unit);

        if (definition.Dimension != Dimension)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsIncompatible,
                Path = "/",
                Constraint =
                    $"cannot express a quantity of dimension {Dimension} in '{unit}', "
                    + $"which has dimension {definition.Dimension}",
                Observed = new ObservedValue(SiValue, Dimension.ToString()),
                Suggestion = $"use a unit of dimension {Dimension}",
            });
        }

        return SiValue / definition.SiFactor;
    }

    /// <summary>Adds two quantities of the same dimension.</summary>
    /// <param name="left">Left addend.</param>
    /// <param name="right">Right addend.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="EinzelException">The dimensions differ.</exception>
    public static Quantity operator +(Quantity left, Quantity right)
    {
        RequireSameDimension(left, right, "add");
        return new Quantity(left.SiValue + right.SiValue, left.Dimension);
    }

    /// <summary>Subtracts two quantities of the same dimension.</summary>
    /// <param name="left">Minuend.</param>
    /// <param name="right">Subtrahend.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="EinzelException">The dimensions differ.</exception>
    public static Quantity operator -(Quantity left, Quantity right)
    {
        RequireSameDimension(left, right, "subtract");
        return new Quantity(left.SiValue - right.SiValue, left.Dimension);
    }

    /// <summary>Negates a quantity.</summary>
    /// <param name="value">The quantity.</param>
    /// <returns>The negation.</returns>
    public static Quantity operator -(Quantity value) => new(-value.SiValue, value.Dimension);

    /// <summary>Multiplies two quantities, combining dimensions.</summary>
    /// <param name="left">Left factor.</param>
    /// <param name="right">Right factor.</param>
    /// <returns>The product.</returns>
    public static Quantity operator *(Quantity left, Quantity right) =>
        new(left.SiValue * right.SiValue, left.Dimension * right.Dimension);

    /// <summary>Divides two quantities, combining dimensions.</summary>
    /// <param name="left">Numerator.</param>
    /// <param name="right">Denominator.</param>
    /// <returns>The quotient.</returns>
    public static Quantity operator /(Quantity left, Quantity right) =>
        new(left.SiValue / right.SiValue, left.Dimension / right.Dimension);

    /// <summary>Scales a quantity by a pure number.</summary>
    /// <param name="left">The quantity.</param>
    /// <param name="right">The scale factor.</param>
    /// <returns>The scaled quantity.</returns>
    public static Quantity operator *(Quantity left, double right) =>
        new(left.SiValue * right, left.Dimension);

    /// <summary>Scales a quantity by a pure number.</summary>
    /// <param name="left">The scale factor.</param>
    /// <param name="right">The quantity.</param>
    /// <returns>The scaled quantity.</returns>
    public static Quantity operator *(double left, Quantity right) =>
        new(left * right.SiValue, right.Dimension);

    /// <summary>Divides a quantity by a pure number.</summary>
    /// <param name="left">The quantity.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The scaled quantity.</returns>
    public static Quantity operator /(Quantity left, double right) =>
        new(left.SiValue / right, left.Dimension);

    /// <summary>Named alternate for the addition operator.</summary>
    /// <param name="left">Left addend.</param>
    /// <param name="right">Right addend.</param>
    /// <returns>The sum.</returns>
    public static Quantity Add(Quantity left, Quantity right) => left + right;

    /// <summary>Named alternate for the subtraction operator.</summary>
    /// <param name="left">Minuend.</param>
    /// <param name="right">Subtrahend.</param>
    /// <returns>The difference.</returns>
    public static Quantity Subtract(Quantity left, Quantity right) => left - right;

    /// <summary>Named alternate for the multiplication operator.</summary>
    /// <param name="left">Left factor.</param>
    /// <param name="right">Right factor.</param>
    /// <returns>The product.</returns>
    public static Quantity Multiply(Quantity left, Quantity right) => left * right;

    /// <summary>Named alternate for the division operator.</summary>
    /// <param name="left">Numerator.</param>
    /// <param name="right">Denominator.</param>
    /// <returns>The quotient.</returns>
    public static Quantity Divide(Quantity left, Quantity right) => left / right;

    /// <summary>Named alternate for the negation operator.</summary>
    /// <param name="value">The quantity.</param>
    /// <returns>The negation.</returns>
    public static Quantity Negate(Quantity value) => -value;

    /// <summary>Raises a quantity to an integer power.</summary>
    /// <param name="value">The quantity.</param>
    /// <param name="exponent">The power.</param>
    /// <returns>The raised quantity.</returns>
    public static Quantity Pow(Quantity value, int exponent) =>
        new(Math.Pow(value.SiValue, exponent), Dimension.Pow(value.Dimension, exponent));

    /// <summary>The absolute value, preserving dimension.</summary>
    /// <param name="value">The quantity.</param>
    /// <returns>The absolute value.</returns>
    public static Quantity Abs(Quantity value) => new(Math.Abs(value.SiValue), value.Dimension);

    private static void RequireSameDimension(Quantity left, Quantity right, string operation)
    {
        if (left.Dimension == right.Dimension)
        {
            return;
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.UnitsIncompatible,
            Path = "/",
            Constraint =
                $"cannot {operation} quantities of dimension {left.Dimension} and {right.Dimension}",
            Observed = new ObservedValue(right.SiValue, right.Dimension.ToString()),
            Suggestion = $"supply the right-hand operand with dimension {left.Dimension}",
        });
    }

    /// <summary>Compares two quantities of the same dimension.</summary>
    /// <param name="other">The quantity to compare against.</param>
    /// <returns>A signed ordering value.</returns>
    /// <exception cref="EinzelException">The dimensions differ.</exception>
    public int CompareTo(Quantity other)
    {
        RequireSameDimension(this, other, "compare");
        return SiValue.CompareTo(other.SiValue);
    }

    /// <summary>Determines whether one quantity is less than another.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when strictly less.</returns>
    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one quantity is greater than another.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when strictly greater.</returns>
    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one quantity is less than or equal to another.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when less or equal.</returns>
    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one quantity is greater than or equal to another.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when greater or equal.</returns>
    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Exact equality of SI magnitude and dimension. Physics code should compare
    /// with an explicit tolerance instead; this exists for dictionary keys and
    /// round-trip assertions.
    /// </summary>
    /// <param name="other">The quantity to compare against.</param>
    /// <returns><see langword="true"/> when magnitude and dimension both match.</returns>
    public bool Equals(Quantity other) =>
        SiValue.Equals(other.SiValue) && Dimension == other.Dimension;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Quantity other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(SiValue, Dimension);

    /// <summary>Determines whether two quantities are exactly equal.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(Quantity left, Quantity right) => left.Equals(right);

    /// <summary>Determines whether two quantities differ.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(Quantity left, Quantity right) => !left.Equals(right);

    /// <summary>Renders the quantity in SI base units.</summary>
    /// <returns>The magnitude followed by the SI dimension symbols.</returns>
    public override string ToString() =>
        Dimension.IsDimensionless
            ? SiValue.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)
            : $"{SiValue.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)} {Dimension}";
}
