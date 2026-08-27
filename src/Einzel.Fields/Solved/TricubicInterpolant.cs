namespace Einzel.Fields.Solved;

/// <summary>
/// A Catmull-Rom tensor-product interpolant over a three-dimensional field.
/// </summary>
/// <remarks>
/// <para>
/// ACC-3: "Trilinear interpolation is forbidden anywhere on a trajectory path;
/// tricubic with continuous first derivatives is the floor." The reason is that
/// trilinear has a discontinuous gradient at every cell face, so an ion crossing a
/// grid accumulates a kick per cell that does not cancel - the error is systematic
/// rather than random, and it is interpolation rather than the integrator that
/// dominates a timing budget.
/// </para>
/// <para>
/// The four-by-four-by-four stencil reaches one node outside the grid, and what
/// that ghost holds depends on what kind of face it is outside of. A Dirichlet
/// face is the end of the data and the ghost <em>continues the ramp</em>: clamping
/// there makes the interpolant non-linear in the boundary cell even when the field
/// is exactly linear, which put 7.5 ppm into a flight time in two dimensions. A
/// Neumann face is a mirror and the ghost is the <em>reflection</em>: extrapolating
/// there leaves a spurious normal field on a plane the field is normal to nothing
/// on, which read 14 V/m on an axis in two dimensions. Both lessons are paid for
/// and both apply here.
/// </para>
/// </remarks>
public sealed class TricubicInterpolant
{
    private readonly ScalarField3D _field;

