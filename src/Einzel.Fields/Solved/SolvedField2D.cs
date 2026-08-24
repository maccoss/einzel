using Einzel.Core.Geometry;

namespace Einzel.Fields.Solved;

/// <summary>
/// A solved potential presented as a three-dimensional electrostatic field,
/// invariant along z.
/// </summary>
/// <remarks>
/// <para>
/// SYM-1 in its simplest form: the solver reduced the problem by a dimension and
/// the interpolant reconstructs the full field transparently, so nothing above
/// this needs to know the solve was two-dimensional. For the memo's printed
/// circuit mirror that reduction is exact — stripe electrodes running along the
/// drift direction make the potential genuinely independent of z away from the
/// ends.
/// </para>
/// <para>
/// Outside the grid the field is zero and the potential takes a stated constant.
/// That is a real modelling claim, not a convenience: it says the solve domain
/// was drawn wide enough that the field has decayed at its edge. The box boundary
/// is declared as a discontinuity so the integrator lands on it exactly instead
/// of stepping across it, which is the same treatment the analytic half-space
/// gets and for the same reason.
/// </para>
/// </remarks>
public sealed class SolvedField2D : IElectrostaticField
{
    private readonly IFieldInterpolant _interpolant;
    private readonly Grid2D _grid;
    private readonly double _outsidePotential;

    /// <summary>Creates a field from a solved potential.</summary>
    /// <param name="potential">The solved potential, in volts.</param>
    /// <param name="interpolant">
    /// The interpolant. Must be permitted on trajectories unless
    /// <paramref name="allowDiscontinuousDerivatives"/> is set.
    /// </param>
    /// <param name="outsidePotential">Potential outside the grid box, in volts.</param>
    /// <param name="allowDiscontinuousDerivatives">
    /// Escape hatch for the tests that measure what a forbidden interpolant costs.
    /// Never set this in a model run.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The interpolant does not have continuous first derivatives and the escape
    /// hatch was not set (ACC-3).
    /// </exception>
    public SolvedField2D(
        ScalarField2D potential,
        IFieldInterpolant interpolant,
        double outsidePotential = 0.0,
        bool allowDiscontinuousDerivatives = false)
    {
        ArgumentNullException.ThrowIfNull(potential);
        ArgumentNullException.ThrowIfNull(interpolant);

        if (!interpolant.PermittedOnTrajectories && !allowDiscontinuousDerivatives)
        {
            throw new ArgumentException(
                "ACC-3: an interpolant without continuous first derivatives may not be used on a "
                + "trajectory path, because the resulting field discontinuity at every cell boundary "
                + "accumulates systematically rather than cancelling",
                nameof(interpolant));
        }

        _interpolant = interpolant;
        _grid = potential.Grid;
        _outsidePotential = outsidePotential;
    }

    /// <summary>The grid the potential was solved on.</summary>
    public Grid2D Grid => _grid;

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        if (!_grid.Contains(position.X, position.Y))
        {
            return Vec3.Zero;
        }

        _interpolant.Gradient(position.X, position.Y, out var dx, out var dy);

        // E = -grad(phi). No z component: the potential does not vary along z.
        return new Vec3(-dx, -dy, 0.0);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) =>
        _grid.Contains(position.X, position.Y)
            ? _interpolant.Value(position.X, position.Y)
            : _outsidePotential;

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        if (_grid.Contains(position.X, position.Y))
        {
            return 0.0;
        }

        // Outside the box the field is zero, so the ion may drift exactly until it
        // reaches the box, or forever if it never will.
        var entry = DistanceToBox(position, direction);
        return entry ?? double.PositiveInfinity;
    }

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        // Positive inside the box, negative outside, zero on it. Only x and y
        // matter: the field is invariant along z, so the box is a slab.
        var dx = Math.Min(position.X - _grid.OriginX, _grid.MaxX - position.X);
        var dy = Math.Min(position.Y - _grid.OriginY, _grid.MaxY - position.Y);

        if (dx >= 0.0 && dy >= 0.0)
        {
            return Math.Min(dx, dy);
        }

        // Outside: the Euclidean distance to the box, negated.
        var outX = Math.Max(0.0, -dx);
        var outY = Math.Max(0.0, -dy);
        return -Math.Sqrt((outX * outX) + (outY * outY));
    }

    private double? DistanceToBox(in Vec3 position, in Vec3 direction)
    {
        // Slab method, in x and y only.
        var near = 0.0;
        var far = double.PositiveInfinity;

        if (!Slab(position.X, direction.X, _grid.OriginX, _grid.MaxX, ref near, ref far))
        {
            return null;
        }

        if (!Slab(position.Y, direction.Y, _grid.OriginY, _grid.MaxY, ref near, ref far))
        {
            return null;
        }

        return near > 0.0 && near < far ? near : null;

        static bool Slab(double origin, double direction, double low, double high, ref double near, ref double far)
        {
            if (Math.Abs(direction) < 1e-300)
            {
                return origin >= low && origin <= high;
            }

            var t0 = (low - origin) / direction;
            var t1 = (high - origin) / direction;

            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            near = Math.Max(near, t0);
            far = Math.Min(far, t1);

            return near <= far;
        }
    }
}
