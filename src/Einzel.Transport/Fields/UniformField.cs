using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Transport.Fields;

/// <summary>
/// A uniform electric field filling all space.
/// </summary>
/// <remarks>
/// The parallel-plate case of the analytic test tier (spec section 19). Motion is
/// exactly parabolic, so the closed form is available to arbitrary precision and
/// any deviation is integrator error.
/// </remarks>
public sealed class UniformField : IElectrostaticField
{
    /// <summary>Creates a uniform field.</summary>
    /// <param name="field">The field vector, of electric-field dimension.</param>
    /// <returns>The field.</returns>
    /// <exception cref="Core.Errors.EinzelException">The quantity has the wrong dimension.</exception>
    public static UniformField Create(Vec3 field) => new(field);

    private UniformField(Vec3 fieldSi) => FieldSi = fieldSi;

    /// <summary>Creates a uniform field from a magnitude, a unit, and a direction.</summary>
    /// <param name="magnitude">The field magnitude; must be of electric-field dimension.</param>
    /// <param name="direction">A direction vector; normalised internally.</param>
    /// <returns>The field.</returns>
    /// <exception cref="Core.Errors.EinzelException">The magnitude has the wrong dimension.</exception>
    public static UniformField Create(Quantity magnitude, Vec3 direction) =>
        new(direction.Normalized() * magnitude.In("V/m"));

    /// <summary>The field vector, in volts per metre.</summary>
    public Vec3 FieldSi { get; }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position) => FieldSi;

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) => -Vec3.Dot(FieldSi, position);
}
