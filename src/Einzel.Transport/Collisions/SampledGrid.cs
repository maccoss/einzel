using Einzel.Core.Geometry;

namespace Einzel.Transport.Collisions;

/// <summary>
/// Samples on a uniform grid, interpolated multilinearly, with the region they
/// actually cover.
/// </summary>
/// <remarks>
/// <para>
/// The machinery underneath every imported gas field, shared rather than written
/// once per quantity. <see cref="SampledGasFlow"/> reads three components from it
/// and <see cref="SampledGasDensity"/> reads one; the cell arithmetic, the
/// clamping at the edge and the coverage fraction are the same either way, and
/// they are the parts that were got wrong first.
/// </para>
/// <para>
/// <b>Extracted rather than copied.</b> A computation duplicated across a seam is
/// how a declared gas came to take part in a run and not in a figure of merit, and
/// how <c>driftVelocity</c> came to be honoured by one transport mode and dropped
/// by the other. The same reasoning applies to a coverage rule whose three cases
/// took two attempts to get right.
/// </para>
/// </remarks>
public sealed class SampledGrid
{
    private readonly double[] _values;
    private readonly int _components;
    private readonly int _countX;
    private readonly int _countY;
    private readonly int _countZ;
    private readonly Vec3 _origin;
    private readonly Vec3 _spacing;

