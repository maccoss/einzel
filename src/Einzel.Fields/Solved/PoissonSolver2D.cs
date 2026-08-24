namespace Einzel.Fields.Solved;

/// <summary>What a solve achieved.</summary>
/// <param name="Converged">Whether the residual reached the requested tolerance.</param>
/// <param name="Cycles">V-cycles performed.</param>
/// <param name="InitialResidual">Residual norm before the first cycle.</param>
/// <param name="FinalResidual">Residual norm after the last cycle.</param>
/// <param name="ConvergenceFactor">
/// Residual reduction per cycle, averaged geometrically. A healthy geometric
/// multigrid sits near 0.1; a value approaching 1 means the cycle is not working
/// and the answer is being reached, if at all, by the smoother alone.
/// </param>
public sealed record SolveReport(
    bool Converged,
    int Cycles,
    double InitialResidual,
    double FinalResidual,
    double ConvergenceFactor);

/// <summary>
/// Geometric multigrid for Laplace's equation on a uniform Cartesian grid.
/// </summary>
/// <remarks>
/// <para>
/// The five-point stencil, red-black Gauss-Seidel smoothing, full-weighting
/// restriction, bilinear prolongation, V-cycles. Spec section 10 chooses
/// finite-difference multigrid for Phase 1 because it is "straightforward to
/// implement correctly, clean to parallelize and GPU-offload, naturally
/// accommodating of SYM-1".
/// </para>
/// <para>
/// Why multigrid rather than something simpler: Gauss-Seidel alone removes error
/// at the grid scale quickly and error at the domain scale barely at all, so its
/// iteration count grows with the square of the node count along an edge.
/// Multigrid carries the long-wavelength error to a coarse grid where it is
/// short-wavelength again, giving a residual reduction per cycle that does not
/// depend on the grid size. PERF-1 budgets thirty minutes for a full basis
/// campaign, and that budget is only reachable with a solver whose cost is linear
/// in the node count.
/// </para>
/// <para>
/// The correction scheme is used rather than full approximation storage: the
/// coarse grids solve for the error, so Dirichlet nodes carry a correction of
/// zero and electrode potentials never need to be represented on a coarse grid at
/// all.
/// </para>
/// </remarks>
public static class PoissonSolver2D
{
    /// <summary>
    /// How much of the fixed set a coarsening may lose before it is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A V-cycle is only useful while the coarse grid still poses the same
    /// problem. Interior electrodes — a rod, an aperture — occupy a fixed physical
    /// size, so each coarsening halves how many nodes represent them, and past a
    /// few levels they stop being represented at all. The coarse grid then
    /// computes a correction for a domain with no electrodes in it: not a worse
    /// approximation but a different one, and prolonging it back drives the
    /// iteration apart. Four discs in a box reached 1e134 V that way.
    /// </para>
    /// <para>
    /// The rule applies only to interior electrodes, and only they need it. A
    /// domain pinned on its edges alone coarsens all the way down safely — the
    /// manufactured-solution tests hold 8, 7, 7, 7 cycles from 32 to 256 intervals
    /// doing exactly that — so limiting every geometry would cost those cases
    /// their multigrid for nothing. Note that a total-node test does not work
    /// here: a disc loses three quarters of its nodes per level, which is
    /// precisely the rate healthy coarsening produces, so the ratio stays flat
    /// until the rod disappears and then it is too late.
    /// </para>
    /// </remarks>
    private const int MinimumInteriorFixedNodes = 128;

    private const int PreSmooth = 2;
    private const int PostSmooth = 2;
    private const int CoarseSmooth = 60;

    /// <summary>Solves Laplace's equation subject to a Dirichlet mask.</summary>
    /// <param name="mask">Fixed nodes and edge conditions.</param>
    /// <param name="tolerance">
    /// Relative residual to reach, against the residual of the initial guess.
    /// </param>
    /// <param name="maximumCycles">Ceiling on V-cycles.</param>
    /// <param name="initialGuess">
    /// Optional starting field. A previous solve on the same geometry makes a good
    /// one; otherwise the fixed potentials are smoothed into a zero interior.
    /// </param>
    /// <returns>The solved potential and a report on how it was reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mask"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tolerance or cycle ceiling is not positive.</exception>
    public static (ScalarField2D Potential, SolveReport Report) Solve(
        DirichletMask mask,
        double tolerance = 1e-10,
        int maximumCycles = 200,
        ScalarField2D? initialGuess = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tolerance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCycles);

