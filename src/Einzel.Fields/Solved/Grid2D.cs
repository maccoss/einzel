using System.Numerics;

namespace Einzel.Fields.Solved;

/// <summary>
/// A uniform Cartesian grid in the x-y plane, in SI metres.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 10 puts a finite-difference multigrid solver on a regular
/// Cartesian or axisymmetric grid at the centre of Phase 1: straightforward to
/// implement correctly, clean to parallelise, and semantically close enough to
/// SIMION that SIMION becomes a direct cross-validation target.
/// </para>
/// <para>
/// Two dimensions, because that is what the first customer needs. A printed
/// circuit ion mirror is stripe electrodes running along the drift direction, so
/// the potential varies only across the stripes and through the board gap and is
/// invariant along the drift. That symmetry is not in the spec's SYM-1 list,
/// which names cylindrical, mirror-plane, and discrete periodic; it should be,
/// because it is the symmetry the memo's analyzer actually has and it removes a
/// whole dimension from the solve.
/// </para>
/// <para>
/// Node counts are of the form 2^k + 1 so that coarsening halves the intervals
/// exactly and every level shares the fine grid's corner nodes.
/// </para>
/// </remarks>
public sealed class Grid2D
{
    /// <summary>Creates a grid.</summary>
    /// <param name="originX">x of node 0, in metres.</param>
    /// <param name="originY">y of node 0, in metres.</param>
    /// <param name="spacing">Node spacing, in metres.</param>
    /// <param name="countX">Node count along x; at least 3.</param>
    /// <param name="countY">Node count along y; at least 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is below 3, or the spacing is not positive.</exception>
    public Grid2D(double originX, double originY, double spacing, int countX, int countY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);
        ArgumentOutOfRangeException.ThrowIfLessThan(countX, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(countY, 3);

        OriginX = originX;
        OriginY = originY;
        Spacing = spacing;
        CountX = countX;
        CountY = countY;
    }

    /// <summary>
    /// Creates a grid spanning a box, with the node count set by the refinement
    /// level so that coarsening is exact.
    /// </summary>
    /// <param name="minX">Lower x bound, in metres.</param>
    /// <param name="minY">Lower y bound, in metres.</param>
    /// <param name="maxX">Upper x bound, in metres.</param>
    /// <param name="maxY">Upper y bound, in metres.</param>
    /// <param name="intervalsX">Intervals along x; must be a positive power of two.</param>
    /// <returns>The grid, with square cells.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The box is degenerate, or the interval count is not a power of two.</exception>
    /// <remarks>
    /// The y interval count follows from the aspect ratio, rounded to a power of
    /// two, so cells stay square. Anisotropic cells are representable by the
    /// five-point stencil but degrade multigrid convergence, and squareness is
    /// cheaper to keep than to diagnose.
    /// </remarks>
    public static Grid2D OverBox(double minX, double minY, double maxX, double maxY, int intervalsX)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalsX);

        if (!int.IsPow2(intervalsX))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalsX), intervalsX, "interval counts must be a power of two so coarsening is exact");
        }

        if (maxX <= minX || maxY <= minY)
        {
            throw new ArgumentOutOfRangeException(nameof(maxX), "the box must have positive extent");
        }

        var spacing = (maxX - minX) / intervalsX;
        var intervalsY = Math.Max(2, (int)Math.Round((maxY - minY) / spacing));

        // Round up to the next power of two so both directions coarsen together.
        intervalsY = (int)BitOperations.RoundUpToPowerOf2((uint)intervalsY);

        return new Grid2D(minX, minY, spacing, intervalsX + 1, intervalsY + 1);
    }

    /// <summary>x of node 0, in metres.</summary>
    public double OriginX { get; }

    /// <summary>y of node 0, in metres.</summary>
    public double OriginY { get; }

    /// <summary>Node spacing, in metres.</summary>
    public double Spacing { get; }

    /// <summary>Node count along x.</summary>
    public int CountX { get; }

    /// <summary>Node count along y.</summary>
    public int CountY { get; }

    /// <summary>Total node count.</summary>
    public int NodeCount => CountX * CountY;

    /// <summary>Upper x bound, in metres.</summary>
    public double MaxX => OriginX + ((CountX - 1) * Spacing);

    /// <summary>Upper y bound, in metres.</summary>
    public double MaxY => OriginY + ((CountY - 1) * Spacing);

    /// <summary>x of a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double X(int i) => OriginX + (i * Spacing);

    /// <summary>y of a node.</summary>
    /// <param name="j">Node index along y.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double Y(int j) => OriginY + (j * Spacing);

    /// <summary>Row-major index of a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <returns>The flat index.</returns>
    public int Index(int i, int j) => (j * CountX) + i;

    /// <summary>Whether a point lies inside the grid box.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <returns><see langword="true"/> when inside or on the boundary.</returns>
    public bool Contains(double x, double y) =>
        x >= OriginX && x <= MaxX && y >= OriginY && y <= MaxY;

    /// <summary>Whether this grid can be coarsened by a further factor of two.</summary>
    public bool CanCoarsen => (CountX - 1) % 2 == 0 && (CountY - 1) % 2 == 0
        && (CountX - 1) / 2 >= 2 && (CountY - 1) / 2 >= 2;

    /// <summary>The next coarser grid, with twice the spacing and the same corners.</summary>
    /// <returns>The coarsened grid.</returns>
    /// <exception cref="InvalidOperationException">The grid cannot be coarsened further.</exception>
    public Grid2D Coarsen()
    {
        if (!CanCoarsen)
        {
            throw new InvalidOperationException(
                $"a {CountX} by {CountY} grid cannot be coarsened further");
        }

        return new Grid2D(OriginX, OriginY, Spacing * 2.0, ((CountX - 1) / 2) + 1, ((CountY - 1) / 2) + 1);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{CountX}x{CountY} at h={Spacing:G6} m");
}
