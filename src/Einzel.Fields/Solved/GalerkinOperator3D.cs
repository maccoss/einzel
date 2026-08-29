namespace Einzel.Fields.Solved;

/// <summary>
/// A coarse operator built from the fine operator rather than from the geometry again.
/// </summary>
/// <remarks>
/// <para>
/// <c>A_coarse = R A_fine P</c>. Rediscretising on a coarse grid asks the geometry what
/// it looks like at that spacing, and past a point the answer is "a different shape":
/// a 1 mm slab four levels down is smaller than a cell and gets pinned to a single
/// node, so the coarse problem constrains the error at two isolated points where the
/// fine problem constrains it over two whole planes. The correction that comes back
/// solves a different problem, and prolonging it was measured putting <b>486 V of 100
/// applied</b> into the field while reporting converged.
/// </para>
/// <para>
/// The triple product cannot do that. It never looks at the geometry - it inherits
/// whatever the fine operator says, including where the fine operator has no free
/// neighbour because a conductor is there. A slab that the coarse grid could not
/// rasterise is still a slab in the coefficients.
/// </para>
/// <para>
/// <b>The transfer pair is variational, which is what makes the product the right
/// operator with no rescaling.</b> The solver's restriction is the separable 1-2-1
/// kernel over 27 nodes normalised to sum to one, so its weights are
/// 1/8, 1/16, 1/32, 1/64 by neighbour class; trilinear prolongation scaled by 1/8 gives
/// the same four numbers. So <c>R = (1/8) P^T</c> exactly, which is the pairing under
/// which <c>R A P</c> is the Galerkin coarse operator rather than an approximation to
/// one.
/// </para>
/// <para>
/// The result is a <b>27-point</b> stencil: a coarse node's row reaches every coarse
/// neighbour within one cell in each direction, because restriction spreads over 27
/// fine nodes, each seven-point row reaches one further, and prolongation gathers back
/// over eight coarse nodes.
/// </para>
/// </remarks>
public sealed class GalerkinOperator3D
{
    /// <summary>Entries in a coarse row: every neighbour within one cell.</summary>
    public const int Entries = 27;

    private readonly double[] _coefficient;

    private GalerkinOperator3D(Grid3D grid)
    {
        Grid = grid;

        _coefficient = new double[grid.NodeCount * Entries];
    }

    /// <summary>The grid this operator is over.</summary>
    public Grid3D Grid { get; }

    /// <summary>
    /// The offsets a row reaches, in the order coefficients are stored.
    /// </summary>
    /// <remarks>
    /// Index 13 is the centre, which is the diagonal. The order is the natural
    /// <c>dk</c>, <c>dj</c>, <c>di</c> nesting from -1 to +1.
    /// </remarks>
    public static (int Di, int Dj, int Dk) Offset(int entry) =>
        ((entry % 3) - 1, ((entry / 3) % 3) - 1, (entry / 9) - 1);

    /// <summary>Where the centre of a row sits in the entry order.</summary>
    public const int Centre = 13;

    /// <summary>A coefficient of a coarse row.</summary>
    /// <param name="node">The coarse node index.</param>
    /// <param name="entry">Which entry, indexing <see cref="Offset"/>.</param>
    /// <returns>The coefficient, signed as the fine operator is.</returns>
    public double Coefficient(int node, int entry) => _coefficient[(node * Entries) + entry];

    /// <summary>The diagonal of a coarse row.</summary>
    /// <param name="node">The coarse node index.</param>
    /// <returns>The centre coefficient.</returns>
    public double Diagonal(int node) => _coefficient[(node * Entries) + Centre];

