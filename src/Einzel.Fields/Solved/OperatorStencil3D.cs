namespace Einzel.Fields.Solved;

/// <summary>
/// The discrete Laplacian as stored coefficients rather than as a rule applied to
/// geometry.
/// </summary>
/// <remarks>
/// <para>
/// The solver's smoother recomputes its stencil from the mask at every node of every
/// sweep, reading cut fractions and edge conditions as it goes. That is the right shape
/// for a fine level, where the geometry is the authority - but a <em>coarse</em> level
/// built the same way is a different problem rather than a coarser one, which is what
/// makes the V-cycle here descend one or two levels instead of five or six.
/// </para>
/// <para>
/// Galerkin coarsening needs the operator as a matrix, because the coarse operator is
/// <c>R A P</c> - built from the fine operator rather than from the geometry again.
/// This is that matrix, in the only form worth storing: a diagonal and six off-diagonal
/// coefficients per node.
/// </para>
/// <para>
/// <b>The convention is the smoother's own, deliberately.</b> A node's equation is
/// <c>diagonal * phi_here - sum(arm * phi_neighbour) = -halfH2 * rhs</c>, exactly as
/// <c>PoissonSolver3D.Smooth</c> writes it, so a smoother built on these coefficients
/// can be checked against that one for bit-identical output. A second convention would
/// have to be reconciled with the first every time either changed.
/// </para>
/// <para>
/// <b>A cut arm and a fixed neighbour contribute to the diagonal and not to the
/// off-diagonals</b>, because their potentials are known: they belong on the
/// right-hand side. <see cref="Known"/> carries what they contribute there, so the
/// matrix is over free nodes alone - which is what a coarse operator has to be.
/// </para>
/// </remarks>
public sealed class OperatorStencil3D
{
    /// <summary>Arms per node: +x, -x, +y, -y, +z, -z.</summary>
    public const int Arms = 6;

    private readonly double[] _diagonal;
    private readonly double[] _arm;
    private readonly double[] _known;

    private OperatorStencil3D(Grid3D grid)
    {
        Grid = grid;

        _diagonal = new double[grid.NodeCount];
        _arm = new double[grid.NodeCount * Arms];
        _known = new double[grid.NodeCount];
    }

    /// <summary>The grid this operator is over.</summary>
    public Grid3D Grid { get; }

