namespace Einzel.Fields.Solved;

/// <summary>Samples a gridded scalar and its gradient at arbitrary points.</summary>
public interface IFieldInterpolant
{
    /// <summary>The interpolated value.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <returns>The value.</returns>
    double Value(double x, double y);

    /// <summary>The interpolated gradient.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="dx">Derivative with respect to x.</param>
    /// <param name="dy">Derivative with respect to y.</param>
    void Gradient(double x, double y, out double dx, out double dy);

    /// <summary>
    /// Whether this interpolant may be used on a trajectory path. ACC-3 permits
    /// only interpolants with continuous first derivatives.
    /// </summary>
    bool PermittedOnTrajectories { get; }
}

/// <summary>
/// Bicubic interpolation with continuous first derivatives.
/// </summary>
/// <remarks>
/// <para>
/// The minimum spec section 8 permits on a trajectory path, and the reasoning is
/// worth restating because it is the least intuitive numerical claim in the
/// document. The instinct on missing a timing target is to reach for a
/// higher-order integrator. That is usually the wrong lever: "A trajectory
/// crossing a gridded potential accumulates error from the interpolant's
/// discontinuous derivatives at every cell boundary, and over 10^5 crossings that
/// error is systematic rather than random."
/// </para>
/// <para>
/// Systematic is the operative word. A random error at each of 10^5 crossings
/// would grow as the square root of the count and largely cancel; an error whose
/// sign is set by the direction of travel accumulates linearly and does not. That
/// is why ACC-3 caps the interpolation contribution at half the ACC-1 budget and
/// why trilinear — bilinear here — is forbidden outright rather than merely
/// discouraged.
/// </para>
/// <para>
/// The scheme is the Catmull-Rom form of bicubic Hermite: the derivative at each
/// node is the central difference of its neighbours, which makes the result C1
/// across cell boundaries. It needs a four-by-four stencil, so it degrades to a
/// clamped stencil within one cell of the grid edge.
/// </para>
/// </remarks>
public sealed class BicubicInterpolant : IFieldInterpolant
{
    private readonly ScalarField2D _field;

    /// <summary>Creates an interpolant over a field.</summary>
    /// <param name="field">The gridded values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public BicubicInterpolant(ScalarField2D field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field;
    }

    /// <inheritdoc/>
    public bool PermittedOnTrajectories => true;

    /// <inheritdoc/>
    public double Value(double x, double y)
    {
        Locate(x, y, out var i, out var j, out var tx, out var ty);

        Span<double> column = stackalloc double[4];

        for (var n = 0; n < 4; n++)
        {
            Span<double> row = [Node(i - 1, j - 1 + n), Node(i, j - 1 + n), Node(i + 1, j - 1 + n), Node(i + 2, j - 1 + n)];
            column[n] = CatmullRom(row, tx);
        }

        return CatmullRom(column, ty);
    }

    /// <inheritdoc/>
    public void Gradient(double x, double y, out double dx, out double dy)
    {
        Locate(x, y, out var i, out var j, out var tx, out var ty);

        Span<double> column = stackalloc double[4];
        Span<double> columnDerivative = stackalloc double[4];

        for (var n = 0; n < 4; n++)
        {
            Span<double> row = [Node(i - 1, j - 1 + n), Node(i, j - 1 + n), Node(i + 1, j - 1 + n), Node(i + 2, j - 1 + n)];
            column[n] = CatmullRom(row, tx);
            columnDerivative[n] = CatmullRomDerivative(row, tx);
        }

        dx = CatmullRom(columnDerivative, ty) / _field.Grid.SpacingX;
        dy = CatmullRomDerivative(column, ty) / _field.Grid.SpacingY;
    }

    private void Locate(double x, double y, out int i, out int j, out double tx, out double ty)
    {
        var grid = _field.Grid;

        var fx = (x - grid.OriginX) / grid.SpacingX;
        var fy = (y - grid.OriginY) / grid.SpacingY;

        i = (int)Math.Floor(fx);
        j = (int)Math.Floor(fy);

        i = Math.Clamp(i, 0, grid.CountX - 2);
        j = Math.Clamp(j, 0, grid.CountY - 2);

        tx = fx - i;
        ty = fy - j;
    }

    /// <summary>
    /// A stencil sample, with out-of-range indices filled by linear
    /// extrapolation rather than by clamping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The four-by-four stencil reaches one node beyond the grid in each
    /// direction, so the boundary cells need a ghost value. Clamping — repeating
    /// the edge node — is the obvious choice and is wrong in a way that matters
    /// here: it makes the interpolant non-linear in the boundary cell even when
    /// the underlying field is exactly linear, because the ghost sits at the edge
    /// value instead of continuing the ramp.
    /// </para>
    /// <para>
    /// That is not academic. An ion mirror is entered and left through the plane
    /// where the field begins, so the ion spends time in the boundary cell twice
    /// per reflection, and a clamped stencil put 7.5 ppm into the flight time of a
    /// mirror whose exact solution is a pure ramp — over the whole ACC-1 budget,
    /// caused entirely by the corner case. Linear extrapolation reproduces linear
    /// fields exactly everywhere, and reduces the edge derivative estimate to the
    /// one-sided difference, which is the right answer there anyway.
    /// </para>
    /// </remarks>
    private double Node(int i, int j)
    {
        var grid = _field.Grid;
        var maxI = grid.CountX - 1;
        var maxJ = grid.CountY - 1;

        // At most one node out of range in each direction, so this recurses twice
        // at the corners and not at all in the interior.
        if (j < 0)
        {
            return (2.0 * Node(i, 0)) - Node(i, 1);
        }

        if (j > maxJ)
        {
            return (2.0 * Node(i, maxJ)) - Node(i, maxJ - 1);
        }

        if (i < 0)
        {
            return (2.0 * Node(0, j)) - Node(1, j);
        }

        if (i > maxI)
        {
            return (2.0 * Node(maxI, j)) - Node(maxI - 1, j);
        }

        return _field[i, j];
    }

    /// <summary>
    /// The Catmull-Rom cubic through the middle two of four samples, with the
    /// tangents taken as central differences. C1 across segment boundaries.
    /// </summary>
    private static double CatmullRom(ReadOnlySpan<double> p, double t)
    {
        var a = p[1];
        var b = 0.5 * (p[2] - p[0]);
        var c = p[0] - (2.5 * p[1]) + (2.0 * p[2]) - (0.5 * p[3]);
        var d = (-0.5 * p[0]) + (1.5 * p[1]) - (1.5 * p[2]) + (0.5 * p[3]);

        return a + (t * (b + (t * (c + (t * d)))));
    }

    private static double CatmullRomDerivative(ReadOnlySpan<double> p, double t)
    {
        var b = 0.5 * (p[2] - p[0]);
        var c = p[0] - (2.5 * p[1]) + (2.0 * p[2]) - (0.5 * p[3]);
        var d = (-0.5 * p[0]) + (1.5 * p[1]) - (1.5 * p[2]) + (0.5 * p[3]);

        return b + (t * ((2.0 * c) + (3.0 * t * d)));
    }
}

