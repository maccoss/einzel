using Einzel.Core.Geometry;
using Einzel.Core.Model;

namespace Einzel.Fields.Magnetic;

/// <summary>A uniform magnetic flux density filling all space.</summary>
/// <remarks>
/// The magnetic counterpart of <see cref="UniformField"/>, and the field every
/// closed form of charged-particle motion in a magnet is written for: cyclotron motion,
/// E x B drift, a Wien filter, a sector. Bounded by <see cref="BoundedMagneticField"/> it is
/// the hard-edge (sharp cut-off) magnet of first-order beam optics.
/// </remarks>
public sealed class UniformMagneticField : IMagneticField
{
    private readonly Vec3 _fluxDensity;

    /// <summary>Creates the field.</summary>
    /// <param name="fluxDensityTesla">B, in tesla. Every component must be finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component is not finite.</exception>
    public UniformMagneticField(Vec3 fluxDensityTesla)
    {
        if (!double.IsFinite(fluxDensityTesla.X) || !double.IsFinite(fluxDensityTesla.Y)
            || !double.IsFinite(fluxDensityTesla.Z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fluxDensityTesla), "a flux density must be finite in every component");
        }

        _fluxDensity = fluxDensityTesla;
    }

    /// <summary>B, in tesla.</summary>
    public Vec3 FluxDensity => _fluxDensity;

    /// <inheritdoc />
    public Vec3 MagneticFieldAt(in Vec3 position) => _fluxDensity;

    /// <inheritdoc />
    public double MaximumFluxDensity => _fluxDensity.Length;

    /// <inheritdoc />
    /// <remarks>Zero everywhere unless B is zero, because an unbounded field acts everywhere.</remarks>
    public double MagnetFreeRunLength(in Vec3 position, in Vec3 direction) =>
        _fluxDensity.LengthSquared > 0.0 ? 0.0 : double.PositiveInfinity;
}

/// <summary>A magnetic field that is silent outside a box: a hard-edge magnet.</summary>
/// <remarks>
/// <para>
/// <b>A hard edge, deliberately, and no fringe.</b> The electric <see cref="FieldRegion"/>
/// may ramp an element up over a fringe because scaling a potential by a ramp still leaves a
/// field that is the gradient of something. Scaling B by a ramp does not leave a field a
/// magnet can make: across a fringe whose ramp runs perpendicular to B the result has a
/// curl, which in vacuum needs a current that is not there. The sharp cut-off is the
/// standard first-order model of a sector's edge (the field integral is right, the fringe's
/// focusing is not), so the validator refuses a fringe on a magnetic element rather than
/// accepting one that would be quietly unphysical.
/// </para>
/// <para>
/// The box face is a discontinuity in B, reported through
/// <see cref="IMagneticField.SignedDistanceToMagneticDiscontinuity"/> so the integrator lands
/// on it exactly as it lands on an electric region's face.
/// </para>
/// </remarks>
public sealed class BoundedMagneticField : IMagneticField
{
    private readonly IMagneticField _inner;

    /// <summary>Bounds a magnetic field to a region.</summary>
    /// <param name="inner">The field inside the region.</param>
    /// <param name="region">The box. Its fringe must be zero.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The region declares a fringe.</exception>
    public BoundedMagneticField(IMagneticField inner, FieldRegion region)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(region);

        if (region.FringeSi != 0.0)
        {
            throw new ArgumentException(
                "a magnetic region is a hard edge; a ramped B is not a field a magnet can make",
                nameof(region));
        }

