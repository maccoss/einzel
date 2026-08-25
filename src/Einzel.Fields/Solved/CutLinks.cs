namespace Einzel.Fields.Solved;

/// <summary>The four axis directions a stencil reaches in.</summary>
public enum StencilDirection
{
    /// <summary>Increasing x.</summary>
    East = 0,

    /// <summary>Decreasing x.</summary>
    West = 1,

    /// <summary>Increasing y.</summary>
    North = 2,

    /// <summary>Decreasing y.</summary>
    South = 3,
}

/// <summary>
/// Where a conductor surface cuts the grid, for every free node that has one
/// nearby.
/// </summary>
/// <remarks>
/// <para>
/// The difference between a boundary that is snapped to the nearest node and one
/// that sits where it actually is. Without this a Dirichlet surface moves in whole
/// cells, so the discrete operator is a staircase function of electrode position:
/// a sub-cell move changes nothing at all, and a move of one cell changes the
/// operator abruptly. That is fatal in two separate ways — shape derivatives
/// measure the staircase rather than the physics, and the boundary is only
/// located to within a cell, which costs an order of accuracy where the field is
/// usually most interesting.
/// </para>
/// <para>
/// For each free node and each of four directions this records how far the
/// surface is, as a fraction of the cell, and what potential it holds there. A
/// fraction of one means the neighbour is an ordinary node and the stencil is the
/// usual one.
/// </para>
/// </remarks>
public sealed class CutLinks
{
    private const int Directions = 4;

    private readonly double[] _fraction;
    private readonly double[] _potential;

    /// <summary>Creates links with no cuts: every neighbour a full cell away.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public CutLinks(Grid2D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Grid = grid;
        _fraction = new double[grid.NodeCount * Directions];
        _potential = new double[grid.NodeCount * Directions];

        Array.Fill(_fraction, 1.0);
    }

    /// <summary>The grid these links cover.</summary>
    public Grid2D Grid { get; }

    /// <summary>How many links have been cut short by a surface.</summary>
    public int CutCount { get; private set; }

    /// <summary>
    /// How many cut links belong to nodes away from the domain edge.
    /// </summary>
    /// <remarks>
    /// The cut-cell counterpart of the fixed-node count that limits coarsening.
    /// An interior electrode may be represented entirely by cut links and no fixed
    /// nodes at all once the grid is coarse enough that it fits between nodes, and
    /// that is a perfectly good representation — its surface is still in the right
    /// place. Counting only fixed nodes would call it dissolved and stop
    /// coarsening several levels too early.
    /// </remarks>
    public int InteriorCutCount { get; private set; }

    /// <summary>
    /// The smallest cut fraction any link uses. A very small value makes the
    /// stencil stiff, which is why it is clamped and why the clamp is visible.
    /// </summary>
    public double SmallestFraction { get; private set; } = 1.0;

    /// <summary>
    /// The floor applied to a cut fraction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A surface passing arbitrarily close to a node gives an arbitrarily small
    /// spacing on one side of the stencil, and the coefficient grows as its
    /// reciprocal. Nothing breaks mathematically — the operator stays an M-matrix
    /// and Gauss-Seidel still converges — but one node carrying a coefficient
    /// millions of times its neighbours' dominates the residual norm, and a
    /// convergence test measured against that norm stops describing the rest of
    /// the grid.
    /// </para>
    /// <para>
    /// So the fraction is floored, and the price is a boundary knowingly moved by
    /// at most this fraction of a cell. It is worth being precise about the size
    /// of that price, because it is the only geometric approximation left in the
    /// discretisation. Measured on a parallel-plate gap of 20 mm at a mesh of
    /// 0.625 mm, a floor of 0.05 cost 3.0e-4 of the applied potential when a face
    /// landed 0.04 cells from a node; at this floor the same case costs at most
    /// 3.1e-5, and every face position outside the floor's window solves to
    /// 1e-11, which is the solver tolerance rather than any property of the mesh.
    /// </para>
    /// <para>
    /// The window is a thousandth of a cell wide, so it is not where a shape
    /// derivative usually finds itself. It has not been removed altogether
    /// because doing so trades a small bounded geometric error for an unbounded
    /// numerical one, which is the worse of the two.
    /// </para>
    /// </remarks>
    public const double MinimumFraction = 1e-3;

    /// <summary>The fraction of a cell to the surface in one direction.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="direction">Which way to look.</param>
    /// <returns>The fraction, in (0, 1]; one when the neighbour is an ordinary node.</returns>
    public double Fraction(int i, int j, StencilDirection direction) =>
        _fraction[(Grid.Index(i, j) * Directions) + (int)direction];

    /// <summary>The potential held at the surface in one direction.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="direction">Which way to look.</param>
    /// <returns>The potential, in volts. Meaningless when the fraction is one.</returns>
    public double Potential(int i, int j, StencilDirection direction) =>
        _potential[(Grid.Index(i, j) * Directions) + (int)direction];

    /// <summary>Records a surface crossing.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="direction">Which way the surface lies.</param>
    /// <param name="fraction">Fraction of a cell to it; clamped to <see cref="MinimumFraction"/>.</param>
    /// <param name="potential">The potential it holds.</param>
    public void Cut(int i, int j, StencilDirection direction, double fraction, double potential)
    {
        var slot = (Grid.Index(i, j) * Directions) + (int)direction;

        if (_fraction[slot] == 1.0)
        {
            CutCount++;

            if (i > 0 && j > 0 && i < Grid.CountX - 1 && j < Grid.CountY - 1)
            {
                InteriorCutCount++;
            }
        }

        var clamped = Math.Clamp(fraction, MinimumFraction, 1.0);

        _fraction[slot] = clamped;
        _potential[slot] = potential;

        SmallestFraction = Math.Min(SmallestFraction, clamped);
    }

    /// <summary>Whether any link in this set has been cut.</summary>
    public bool HasCuts => CutCount > 0;
}
