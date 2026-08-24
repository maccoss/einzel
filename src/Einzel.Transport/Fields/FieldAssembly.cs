using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Transport.Fields;

/// <summary>
/// Several field elements acting at once, summed.
/// </summary>
/// <remarks>
/// <para>
/// Superposition is exact for electrostatics, so summing element fields and
/// element potentials is the whole implementation. Spec section 10 leans on the
/// same property much harder: solving once per electrode at unit potential turns
/// voltage optimisation from a solve-per-iteration problem into arithmetic.
/// </para>
/// <para>
/// The two structural queries do not sum, and each is handled conservatively. A
/// run is field-free only where every element is field-free, so the shortest run
/// wins. Discontinuities are trickier — see
/// <see cref="SignedDistanceToDiscontinuity"/>.
/// </para>
/// </remarks>
public sealed class SuperposedField : IElectrostaticField
{
    private readonly IElectrostaticField[] _elements;

    /// <summary>Creates a superposition.</summary>
    /// <param name="elements">The elements to sum.</param>
    /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
    public SuperposedField(IEnumerable<IElectrostaticField> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _elements = [.. elements];
    }

    /// <summary>The elements being summed.</summary>
    public IReadOnlyList<IElectrostaticField> Elements => _elements;

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var total = Vec3.Zero;

        foreach (var element in _elements)
        {
            total += element.ElectricFieldAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var total = 0.0;

        foreach (var element in _elements)
        {
            total += element.PotentialAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A run is field-free only where every element is, so the shortest run
    /// governs. Returning less than the true run length is always safe; returning
    /// more would advance an ion in a straight line through a region where it
    /// should have been accelerating.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var shortest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            shortest = Math.Min(shortest, element.FieldFreeRunLength(in position, in direction));

            if (shortest <= 0.0)
            {
                return 0.0;
            }
        }

        return shortest;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The magnitude is the distance to the nearest declared discontinuity; the
    /// sign is the product of the element signs, so crossing any single boundary
    /// flips it and the integrator localises the crossing by bisection.
    /// </para>
    /// <para>
    /// The known limit: a step crossing two boundaries at once returns the sign to
    /// where it started and the crossing is missed. That is benign rather than
    /// silent — the step-size controller sees the resulting error and refines
    /// until the crossings separate — but it means the exact-landing guarantee
    /// holds for one boundary per step, not two. Nothing in schema v0.1 can build
    /// such a geometry; a stacked-ring mirror could, and the fix then is to track
    /// element regions rather than to sum signs.
    /// </para>
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var nearest = double.PositiveInfinity;
        var sign = 1;

        foreach (var element in _elements)
        {
            var distance = element.SignedDistanceToDiscontinuity(in position);

            if (!double.IsFinite(distance))
            {
                continue;
            }

            nearest = Math.Min(nearest, Math.Abs(distance));
            sign *= distance < 0.0 ? -1 : 1;
        }

        return double.IsPositiveInfinity(nearest) ? double.PositiveInfinity : nearest * sign;
    }
}

/// <summary>
/// Builds engine field objects from a validated model.
/// </summary>
/// <remarks>
/// The seam between the declarative document and the engine. It lives here
/// rather than in Einzel.Core because Core is the innermost assembly and cannot
/// reference the field types built from it.
/// </remarks>
public static class FieldAssembly
{
    /// <summary>Builds the field described by a compiled model.</summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The assembled field.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public static IElectrostaticField Build(CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var elements = new List<IElectrostaticField>(model.Fields.Count);

        foreach (var element in model.Fields)
        {
            switch (element.Kind)
            {
                case CompiledFieldKind.FieldFree:
                    // Contributes nothing to the sum, and adding it would only make
                    // FieldFreeRunLength do redundant work.
                    break;

                case CompiledFieldKind.Uniform:
                    elements.Add(UniformField.Create(element.Field));
                    break;

                case CompiledFieldKind.HalfSpaceUniform:
                    elements.Add(HalfSpaceUniformField.Create(
                        element.PlanePoint,
                        element.InwardNormal,
                        Quantity.Si(element.PotentialGradientSi, Dimension.ElectricField)));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(model), element.Kind, "unhandled field element kind");
            }
        }

        return elements.Count switch
        {
            0 => FieldFreeSpace.Instance,
            1 => elements[0],
            _ => new SuperposedField(elements),
        };
    }

    /// <summary>Builds the ion species described by a compiled model.</summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The species.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public static IonSpecies BuildSpecies(CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return IonSpecies.Create(
            Quantity.Si(model.MassSi, Dimension.MassDimension),
            Quantity.Si(model.ChargeSi, Dimension.Charge));
    }
}
