using Einzel.Core.Geometry;

namespace Einzel.Transport.Collisions;

/// <summary>
/// A neutral gas whose bulk velocity is sampled from a grid.
/// </summary>
/// <remarks>
/// <para>
/// The half of GAS-1 a single declared vector cannot express, and the one spec
/// figure 4 makes a requirement rather than a benefit above 10^-2 mbar: "the
/// neutral jet off the inlet capillary drags ions and frequently dominates the
/// axial DC gradient". A jet is not uniform across a ring stack, and a funnel
/// modelled in a gas that moves all in one piece is a funnel whose gas is not
/// doing what a funnel's gas does.
/// </para>
/// <para>
/// Einzel consumes a velocity field; it does not compute one. That boundary is
/// deliberate and is the same one §17 draws around visualisation: computing a
/// compressible flow through a differentially pumped stack is a CFD problem, and a
/// half-hearted one inside an ion-optics engine would be worse than none because
/// it would look like an answer.
/// </para>
/// <para>
/// <strong>Trilinear, not tricubic.</strong> ACC-3 forbids the cheap interpolant on
/// a trajectory path because the interpolant's discontinuous derivatives accumulate
/// into the timing budget over a hundred thousand cell crossings. That argument
/// does not transfer: the gas velocity's derivative is never used, it enters the
/// drift-diffusion flux as a value at a face, and a CFD field arrives with its own
/// discretisation error far above anything the interpolant adds.
/// </para>
/// <para>
/// <strong>Clamped outside, and the overhang is measurable.</strong> A flow that
/// stopped at the edge of its imported box would put a shear where the instrument
/// has none. Clamping continues the edge value, which is right for a stream and
/// wrong for the end of a jet - so <see cref="FractionOutside"/> exists for a caller
/// to warn with, because the honest statement is how much of the tracked region was
/// never measured rather than a silent extrapolation either way.
/// </para>
/// </remarks>
public sealed class SampledGasFlow : IGasFlow
{
    private readonly double[] _values;
    private readonly int _countX;
    private readonly int _countY;
    private readonly int _countZ;
    private readonly Vec3 _origin;
    private readonly Vec3 _spacing;

    /// <summary>Creates a flow from samples on a uniform grid.</summary>
    /// <param name="countX">Nodes along x, at least one.</param>
    /// <param name="countY">Nodes along y.</param>
    /// <param name="countZ">Nodes along z.</param>
    /// <param name="originSi">Position of node (0,0,0), in metres.</param>
    /// <param name="spacingSi">Node spacing, in metres. Zero on an axis with one node.</param>
    /// <param name="values">
    /// Three components per node, x fastest then y then z - the order VTK reads an
    /// extent in and the order this engine writes one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is not positive.</exception>
    /// <exception cref="ArgumentException">The sample count does not match the extent.</exception>
    public SampledGasFlow(
        int countX, int countY, int countZ, Vec3 originSi, Vec3 spacingSi, double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(countX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(countY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(countZ);

        var expected = 3L * countX * countY * countZ;

        if (values.Length != expected)
        {
            throw new ArgumentException(
                $"{values.Length} numbers for an extent of {countX}x{countY}x{countZ} with three "
                + $"components, which needs {expected}",
                nameof(values));
        }

        _countX = countX;
        _countY = countY;
        _countZ = countZ;
        _origin = originSi;
        _spacing = spacingSi;
        _values = values;

        var fastest = 0.0;
        var moving = false;

        for (var i = 0; i + 2 < values.Length; i += 3)
        {
            var speed = Math.Sqrt(
                (values[i] * values[i])
                + (values[i + 1] * values[i + 1])
                + (values[i + 2] * values[i + 2]));

            fastest = Math.Max(fastest, speed);
            moving |= speed > 0.0;
        }

        FastestSpeedSi = fastest;
        IsMoving = moving;
    }

    /// <inheritdoc/>
    public bool IsMoving { get; }

    /// <inheritdoc/>
    public double FastestSpeedSi { get; }

    /// <summary>The lower corner of the sampled region, in metres.</summary>
    public Vec3 MinimumSi => _origin;

    /// <summary>The upper corner, in metres.</summary>
    public Vec3 MaximumSi => new(
        _origin.X + (_spacing.X * (_countX - 1)),
        _origin.Y + (_spacing.Y * (_countY - 1)),
        _origin.Z + (_spacing.Z * (_countZ - 1)));

    /// <inheritdoc/>
    public Vec3 VelocityAt(in Vec3 point)
    {
        var (i, fx) = Cell(point.X, _origin.X, _spacing.X, _countX);
        var (j, fy) = Cell(point.Y, _origin.Y, _spacing.Y, _countY);
        var (k, fz) = Cell(point.Z, _origin.Z, _spacing.Z, _countZ);

        var result = Vec3.Zero;

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

                    result += At(i + dx, j + dy, k + dz) * (wx * wy * wz);
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
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

    private Vec3 At(int i, int j, int k)
    {
        i = Math.Clamp(i, 0, _countX - 1);
        j = Math.Clamp(j, 0, _countY - 1);
        k = Math.Clamp(k, 0, _countZ - 1);

        var index = 3 * (((k * _countY) + j) * _countX + i);

        return new Vec3(_values[index], _values[index + 1], _values[index + 2]);
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