    /// <summary>
    /// Forms <c>R A P</c> from a fine operator and the coarse grid below it.
    /// </summary>
    /// <param name="fine">The fine operator.</param>
    /// <param name="fineMask">The fine geometry, for which nodes are free.</param>
    /// <param name="coarse">The coarse grid.</param>
    /// <returns>The coarse operator.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// Assembled column by column rather than row by row: for each coarse node the
    /// prolongation of a unit coarse vector is formed, the fine operator applied to it,
    /// and the result restricted. That is the same product read in the cheap direction -
    /// prolongation is sparse and easy to enumerate forwards, while enumerating which
    /// fine nodes restrict into a given coarse node and then which coarse nodes
    /// prolonged into their neighbours is the same arithmetic written backwards.
    /// </remarks>
    public static GalerkinOperator3D Form(
        OperatorStencil3D fine, DirichletMask3D fineMask, Grid3D coarse)
    {
        ArgumentNullException.ThrowIfNull(fine);
        ArgumentNullException.ThrowIfNull(fineMask);
        ArgumentNullException.ThrowIfNull(coarse);

        var built = new GalerkinOperator3D(coarse);

        var fineGrid = fine.Grid;

        var column = new double[fineGrid.NodeCount];
        var applied = new double[fineGrid.NodeCount];

        // Generation stamps rather than clearing whole arrays per column: a column
        // touches a few dozen nodes out of millions, so clearing would dominate.
        var stamp = new int[fineGrid.NodeCount];
        var generation = 0;

        var touched = new List<int>(32);
        var rows = new List<int>(256);

        for (var ck = 0; ck < coarse.CountZ; ck++)
        {
            for (var cj = 0; cj < coarse.CountY; cj++)
            {
                for (var ci = 0; ci < coarse.CountX; ci++)
                {
                    generation++;

                    touched.Clear();
                    rows.Clear();

                    Spread(column, touched, fineGrid, fineMask, ci, cj, ck);

                    Rows(rows, stamp, generation, fineGrid, fineMask, touched);

                    Evaluate(column, applied, rows, fine);

                    Gather(built, applied, rows, fineGrid, coarse, ci, cj, ck);

                    foreach (var node in touched)
                    {
                        column[node] = 0.0;
                    }
                }
            }
        }

        return built;
    }

    /// <summary>Prolongs a unit coarse vector onto the fine grid.</summary>
    /// <remarks>
    /// A fixed fine node holds no correction, so prolongation skips it - exactly as the
    /// solver's own does, and for the same reason: the error is zero on a conductor and
    /// interpolating a value there would invent one.
    /// </remarks>
    private static void Spread(
        double[] column,
        List<int> touched,
        Grid3D fineGrid,
        DirichletMask3D fineMask,
        int ci,
        int cj,
        int ck)
    {
        for (var dk = -1; dk <= 1; dk++)
        {
            for (var dj = -1; dj <= 1; dj++)
            {
                for (var di = -1; di <= 1; di++)
                {
                    var fi = (2 * ci) + di;
                    var fj = (2 * cj) + dj;
                    var fk = (2 * ck) + dk;

                    if (fi < 0 || fj < 0 || fk < 0
                        || fi >= fineGrid.CountX || fj >= fineGrid.CountY || fk >= fineGrid.CountZ)
                    {
                        continue;
                    }

                    if (fineMask.IsFixed(fi, fj, fk))
                    {
                        continue;
                    }

                    var node = fineGrid.Index(fi, fj, fk);

                    column[node] = Weight(di) * Weight(dj) * Weight(dk);
                    touched.Add(node);
                }
            }
        }
    }