    /// <summary>Arm directions, in the order the coefficients are stored.</summary>
    public static (int Di, int Dj, int Dk)[] Directions =>
        [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>The coefficient on a node's own value.</summary>
    /// <param name="node">The node index.</param>
    /// <returns>The diagonal, which is positive for a free node and zero for a fixed one.</returns>
    public double Diagonal(int node) => _diagonal[node];

    /// <summary>The coefficient on a free neighbour's value.</summary>
    /// <param name="node">The node index.</param>
    /// <param name="arm">Which arm, indexing <see cref="Directions"/>.</param>
    /// <returns>A non-negative coefficient, zero where the arm leads to a known value.</returns>
    public double Arm(int node, int arm) => _arm[(node * Arms) + arm];

    /// <summary>
    /// What the known values around a node contribute to its right-hand side.
    /// </summary>
    /// <param name="node">The node index.</param>
    /// <returns>The sum of coefficient times potential over cut arms and fixed neighbours.</returns>
    public double Known(int node) => _known[node];

    /// <summary>
    /// Assembles the operator from a mask, reading exactly what the smoother reads.
    /// </summary>
    /// <param name="mask">The geometry.</param>
    /// <param name="potential">
    /// The field whose fixed nodes hold the known potentials, or null to read them from
    /// the mask.
    /// </param>
    /// <returns>The assembled operator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mask"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>A fixed neighbour's value comes from the field, not the mask</b>, because that
    /// is what the smoother reads and the two differ where it matters most: a coarse
    /// level solves for the <em>error</em>, whose value on a conductor is zero rather
    /// than the electrode's potential. Reading the mask there would inject the applied
    /// voltage into every correction.
    /// </para>
    /// <para>
    /// A Neumann edge is a mirror, so its ghost <em>is</em> a node of the grid - the one
    /// reflected inside - and is an ordinary off-diagonal entry unless that node is
    /// itself fixed.
    /// </para>
    /// </remarks>
    public static OperatorStencil3D Assemble(DirichletMask3D mask, ScalarField3D? potential = null)
    {
        ArgumentNullException.ThrowIfNull(mask);

        var grid = mask.Grid;
        var built = new OperatorStencil3D(grid);

        var aspectY = (grid.SpacingX / grid.SpacingY) * (grid.SpacingX / grid.SpacingY);
        var aspectZ = (grid.SpacingX / grid.SpacingZ) * (grid.SpacingX / grid.SpacingZ);

        var cuts = mask.Cuts;
        var directions = Directions;

        // Hoisted: a stackalloc inside a loop grows the frame every iteration.
        Span<double> fraction = stackalloc double[Arms];
        Span<double> surface = stackalloc double[Arms];
        Span<int> neighbour = stackalloc int[Arms];

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    var node = grid.Index(i, j, k);

                    if (mask.IsFixed(i, j, k))
                    {
                        continue;
                    }

                    // The three axis scalings, exactly as the smoother forms them: each
                    // axis is in cell units of its own spacing, and the pair of arms
                    // along an axis shares one factor.
                    for (var arm = 0; arm < Arms; arm++)
                    {
                        var (di, dj, dk) = directions[arm];

                        neighbour[arm] = Reach(
                            mask, potential, cuts, i, j, k, di, dj, dk, arm,
                            out fraction[arm], out surface[arm]);
                    }

                    var alongX = 1.0 / (fraction[1] + fraction[0]);
                    var alongY = aspectY / (fraction[3] + fraction[2]);
                    var alongZ = aspectZ / (fraction[5] + fraction[4]);

                    var diagonal = 0.0;
                    var known = 0.0;

                    for (var arm = 0; arm < Arms; arm++)
                    {
                        var along = arm < 2 ? alongX : arm < 4 ? alongY : alongZ;
                        var coefficient = along / fraction[arm];

                        diagonal += coefficient;

                        if (neighbour[arm] >= 0)
                        {
                            built._arm[(node * Arms) + arm] = coefficient;
                        }
                        else
                        {
                            // A cut surface or a fixed neighbour: a known potential, so
                            // it belongs on the right-hand side rather than in the row.
                            known += coefficient * surface[arm];
                        }
                    }

                    built._diagonal[node] = diagonal;
                    built._known[node] = known;
                }
            }
        }

        return built;
    }

    /// <summary>
    /// Finds what an arm reaches: a free node, or a known potential.
    /// </summary>
    /// <returns>The free neighbour's index, or -1 where the value is known.</returns>
    private static int Reach(
        DirichletMask3D mask,
        ScalarField3D? potential,
        CutLinks3D? cuts,
        int i,
        int j,
        int k,
        int di,
        int dj,
        int dk,
        int arm,
        out double fraction,
        out double surface)
    {
        if (cuts is not null)
        {
            var cut = cuts.Fraction(i, j, k, (Arm3D)arm, out var onSurface);

            if (cut < 1.0)
            {
                fraction = cut;
                surface = onSurface;
                return -1;
            }
        }

        fraction = 1.0;
        surface = 0.0;

        var grid = mask.Grid;
        var ni = i + di;
        var nj = j + dj;
        var nk = k + dk;

        if (ni < 0 || nj < 0 || nk < 0 || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
        {
            var neumann = di > 0 ? mask.UpperX == EdgeCondition.Neumann
                : di < 0 ? mask.LowerX == EdgeCondition.Neumann
                : dj > 0 ? mask.UpperY == EdgeCondition.Neumann
                : dj < 0 ? mask.LowerY == EdgeCondition.Neumann
                : dk > 0 ? mask.UpperZ == EdgeCondition.Neumann
                : mask.LowerZ == EdgeCondition.Neumann;

            if (!neumann)
            {
                // A Dirichlet face no node holds falls back to zero, which the geometry
                // builder avoids by pinning the face itself.
                return -1;
            }

            // A mirror: the ghost equals its reflection inside, which is a real node.
            var ri = i - di;
            var rj = j - dj;
            var rk = k - dk;

            if (!mask.IsFixed(ri, rj, rk))
            {
                return grid.Index(ri, rj, rk);
            }

            surface = Value(mask, potential, ri, rj, rk);
            return -1;
        }

        if (mask.IsFixed(ni, nj, nk))
        {
            surface = Value(mask, potential, ni, nj, nk);
            return -1;
        }

        return grid.Index(ni, nj, nk);
    }

    private static double Value(
        DirichletMask3D mask, ScalarField3D? field, int i, int j, int k) =>
        field is null ? mask.Value(i, j, k) : field[i, j, k];
}