        _inner = inner;
        Region = region;
    }

    /// <summary>The box outside which B is zero.</summary>
    public FieldRegion Region { get; }

    /// <inheritdoc />
    public Vec3 MagneticFieldAt(in Vec3 position) =>
        Region.Contains(in position) ? _inner.MagneticFieldAt(in position) : Vec3.Zero;

    /// <inheritdoc />
    /// <remarks>Inside or outside by where <paramref name="side"/> is, not where the point is.</remarks>
    public Vec3 MagneticFieldAt(in Vec3 position, in Vec3 side) =>
        Region.Contains(in side) ? _inner.MagneticFieldAt(in position, in side) : Vec3.Zero;

    /// <inheritdoc />
    public double MaximumFluxDensity => _inner.MaximumFluxDensity;

    /// <inheritdoc />
    /// <remarks>
    /// Inside the box, the inner field's answer (zero for a uniform field). Outside, the
    /// distance along the ray to where it enters the box, by the slab method - exact for a
    /// straight line, which is the only path this is asked about.
    /// </remarks>
    public double MagnetFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        // A box holding no field acts nowhere, so it bounds no drift and lands nothing:
        // a zero flux density flies exactly as no magnet at all.
        if (_inner.MaximumFluxDensity == 0.0)
        {
            return double.PositiveInfinity;
        }

        if (Region.Contains(in position))
        {
            return _inner.MagnetFreeRunLength(in position, in direction);
        }

        var entry = 0.0;
        var exit = double.PositiveInfinity;

        if (!Slab(position.X, direction.X, Region.MinX, Region.MaxX, ref entry, ref exit)
            || !Slab(position.Y, direction.Y, Region.MinY, Region.MaxY, ref entry, ref exit)
            || !Slab(position.Z, direction.Z, Region.MinZ, Region.MaxZ, ref entry, ref exit)
            || entry > exit)
        {
            return double.PositiveInfinity;
        }

        return entry;
    }

    /// <inheritdoc />
    public double MagnetHalfwayLength(in Vec3 position, in Vec3 direction)
    {
        if (_inner.MaximumFluxDensity == 0.0 || Region.Contains(in position))
        {
            return double.PositiveInfinity;
        }

        var entry = 0.0;
        var exit = double.PositiveInfinity;

        if (!Slab(position.X, direction.X, Region.MinX, Region.MaxX, ref entry, ref exit)
            || !Slab(position.Y, direction.Y, Region.MinY, Region.MaxY, ref entry, ref exit)
            || !Slab(position.Z, direction.Z, Region.MinZ, Region.MaxZ, ref entry, ref exit)
            || entry > exit)
        {
            return double.PositiveInfinity;
        }

        return 0.5 * (entry + exit);
    }

    /// <inheritdoc />
    public double SignedDistanceToMagneticDiscontinuity(in Vec3 position)
    {
        // Zero inside and zero outside is no jump.
        if (_inner.MaximumFluxDensity == 0.0)
        {
            return double.PositiveInfinity;
        }

        var toBoundary = Region.SignedDistance(in position);

        if (!Region.Contains(in position))
        {
            return toBoundary;
        }

        return DiscontinuityDistance.Combine(
            toBoundary, _inner.SignedDistanceToMagneticDiscontinuity(in position));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The box surface and the inner field's own members inside the box, positive infinity outside
    /// it, as <see cref="Analytic.BoundedField"/> tracks an electric region: combined into one sign,
    /// an inner surface inside the box went undeclared wherever the box face was nearer. All
    /// infinite where the inner field is zero, which jumps nowhere.
    /// </remarks>
    public int MagneticDiscontinuityMembers => 1 + Math.Max(1, _inner.MagneticDiscontinuityMembers);

    /// <inheritdoc />
    public double MagneticMemberDistanceToDiscontinuity(int member, in Vec3 position) =>
        _inner.MaximumFluxDensity == 0.0 ? double.PositiveInfinity
            : member == 0 ? Region.SignedDistance(in position)
            : Region.Contains(in position) ? _inner.MagneticMemberDistanceToDiscontinuity(member - 1, in position)
            : double.PositiveInfinity;

    /// <inheritdoc />
    public void MagneticMemberDistancesToDiscontinuity(in Vec3 position, Span<double> distances)
    {
        if (_inner.MaximumFluxDensity == 0.0)
        {
            distances.Fill(double.PositiveInfinity);
            return;
        }

        distances[0] = Region.SignedDistance(in position);

        if (Region.Contains(in position))
        {
            _inner.MagneticMemberDistancesToDiscontinuity(in position, distances[1..]);
        }
        else
        {
            distances[1..].Fill(double.PositiveInfinity);
        }
    }

    private static bool Slab(
        double origin, double direction, double min, double max, ref double entry, ref double exit)
    {
        if (direction == 0.0)
        {
            return origin >= min && origin <= max;
        }

        var t1 = (min - origin) / direction;
        var t2 = (max - origin) / direction;

        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        entry = Math.Max(entry, t1);
        exit = Math.Min(exit, t2);
        return true;
    }
}

