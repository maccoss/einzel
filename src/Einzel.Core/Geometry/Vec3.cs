namespace Einzel.Core.Geometry;

/// <summary>
/// A double-precision vector in three dimensions, in SI units.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="System.Numerics.Vector3"/>, which is single
/// precision. Float carries about seven significant decimal digits; ACC-1 asks
/// for flight-time error below 1 ppm over a path of several metres, which needs
/// roughly ten. A float position would lose the budget before the integrator ran
/// a single step.
/// </para>
/// <para>
/// A readonly struct so that trajectory state stays on the stack and the inner
/// loop allocates nothing (spec section 11, and the GC-pause risk in section 22).
/// </para>
/// </remarks>
/// <param name="X">The x component.</param>
/// <param name="Y">The y component.</param>
/// <param name="Z">The z component.</param>
public readonly record struct Vec3(double X, double Y, double Z)
{
    /// <summary>The zero vector.</summary>
    public static Vec3 Zero => default;

    /// <summary>The unit vector along x.</summary>
    public static Vec3 UnitX => new(1.0, 0.0, 0.0);

    /// <summary>The unit vector along y.</summary>
    public static Vec3 UnitY => new(0.0, 1.0, 0.0);

    /// <summary>The unit vector along z.</summary>
    public static Vec3 UnitZ => new(0.0, 0.0, 1.0);

    /// <summary>The squared Euclidean length. Avoids a square root where the ordering suffices.</summary>
    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    /// <summary>The Euclidean length.</summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Adds two vectors.</summary>
    /// <param name="left">Left addend.</param>
    /// <param name="right">Right addend.</param>
    /// <returns>The sum.</returns>
    public static Vec3 operator +(Vec3 left, Vec3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary>Subtracts two vectors.</summary>
    /// <param name="left">Minuend.</param>
    /// <param name="right">Subtrahend.</param>
    /// <returns>The difference.</returns>
    public static Vec3 operator -(Vec3 left, Vec3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Negates a vector.</summary>
    /// <param name="value">The vector.</param>
    /// <returns>The negation.</returns>
    public static Vec3 operator -(Vec3 value) => new(-value.X, -value.Y, -value.Z);

    /// <summary>Scales a vector.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The scale factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec3 operator *(Vec3 left, double right) =>
        new(left.X * right, left.Y * right, left.Z * right);

    /// <summary>Scales a vector.</summary>
    /// <param name="left">The scale factor.</param>
    /// <param name="right">The vector.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec3 operator *(double left, Vec3 right) =>
        new(left * right.X, left * right.Y, left * right.Z);

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec3 operator /(Vec3 left, double right) =>
        new(left.X / right, left.Y / right, left.Z / right);

    /// <summary>Named alternate for the addition operator.</summary>
    /// <param name="left">Left addend.</param>
    /// <param name="right">Right addend.</param>
    /// <returns>The sum.</returns>
    public static Vec3 Add(Vec3 left, Vec3 right) => left + right;

    /// <summary>Named alternate for the subtraction operator.</summary>
    /// <param name="left">Minuend.</param>
    /// <param name="right">Subtrahend.</param>
    /// <returns>The difference.</returns>
    public static Vec3 Subtract(Vec3 left, Vec3 right) => left - right;

    /// <summary>Named alternate for the scaling operator.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The scale factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec3 Multiply(Vec3 left, double right) => left * right;

    /// <summary>Named alternate for the division operator.</summary>
    /// <param name="left">The vector.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec3 Divide(Vec3 left, double right) => left / right;

    /// <summary>Named alternate for the negation operator.</summary>
    /// <param name="value">The vector.</param>
    /// <returns>The negation.</returns>
    public static Vec3 Negate(Vec3 value) => -value;

    /// <summary>The scalar product.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The dot product.</returns>
    public static double Dot(Vec3 left, Vec3 right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    /// <summary>The vector product.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>The cross product.</returns>
    public static Vec3 Cross(Vec3 left, Vec3 right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    /// <summary>Returns the unit vector in the same direction.</summary>
    /// <returns>The normalised vector.</returns>
    /// <exception cref="InvalidOperationException">The vector has zero length.</exception>
    public Vec3 Normalized()
    {
        var length = Length;

        if (length == 0.0)
        {
            throw new InvalidOperationException("cannot normalise a zero-length vector");
        }

        return this / length;
    }
}