    /// <summary>Wraps a field.</summary>
    /// <param name="field">The nodal values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public TricubicInterpolant(ScalarField3D field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field;
    }

    /// <summary>Whether this interpolant may be used on a trajectory path (ACC-3).</summary>
    public static bool PermittedOnTrajectories => true;

    /// <summary>The interpolated value.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="z">z, in metres.</param>
    /// <returns>The value.</returns>
    public double Value(double x, double y, double z)
    {
        Locate(x, y, z, out var i, out var j, out var k, out var tx, out var ty, out var tz);

        Span<double> cube = stackalloc double[64];
        Gather(i, j, k, cube);

        return Contract(cube, tx, ty, tz, derivative: -1);
    }

    /// <summary>The interpolated gradient.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="z">z, in metres.</param>
    /// <param name="gradX">Rate of change along x, per metre.</param>
    /// <param name="gradY">Rate of change along y, per metre.</param>
    /// <param name="gradZ">Rate of change along z, per metre.</param>
    /// <remarks>
    /// Differentiated analytically rather than by finite differences of the
    /// interpolant. A difference would reintroduce a step size and with it a second
    /// truncation error, on top of an interpolant chosen precisely so its first
    /// derivative is continuous.
    /// </remarks>
    public void Gradient(double x, double y, double z, out double gradX, out double gradY, out double gradZ)
    {
        Locate(x, y, z, out var i, out var j, out var k, out var tx, out var ty, out var tz);

        Span<double> cube = stackalloc double[64];
        Gather(i, j, k, cube);

        var grid = _field.Grid;

        gradX = Contract(cube, tx, ty, tz, derivative: 0) / grid.SpacingX;
        gradY = Contract(cube, tx, ty, tz, derivative: 1) / grid.SpacingY;
        gradZ = Contract(cube, tx, ty, tz, derivative: 2) / grid.SpacingZ;
    }

    private void Locate(
        double x, double y, double z,
        out int i, out int j, out int k,
        out double tx, out double ty, out double tz)
    {
        var grid = _field.Grid;

        var fx = (x - grid.OriginX) / grid.SpacingX;
        var fy = (y - grid.OriginY) / grid.SpacingY;
        var fz = (z - grid.OriginZ) / grid.SpacingZ;

        i = (int)Math.Floor(fx);
        j = (int)Math.Floor(fy);
        k = (int)Math.Floor(fz);

        // Clamped to a cell that exists. Outside the grid the interpolant
        // extrapolates from the edge cell, which is what a field that has decayed
        // to nothing at its boundary should do.
        i = Math.Clamp(i, 0, grid.CountX - 2);
        j = Math.Clamp(j, 0, grid.CountY - 2);
        k = Math.Clamp(k, 0, grid.CountZ - 2);

        tx = fx - i;
        ty = fy - j;
        tz = fz - k;
    }

    private void Gather(int i, int j, int k, Span<double> cube)
    {
        for (var dk = -1; dk <= 2; dk++)
        {
            for (var dj = -1; dj <= 2; dj++)
            {
                for (var di = -1; di <= 2; di++)
                {
                    cube[((dk + 1) * 16) + ((dj + 1) * 4) + (di + 1)] = Node(i + di, j + dj, k + dk);
                }
            }
        }
    }

    /// <summary>Tensor contraction, differentiating along one axis or none.</summary>
    /// <param name="cube">The gathered four-cubed stencil.</param>
    /// <param name="tx">Position within the cell along x.</param>
    /// <param name="ty">Position within the cell along y.</param>
    /// <param name="tz">Position within the cell along z.</param>
    /// <param name="derivative">0, 1 or 2 for the axis to differentiate; -1 for none.</param>
    private static double Contract(ReadOnlySpan<double> cube, double tx, double ty, double tz, int derivative)
    {
        Span<double> alongX = stackalloc double[16];

        for (var s = 0; s < 16; s++)
        {
            var basis = cube.Slice(s * 4, 4);

            alongX[s] = derivative == 0
                ? Slope(basis[0], basis[1], basis[2], basis[3], tx)
                : Spline(basis[0], basis[1], basis[2], basis[3], tx);
        }

        Span<double> alongY = stackalloc double[4];

        for (var s = 0; s < 4; s++)
        {
            var basis = alongX.Slice(s * 4, 4);

            alongY[s] = derivative == 1
                ? Slope(basis[0], basis[1], basis[2], basis[3], ty)
                : Spline(basis[0], basis[1], basis[2], basis[3], ty);
        }

        return derivative == 2
            ? Slope(alongY[0], alongY[1], alongY[2], alongY[3], tz)
            : Spline(alongY[0], alongY[1], alongY[2], alongY[3], tz);
    }

    /// <summary>Catmull-Rom through four points, at a position within the middle span.</summary>
    private static double Spline(double p0, double p1, double p2, double p3, double t) =>
        0.5 * ((2.0 * p1)
            + ((p2 - p0) * t)
            + ((((2.0 * p0) - (5.0 * p1)) + (4.0 * p2)) - p3) * t * t
            + ((((3.0 * p1) - p0) - (3.0 * p2)) + p3) * t * t * t);

    /// <summary>Its derivative with respect to the cell coordinate.</summary>
    private static double Slope(double p0, double p1, double p2, double p3, double t) =>
        0.5 * ((p2 - p0)
            + (2.0 * ((((2.0 * p0) - (5.0 * p1)) + (4.0 * p2)) - p3) * t)
            + (3.0 * ((((3.0 * p1) - p0) - (3.0 * p2)) + p3) * t * t));

    private double Node(int i, int j, int k)
    {
        var grid = _field.Grid;
        var maxI = grid.CountX - 1;
        var maxJ = grid.CountY - 1;
        var maxK = grid.CountZ - 1;

        // At most one node out of range per axis, so this recurses at most three
        // deep at a corner and not at all in the interior.
        if (k < 0)
        {
            return _field.LowerZ == EdgeCondition.Neumann ? Node(i, j, 1) : (2.0 * Node(i, j, 0)) - Node(i, j, 1);
        }

        if (k > maxK)
        {
            return _field.UpperZ == EdgeCondition.Neumann
                ? Node(i, j, maxK - 1)
                : (2.0 * Node(i, j, maxK)) - Node(i, j, maxK - 1);
        }

        if (j < 0)
        {
            return _field.LowerY == EdgeCondition.Neumann ? Node(i, 1, k) : (2.0 * Node(i, 0, k)) - Node(i, 1, k);
        }

        if (j > maxJ)
        {
            return _field.UpperY == EdgeCondition.Neumann
                ? Node(i, maxJ - 1, k)
                : (2.0 * Node(i, maxJ, k)) - Node(i, maxJ - 1, k);
        }

        if (i < 0)
        {
            return _field.LowerX == EdgeCondition.Neumann ? Node(1, j, k) : (2.0 * Node(0, j, k)) - Node(1, j, k);
        }

        if (i > maxI)
        {
            return _field.UpperX == EdgeCondition.Neumann
                ? Node(maxI - 1, j, k)
                : (2.0 * Node(maxI, j, k)) - Node(maxI - 1, j, k);
        }

        return _field[i, j, k];
    }
}