/// <summary>The sum of several magnetic elements: B adds.</summary>
public sealed class SuperposedMagneticField : IMagneticField
{
    private readonly IMagneticField[] _elements;

    // Where each element's magnetic members start, and how many there are in all.
    private readonly int[] _offsets;

    /// <summary>Superposes magnetic elements.</summary>
    /// <param name="elements">The elements; at least one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
    /// <exception cref="ArgumentException">There are no elements.</exception>
    public SuperposedMagneticField(IEnumerable<IMagneticField> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _elements = [.. elements];

        if (_elements.Length == 0)
        {
            throw new ArgumentException("a superposition needs at least one element", nameof(elements));
        }

        _offsets = new int[_elements.Length + 1];

        for (var i = 0; i < _elements.Length; i++)
        {
            _offsets[i + 1] = _offsets[i] + Math.Max(1, _elements[i].MagneticDiscontinuityMembers);
        }
    }

    /// <inheritdoc />
    public Vec3 MagneticFieldAt(in Vec3 position)
    {
        var sum = Vec3.Zero;

        foreach (var element in _elements)
        {
            sum += element.MagneticFieldAt(in position);
        }

        return sum;
    }

    /// <inheritdoc />
    public Vec3 MagneticFieldAt(in Vec3 position, in Vec3 side)
    {
        var sum = Vec3.Zero;

        foreach (var element in _elements)
        {
            sum += element.MagneticFieldAt(in position, in side);
        }

        return sum;
    }

    /// <inheritdoc />
    public double MaximumFluxDensity => _elements.Sum(e => e.MaximumFluxDensity);

    /// <inheritdoc />
    public double MagnetFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var run = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            run = Math.Min(run, element.MagnetFreeRunLength(in position, in direction));
        }

        return run;
    }

    /// <inheritdoc />
    public double MagnetHalfwayLength(in Vec3 position, in Vec3 direction)
    {
        var run = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            run = Math.Min(run, element.MagnetHalfwayLength(in position, in direction));
        }

        return run;
    }

    /// <inheritdoc />
    public double SignedDistanceToMagneticDiscontinuity(in Vec3 position)
    {
        var composed = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            composed = DiscontinuityDistance.Combine(
                composed, element.SignedDistanceToMagneticDiscontinuity(in position));
        }

        return composed;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each element's members, in order: two abutting magnets are two members whose surfaces
    /// coincide at the shared face, so the face is landed on where the combined sign cancelled.
    /// </remarks>
    public int MagneticDiscontinuityMembers => _offsets[^1];

    /// <inheritdoc />
    public double MagneticMemberDistanceToDiscontinuity(int member, in Vec3 position)
    {
        var element = 0;

        while (_offsets[element + 1] <= member)
        {
            element++;
        }

        return _elements[element].MagneticMemberDistanceToDiscontinuity(member - _offsets[element], in position);
    }

    /// <inheritdoc />
    public void MagneticMemberDistancesToDiscontinuity(in Vec3 position, Span<double> distances)
    {
        for (var i = 0; i < _elements.Length; i++)
        {
            _elements[i].MagneticMemberDistancesToDiscontinuity(in position, distances[_offsets[i].._offsets[i + 1]]);
        }
    }
}