    /// <summary>Creates a sampled grid.</summary>
    /// <param name="components">Numbers per node - one for a scalar, three for a vector.</param>
    /// <param name="countX">Nodes along x, at least one.</param>
    /// <param name="countY">Nodes along y.</param>
    /// <param name="countZ">Nodes along z.</param>
    /// <param name="originSi">Position of node (0,0,0), in metres.</param>
    /// <param name="spacingSi">Node spacing, in metres. Zero on an axis with one node.</param>
    /// <param name="values">
    /// The samples, x fastest then y then z - the order VTK reads an extent in and
    /// the order this engine writes one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is not positive.</exception>
    /// <exception cref="ArgumentException">The sample count does not match the extent.</exception>
    public SampledGrid(
        int components,
        int countX,
        int countY,
        int countZ,
        Vec3 originSi,
        Vec3 spacingSi,
        double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(components);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(countX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(countY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(countZ);

        var expected = (long)components * countX * countY * countZ;

        if (values.Length != expected)
        {
            throw new ArgumentException(
                $"{values.Length} numbers for an extent of {countX}x{countY}x{countZ} with "
                + $"{components} component(s), which needs {expected}",
                nameof(values));
        }

        _components = components;
        _countX = countX;
        _countY = countY;
        _countZ = countZ;
        _origin = originSi;
        _spacing = spacingSi;
        _values = values;
    }

    /// <summary>How many numbers each node carries.</summary>
    public int Components => _components;

    /// <summary>Every sample, in file order.</summary>
    public ReadOnlySpan<double> Values => _values;

    /// <summary>Nodes along x.</summary>
    public int CountX => _countX;

    /// <summary>Nodes along y.</summary>
    public int CountY => _countY;

    /// <summary>Nodes along z.</summary>
    public int CountZ => _countZ;

    /// <summary>Node spacing, in metres. Zero on an axis with one node.</summary>
    public Vec3 SpacingSi => _spacing;

    /// <summary>The lower corner of the sampled region, in metres.</summary>
    public Vec3 MinimumSi => _origin;

    /// <summary>The upper corner, in metres.</summary>
    public Vec3 MaximumSi => new(
        _origin.X + (_spacing.X * (_countX - 1)),
        _origin.Y + (_spacing.Y * (_countY - 1)),
        _origin.Z + (_spacing.Z * (_countZ - 1)));

    /// <summary>Interpolates every component at a point, into a caller's buffer.</summary>
    /// <param name="point">Where, in metres.</param>
    /// <param name="into">
    /// Where to write the result. At least <see cref="Components"/> long.
    /// </param>
    /// <exception cref="ArgumentException">The buffer is too short.</exception>
    /// <remarks>
    /// Outside the box the edge value is continued rather than refused. How much of
    /// a region that covers is <see cref="FractionOutside"/>'s answer, reported
    /// rather than silently absorbed: the gas beyond an imported volume is whatever
    /// the last plane of it said, which is right for a stream and wrong for the end
    /// of a jet, and nothing in the samples says which.
    /// </remarks>
    public void SampleInto(in Vec3 point, Span<double> into)
    {
        if (into.Length < _components)
        {
            throw new ArgumentException(
                $"a buffer of {into.Length} for {_components} component(s)", nameof(into));
        }

        var (i, fx) = Cell(point.X, _origin.X, _spacing.X, _countX);
        var (j, fy) = Cell(point.Y, _origin.Y, _spacing.Y, _countY);
        var (k, fz) = Cell(point.Z, _origin.Z, _spacing.Z, _countZ);

        into[.._components].Clear();

        for (var dz = 0; dz <= 1; dz++)
        {
            var wz = dz == 0 ? 1.0 - fz : fz;

            if (wz == 0.0)
            {
                continue;
            }

            for (var dy = 0; dy <= 1; dy++)
            {
                var wy = dy == 0 ? 1.0 - fy : fy;

                if (wy == 0.0)
                {
                    continue;
                }

                for (var dx = 0; dx <= 1; dx++)
                {
                    var wx = dx == 0 ? 1.0 - fx : fx;

                    if (wx == 0.0)
                    {
                        continue;
                    }

                    var weight = wx * wy * wz;
                    var at = Index(i + dx, j + dy, k + dz);

                    for (var c = 0; c < _components; c++)
                    {
                        into[c] += _values[at + c] * weight;
                    }
                }
            }
        }
    }

    /// <summary>Interpolates a one-component grid at a point.</summary>
    /// <param name="point">Where, in metres.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">This grid is not a scalar one.</exception>
    public double ScalarAt(in Vec3 point)
    {
        if (_components != 1)
        {
            throw new InvalidOperationException(
                $"this grid carries {_components} components, and a scalar reading needs one");
        }

        Span<double> one = stackalloc double[1];

        SampleInto(in point, one);

        return one[0];
    }

    /// <summary>Whether a point lies inside the sampled region.</summary>
    /// <param name="point">The point, in metres.</param>
    /// <returns><see langword="true"/> where the samples are data rather than an edge value.</returns>
    public bool Covers(in Vec3 point)
    {
        var lower = MinimumSi;
        var upper = MaximumSi;

        return point.X >= lower.X && point.X <= upper.X
            && point.Y >= lower.Y && point.Y <= upper.Y
            && point.Z >= lower.Z && point.Z <= upper.Z;
    }

    /// <summary>
    /// How much of a box lies outside the sampled region, as a volume fraction.
    /// </summary>
    /// <param name="minimumSi">Lower corner of the box, in metres.</param>
    /// <param name="maximumSi">Upper corner.</param>
    /// <returns>Zero when the box is wholly inside, one when it is wholly outside.</returns>
    /// <remarks>
    /// What a caller warns with. Outside the imported extent the edge value is
    /// continued, which is right for a stream and wrong for the end of a jet - and
    /// there is no way to tell which from the samples alone, so the honest output is
    /// the size of the region where the answer was extrapolated rather than
    /// measured.
    /// </remarks>
    public double FractionOutside(Vec3 minimumSi, Vec3 maximumSi)
    {
        var lower = MinimumSi;
        var upper = MaximumSi;

        var covered =
            Covered(minimumSi.X, maximumSi.X, lower.X, upper.X, _countX == 1)
            * Covered(minimumSi.Y, maximumSi.Y, lower.Y, upper.Y, _countY == 1)
            * Covered(minimumSi.Z, maximumSi.Z, lower.Z, upper.Z, _countZ == 1);

        return Math.Clamp(1.0 - covered, 0.0, 1.0);
    }

    /// <summary>
    /// What fraction of a box's extent along one axis the field covers.
    /// </summary>
    /// <remarks>
    /// Three cases, and an earlier version collapsed two of them that are opposites.
    /// A field with one node on an axis does not <em>resolve</em> that axis, so it
    /// makes no claim about it and covers all of it - that is what a
    /// two-dimensional import looks like. A box with no thickness on an axis is
    /// either inside or outside, with nothing in between. Everything else is the
    /// ordinary overlap, and a non-overlapping interval covers none of it rather
    /// than all of it, which is the case that was wrong: a box a long way outside
    /// the field reported itself as fully covered.
    /// </remarks>
    private static double Covered(
        double boxLow, double boxHigh, double fieldLow, double fieldHigh, bool fieldIsFlat)
    {
        if (fieldIsFlat)
        {
            return 1.0;
        }

        var span = boxHigh - boxLow;

        if (span <= 0.0)
        {
            return boxLow >= fieldLow && boxLow <= fieldHigh ? 1.0 : 0.0;
        }

        var low = Math.Max(boxLow, fieldLow);
        var high = Math.Min(boxHigh, fieldHigh);

        return high > low ? (high - low) / span : 0.0;
    }

    /// <summary>The same extent, carrying different numbers.</summary>
    /// <param name="components">Numbers per node in the result.</param>
    /// <param name="values">The replacement samples.</param>
    /// <returns>A grid over the identical region.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException">The sample count does not match the extent.</exception>
    /// <remarks>
    /// What a unit conversion goes through. Reconstructing the extent from the
    /// published corners and spacing is arithmetic that can be got wrong on a flat
    /// axis, and getting it wrong would silently reshape the field.
    /// </remarks>
    public SampledGrid WithValues(int components, double[] values) =>
        new(components, _countX, _countY, _countZ, _origin, _spacing, values);

    private int Index(int i, int j, int k)
    {
        i = Math.Clamp(i, 0, _countX - 1);
        j = Math.Clamp(j, 0, _countY - 1);
        k = Math.Clamp(k, 0, _countZ - 1);

        return _components * ((((k * _countY) + j) * _countX) + i);
    }

    /// <summary>The cell a coordinate falls in, and how far across it.</summary>
    private static (int Index, double Fraction) Cell(
        double value, double origin, double spacing, int count)
    {
        if (count == 1 || spacing == 0.0)
        {
            return (0, 0.0);
        }

        var position = (value - origin) / spacing;

        // Clamped rather than refused. Outside the box the edge value continues,
        // and how much of the tracked region that covers is reported separately by
        // FractionOutside rather than silently absorbed here.
        if (position <= 0.0)
        {
            return (0, 0.0);
        }

        if (position >= count - 1)
        {
            return (count - 2, 1.0);
        }

        var index = (int)position;

        return (index, position - index);
    }
}