    /// <summary>
    /// The fine rows whose value can be non-zero: the prolonged nodes and their free
    /// neighbours.
    /// </summary>
    private static void Rows(
        List<int> rows,
        int[] stamp,
        int generation,
        Grid3D grid,
        DirichletMask3D mask,
        List<int> touched)
    {
        var directions = OperatorStencil3D.Directions;

        foreach (var seed in touched)
        {
            Mark(rows, stamp, generation, seed);

            var (si, sj, sk) = Unpack(grid, seed);

            for (var arm = 0; arm < OperatorStencil3D.Arms; arm++)
            {
                var (di, dj, dk) = directions[arm];

                var ni = si + di;
                var nj = sj + dj;
                var nk = sk + dk;

                // An arm leaving the grid is a Neumann mirror onto the reflected node,
                // which is already a seed or will be reached from one.
                if (ni < 0 || nj < 0 || nk < 0
                    || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
                {
                    continue;
                }

                if (mask.IsFixed(ni, nj, nk))
                {
                    continue;
                }

                Mark(rows, stamp, generation, grid.Index(ni, nj, nk));
            }
        }
    }

    private static void Mark(List<int> rows, int[] stamp, int generation, int node)
    {
        if (stamp[node] == generation)
        {
            return;
        }

        stamp[node] = generation;
        rows.Add(node);
    }

    /// <summary>Applies the fine operator to the prolonged column.</summary>
    private static void Evaluate(
        double[] column,
        double[] applied,
        List<int> rows,
        OperatorStencil3D fine)
    {
        var grid = fine.Grid;
        var directions = OperatorStencil3D.Directions;


        foreach (var node in rows)
        {
            var (i, j, k) = Unpack(grid, node);

            // The row in the fine operator's own convention: the diagonal on this node
            // less each arm on its free neighbour. Known values contribute nothing to a
            // matrix-vector product - they are right-hand side, not matrix.
            var value = fine.Diagonal(node) * column[node];

            for (var arm = 0; arm < OperatorStencil3D.Arms; arm++)
            {
                var coefficient = fine.Arm(node, arm);

                if (coefficient == 0.0)
                {
                    continue;
                }

                var (di, dj, dk) = directions[arm];

                var ni = i + di;
                var nj = j + dj;
                var nk = k + dk;

                if (ni < 0 || nj < 0 || nk < 0
                    || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
                {
                    ni = i - di;
                    nj = j - dj;
                    nk = k - dk;
                }

                value -= coefficient * column[grid.Index(ni, nj, nk)];
            }

            applied[node] = value;
        }
    }

    /// <summary>Restricts the applied column onto the coarse rows it reaches.</summary>
    /// <remarks>
    /// <para>
    /// Which coarse rows a fine node restricts into follows from parity and needs no
    /// search. A coarse row at <c>r</c> covers fine nodes <c>2r-1</c>, <c>2r</c> and
    /// <c>2r+1</c> along each axis, so an <b>even</b> fine index is covered by exactly
    /// one coarse index and an <b>odd</b> one by exactly two.
    /// </para>
    /// <para>
    /// The weight is the solver's own restriction: the separable 1-2-1 kernel normalised
    /// to sum to one, which is <c>1/8</c> times the trilinear weights - the variational
    /// pairing <c>R = (1/8) P^T</c> that makes this product the Galerkin operator.
    /// </para>
    /// </remarks>
    private static void Gather(
        GalerkinOperator3D built,
        double[] applied,
        List<int> rows,
        Grid3D fineGrid,
        Grid3D coarse,
        int ci,
        int cj,
        int ck)
    {
        Span<int> alongI = stackalloc int[2];
        Span<int> alongJ = stackalloc int[2];
        Span<int> alongK = stackalloc int[2];

        foreach (var node in rows)
        {
            var value = applied[node];

            if (value == 0.0)
            {
                continue;
            }

            var (fi, fj, fk) = Unpack(fineGrid, node);

            var countI = Covering(fi, coarse.CountX, alongI);
            var countJ = Covering(fj, coarse.CountY, alongJ);
            var countK = Covering(fk, coarse.CountZ, alongK);

            for (var a = 0; a < countK; a++)
            {
                var rk = alongK[a];
                var entryK = ck - rk;

                if (Math.Abs(entryK) > 1)
                {
                    continue;
                }

                for (var b = 0; b < countJ; b++)
                {
                    var rj = alongJ[b];
                    var entryJ = cj - rj;

                    if (Math.Abs(entryJ) > 1)
                    {
                        continue;
                    }

                    for (var c = 0; c < countI; c++)
                    {
                        var ri = alongI[c];
                        var entryI = ci - ri;

                        if (Math.Abs(entryI) > 1)
                        {
                            continue;
                        }

                        var weight = Weight(fi - (2 * ri))
                            * Weight(fj - (2 * rj))
                            * Weight(fk - (2 * rk))
                            / 8.0;

                        var row = coarse.Index(ri, rj, rk);
                        var entry = ((entryK + 1) * 9) + ((entryJ + 1) * 3) + entryI + 1;

                        built._coefficient[(row * Entries) + entry] += weight * value;
                    }
                }
            }

            applied[node] = 0.0;
        }
    }

    /// <summary>The coarse indices along one axis that cover a fine index.</summary>
    private static int Covering(int fine, int count, Span<int> into)
    {
        if ((fine & 1) == 0)
        {
            into[0] = fine / 2;

            return into[0] < count ? 1 : 0;
        }

        var lower = (fine - 1) / 2;
        var upper = lower + 1;

        into[0] = lower;

        if (upper >= count)
        {
            return 1;
        }

        into[1] = upper;

        return 2;
    }

    /// <summary>The trilinear weight of a fine node an offset from a coarse one.</summary>
    private static double Weight(int offset) => offset == 0 ? 1.0 : 0.5;

    private static (int I, int J, int K) Unpack(Grid3D grid, int node)
    {
        var i = node % grid.CountX;
        var rest = node / grid.CountX;

        return (i, rest % grid.CountY, rest / grid.CountY);
    }
}