/// <summary>
/// Bilinear interpolation. Present so the cost of using it can be measured, and
/// refused on trajectory paths.
/// </summary>
/// <remarks>
/// <para>
/// Its first derivatives are piecewise constant in each direction and jump at
/// every cell boundary, which means the electric field an ion sees is
/// discontinuous. Spec section 8: "Trilinear interpolation is not permitted
/// anywhere in a trajectory path."
/// </para>
/// <para>
/// Keeping it, rather than simply not writing it, turns that prohibition from an
/// assertion into a measurement — <c>InterpolationTests</c> puts the same
/// trajectory through both and reports the difference. A rule with a number
/// attached survives contact with someone who thinks it is overcautious.
/// </para>
/// </remarks>
public sealed class BilinearInterpolant : IFieldInterpolant
{
    private readonly ScalarField2D _field;

    /// <summary>Creates an interpolant over a field.</summary>
    /// <param name="field">The gridded values.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public BilinearInterpolant(ScalarField2D field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field;
    }

    /// <inheritdoc/>
    public bool PermittedOnTrajectories => false;

    /// <inheritdoc/>
    public double Value(double x, double y)
    {
        Locate(x, y, out var i, out var j, out var tx, out var ty);

        var v00 = _field[i, j];
        var v10 = _field[i + 1, j];
        var v01 = _field[i, j + 1];
        var v11 = _field[i + 1, j + 1];

        return ((1 - tx) * (1 - ty) * v00) + (tx * (1 - ty) * v10)
            + ((1 - tx) * ty * v01) + (tx * ty * v11);
    }

    /// <inheritdoc/>
    public void Gradient(double x, double y, out double dx, out double dy)
    {
        Locate(x, y, out var i, out var j, out var tx, out var ty);

        var v00 = _field[i, j];
        var v10 = _field[i + 1, j];
        var v01 = _field[i, j + 1];
        var v11 = _field[i + 1, j + 1];
        var grid = _field.Grid;

        dx = (((1 - ty) * (v10 - v00)) + (ty * (v11 - v01))) / grid.SpacingX;
        dy = (((1 - tx) * (v01 - v00)) + (tx * (v11 - v10))) / grid.SpacingY;
    }

    private void Locate(double x, double y, out int i, out int j, out double tx, out double ty)
    {
        var grid = _field.Grid;

        var fx = (x - grid.OriginX) / grid.SpacingX;
        var fy = (y - grid.OriginY) / grid.SpacingY;

        i = Math.Clamp((int)Math.Floor(fx), 0, grid.CountX - 2);
        j = Math.Clamp((int)Math.Floor(fy), 0, grid.CountY - 2);

        tx = fx - i;
        ty = fy - j;
    }
}
