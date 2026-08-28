using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A superposition in which at least one member varies with time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SuperposedField"/> implements only <see cref="IElectrostaticField"/>,
/// so summing a driven element with anything else produces a field that satisfies
/// the static interface and nothing more. A driven member answers that interface
/// with its value at <c>t = 0</c>, so the sum silently becomes a <em>snapshot of the
/// RF at the top of its cycle</em> — a static field that exists for no length of
/// time, presented as the instrument.
/// </para>
/// <para>
/// That is the same failure the diffusive mode was found stepping a density through,
/// and it is worth naming as a class rather than a case: <strong>a time-varying
/// quantity reached through a time-free interface does not fail, it answers at an
/// arbitrary instant.</strong> There is no exception, no NaN, and nothing in the
/// result to distinguish it. The fix is structural — when any member is driven the
/// sum is driven, and the composition is chosen by what it contains rather than by
/// what the caller happens to ask for.
/// </para>
/// <para>
/// Static members are evaluated by their own time-free interface and contribute the
/// same value at every instant, which is what static means. Nothing here needs to
/// know which is which beyond the type test.
/// </para>
/// </remarks>
public sealed class DrivenSuperposedField : ITimeVaryingField, IConductorBounded
{
    private readonly IElectrostaticField[] _elements;

    /// <summary>Creates a driven superposition.</summary>
    /// <param name="elements">The elements to sum, at least one of them driven.</param>
    /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// No member varies with time, in which case <see cref="SuperposedField"/> is the
    /// right type and this one would claim a drive the field does not have.
    /// </exception>
    public DrivenSuperposedField(IEnumerable<IElectrostaticField> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        _elements = [.. elements];

        if (!Array.Exists(_elements, e => e is ITimeVaryingField))
        {
            throw new ArgumentException(
                "no member of this superposition varies with time, so it should be a "
                + "SuperposedField - a field that reports a drive it does not have would make "
                + "an integrator cap its step for nothing and a renderer claim an animation",
                nameof(elements));
        }
    }

    /// <summary>The elements being summed.</summary>
    public IReadOnlyList<IElectrostaticField> Elements => _elements;

    /// <inheritdoc/>
    /// <remarks>
    /// The shortest period any member declares: the sum carries information on the
    /// fastest timescale present in it, so a step long enough to skip that member's
    /// cycle skips it in the sum too.
    /// </remarks>
    public double ShortestPeriodSeconds
    {
        get
        {
            var shortest = double.PositiveInfinity;

            foreach (var element in _elements)
            {
                if (element is ITimeVaryingField driven)
                {
                    shortest = Math.Min(shortest, driven.ShortestPeriodSeconds);
                }
            }

            return shortest;
        }
    }

    /// <inheritdoc/>
    public double ResolutionLength
    {
        get
        {
            var shortest = double.PositiveInfinity;

            foreach (var element in _elements)
            {
                shortest = Math.Min(shortest, element.ResolutionLength);
            }

            return shortest;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The earliest switch any member declares. A sequenced element and a
    /// continuously driven one can coexist, and the continuous one returns infinity,
    /// so the minimum is the answer without a special case.
    /// </remarks>
    public double NextSwitchAfter(double timeSeconds)
    {
        var next = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            if (element is ITimeVaryingField driven)
            {
                next = Math.Min(next, driven.NextSwitchAfter(timeSeconds));
            }
        }

        return next;
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        var total = Vec3.Zero;

        foreach (var element in _elements)
        {
            total += element is ITimeVaryingField driven
                ? driven.ElectricFieldAt(in position, timeSeconds)
                : element.ElectricFieldAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds)
    {
        var total = 0.0;

        foreach (var element in _elements)
        {
            total += element is ITimeVaryingField driven
                ? driven.PotentialAt(in position, timeSeconds)
                : element.PotentialAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>The value at the start of the cycle, as every driven field reports.</remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>The value at the start of the cycle, as every driven field reports.</remarks>
    public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>
    /// Zero, always. A driven field's run length is the distance over which it is
    /// field-free, and it cannot be known in advance of the time the ion will take to
    /// cover it - which is the quantity the run length exists to help compute.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <inheritdoc/>
    /// <remarks>
    /// The nearest conductor over every member that has one. Potentials superpose;
    /// solid bodies do not, they coexist - so this is a union rather than a sum, the
    /// same reasoning as in <see cref="SuperposedField"/>.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position)
    {
        var nearest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            if (element is IConductorBounded bounded)
            {
                nearest = Math.Min(nearest, bounded.SignedDistanceToConductor(in position));
            }
        }

        return nearest;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The same sign-product tracking <see cref="SuperposedField"/> uses, and for
    /// the same reason: the magnitude is the distance to the nearest declared jump
    /// and the sign says which side of the whole arrangement the point is on, so a
    /// step that crosses two surfaces is correctly seen as crossing neither.
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

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position)
    {
        foreach (var element in _elements)
        {
            if (element is IConductorBounded bounded
                && bounded.SignedDistanceToConductor(in position) <= 0.0)
            {
                return bounded.ConductorAt(in position);
            }
        }

        return null;
    }
}