        var grid = mask.Grid;
        var potential = initialGuess?.Clone() ?? new ScalarField2D(grid);
        mask.ApplyTo(potential);

        var rightHandSide = new ScalarField2D(grid);
        var residual = new ScalarField2D(grid);

        var initial = Residual(potential, rightHandSide, mask, residual);

        // A geometry with no free nodes, or one already solved, is not an error.
        if (initial == 0.0)
        {
            return (potential, new SolveReport(true, 0, 0.0, 0.0, 0.0));
        }

        var current = initial;
        var cycles = 0;

        for (; cycles < maximumCycles; cycles++)
        {
            VCycle(potential, rightHandSide, mask);
            current = Residual(potential, rightHandSide, mask, residual);

            if (current <= tolerance * initial)
            {
                cycles++;
                break;
            }
        }

        var factor = cycles > 0 && initial > 0.0
            ? Math.Pow(current / initial, 1.0 / cycles)
            : 0.0;

        return (potential, new SolveReport(current <= tolerance * initial, cycles, initial, current, factor));
    }

    private static void VCycle(ScalarField2D potential, ScalarField2D rightHandSide, DirichletMask mask)
    {
        var grid = potential.Grid;

        if (!grid.CanCoarsen)
        {
            Smooth(potential, rightHandSide, mask, CoarseSmooth);
            return;
        }

        var coarseMask = mask.Coarsen();

        // Refuse a coarsening that would dissolve interior electrodes rather than
        // represent them more coarsely.
        if (mask.InteriorFixedCount > 0 && coarseMask.InteriorFixedCount < MinimumInteriorFixedNodes)
        {
            Smooth(potential, rightHandSide, mask, CoarseSmooth);
            return;
        }

        Smooth(potential, rightHandSide, mask, PreSmooth);

        var residual = new ScalarField2D(grid);
        Residual(potential, rightHandSide, mask, residual);

        var coarseRhs = Restrict(residual, coarseMask);
        var coarseCorrection = new ScalarField2D(coarseMask.Grid);

        VCycle(coarseCorrection, coarseRhs, coarseMask);

        Prolong(coarseCorrection, potential, mask);
        Smooth(potential, rightHandSide, mask, PostSmooth);
    }

    /// <summary>
    /// Red-black Gauss-Seidel. Red and black sweeps are each independent within
    /// themselves, which is why this smoother and not lexicographic ordering:
    /// spec section 6 puts a SIMD and GPU dispatch layer under the engine, and a
    /// smoother that cannot be parallelised would have to be replaced later.
    /// </summary>
    private static void Smooth(
        ScalarField2D potential, ScalarField2D rightHandSide, DirichletMask mask, int sweeps)
    {
        var grid = potential.Grid;
        var h2 = grid.Spacing * grid.Spacing;

        for (var sweep = 0; sweep < sweeps; sweep++)
        {
            for (var colour = 0; colour < 2; colour++)
            {
                for (var j = 0; j < grid.CountY; j++)
                {
                    for (var i = (j + colour) % 2; i < grid.CountX; i += 2)
                    {
                        if (mask.IsFixed(i, j))
                        {
                            continue;
                        }

                        var (sum, weight) = Neighbours(potential, mask, i, j);
                        potential[i, j] = (sum - (h2 * rightHandSide[i, j])) / weight;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The five-point neighbour sum, with Neumann edges handled by reflection.
    /// </summary>
    /// <remarks>
    /// A zero-derivative edge is a mirror plane, so the ghost node outside it
    /// equals its reflection inside, which shows up as the interior neighbour
    /// counted twice. That keeps the stencil second-order at the edge rather than
    /// dropping to first, and the difference matters: a one-sided first-order edge
    /// would put a first-order error into an otherwise second-order solve and cap
    /// the convergence order the whole grid can show.
    /// </remarks>
    private static (double Sum, double Weight) Neighbours(
        ScalarField2D potential, DirichletMask mask, int i, int j)
    {
        var grid = potential.Grid;
        var sum = 0.0;

        sum += i > 0 ? potential[i - 1, j]
            : mask.LeftEdge == EdgeCondition.Neumann ? potential[i + 1, j] : 0.0;

        sum += i < grid.CountX - 1 ? potential[i + 1, j]
            : mask.RightEdge == EdgeCondition.Neumann ? potential[i - 1, j] : 0.0;

        sum += j > 0 ? potential[i, j - 1]
            : mask.BottomEdge == EdgeCondition.Neumann ? potential[i, j + 1] : 0.0;

        sum += j < grid.CountY - 1 ? potential[i, j + 1]
            : mask.TopEdge == EdgeCondition.Neumann ? potential[i, j - 1] : 0.0;

        return (sum, 4.0);
    }

    /// <summary>Computes the residual and returns its root-mean-square norm.</summary>
    private static double Residual(
        ScalarField2D potential, ScalarField2D rightHandSide, DirichletMask mask, ScalarField2D residual)
    {
        var grid = potential.Grid;
        var inverseH2 = 1.0 / (grid.Spacing * grid.Spacing);
        var sum = 0.0;
        var count = 0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (mask.IsFixed(i, j))
                {
                    residual[i, j] = 0.0;
                    continue;
                }

                var (neighbours, weight) = Neighbours(potential, mask, i, j);
                var laplacian = (neighbours - (weight * potential[i, j])) * inverseH2;
                var value = rightHandSide[i, j] - laplacian;

                residual[i, j] = value;
                sum += value * value;
                count++;
            }
        }

        return count == 0 ? 0.0 : Math.Sqrt(sum / count);
    }

    /// <summary>Full-weighting restriction onto the coarse grid.</summary>
    private static ScalarField2D Restrict(ScalarField2D fine, DirichletMask coarseMask)
    {
        var coarseGrid = coarseMask.Grid;
        var coarse = new ScalarField2D(coarseGrid);
        var fineGrid = fine.Grid;

        for (var j = 0; j < coarseGrid.CountY; j++)
        {
            for (var i = 0; i < coarseGrid.CountX; i++)
            {
                if (coarseMask.IsFixed(i, j))
                {
                    continue;
                }

                var fi = i * 2;
                var fj = j * 2;

                // Full weighting: 1/4 centre, 1/8 each edge neighbour, 1/16 each
                // diagonal. Simple injection would alias high-frequency residual
                // onto the coarse grid and stall the cycle.
                var value = 0.25 * fine[fi, fj];

                value += 0.125 * (Sample(fine, fi - 1, fj) + Sample(fine, fi + 1, fj)
                    + Sample(fine, fi, fj - 1) + Sample(fine, fi, fj + 1));

                value += 0.0625 * (Sample(fine, fi - 1, fj - 1) + Sample(fine, fi + 1, fj - 1)
                    + Sample(fine, fi - 1, fj + 1) + Sample(fine, fi + 1, fj + 1));

                coarse[i, j] = value;
            }
        }

        return coarse;

        static double Sample(ScalarField2D field, int i, int j) =>
            i < 0 || j < 0 || i >= field.Grid.CountX || j >= field.Grid.CountY ? 0.0 : field[i, j];
    }

    /// <summary>Bilinear prolongation, added into the fine field at free nodes.</summary>
    private static void Prolong(ScalarField2D coarse, ScalarField2D fine, DirichletMask fineMask)
    {
        var fineGrid = fine.Grid;
        var coarseGrid = coarse.Grid;

        for (var j = 0; j < fineGrid.CountY; j++)
        {
            for (var i = 0; i < fineGrid.CountX; i++)
            {
                if (fineMask.IsFixed(i, j))
                {
                    continue;
                }

                var ci = i / 2;
                var cj = j / 2;
                var oddI = (i & 1) == 1;
                var oddJ = (j & 1) == 1;

                double correction;

                if (!oddI && !oddJ)
                {
                    correction = coarse[ci, cj];
                }
                else if (oddI && !oddJ)
                {
                    correction = 0.5 * (coarse[ci, cj] + coarse[Math.Min(ci + 1, coarseGrid.CountX - 1), cj]);
                }
                else if (!oddI && oddJ)
                {
                    correction = 0.5 * (coarse[ci, cj] + coarse[ci, Math.Min(cj + 1, coarseGrid.CountY - 1)]);
                }
                else
                {
                    var i1 = Math.Min(ci + 1, coarseGrid.CountX - 1);
                    var j1 = Math.Min(cj + 1, coarseGrid.CountY - 1);
                    correction = 0.25 * (coarse[ci, cj] + coarse[i1, cj] + coarse[ci, j1] + coarse[i1, j1]);
                }

                fine[i, j] += correction;
            }
        }
    }
}
