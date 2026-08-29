namespace Einzel.Fields.Solved;

/// <summary>
/// The coarse levels of a V-cycle, built from the fine operator instead of the geometry.
/// </summary>
/// <remarks>
/// <para>
/// The finest level is untouched: it keeps its cut cells and its geometry-driven
/// smoother, because that is where the accuracy comes from and none of it is in
/// question. Everything below is <c>R A P</c>, which never looks at the geometry and so
/// cannot lose it - the failure that limits the rediscretised hierarchy to one or two
/// levels on any device geometry.
/// </para>
/// <para>
/// <b>The scaling is the part that is easy to get wrong, so it is worth stating.</b> The
/// fine equation is <c>-(A phi) = halfH2 * rhs</c>, and restricting it gives
/// <c>-(R A P) e = halfH2 * (R r)</c>. The <c>halfH2</c> that appears is the
/// <em>finest</em> level's, and it stays that way all the way down, because the coarse
/// operator inherited the fine operator's units rather than being rediscretised in its
/// own. A hierarchy that recomputed <c>halfH2</c> per level would be wrong by a factor
/// of four per level and would still converge - to something else.
/// </para>
/// <para>
/// <b>Twenty-seven points is closed under this coarsening.</b> Restriction reaches one
/// fine cell, the operator one more, prolongation one more, so a coarse row reaches at
/// most three fine cells - which is one and a half coarse cells, and therefore one. So
/// every level below the first has the same shape as the first, and the hierarchy needs
/// only one operator type.
/// </para>
/// </remarks>
public sealed class GalerkinHierarchy3D
{
    private readonly GalerkinOperator3D[] _levels;
    private readonly double _halfH2;

    private GalerkinHierarchy3D(GalerkinOperator3D[] levels, double halfH2)
    {
        _levels = levels;
        _halfH2 = halfH2;
    }

    /// <summary>How many coarse levels there are below the finest.</summary>
    public int Levels => _levels.Length;

    /// <summary>The grid of the coarsest level.</summary>
    public Grid3D Coarsest => _levels[^1].Grid;

    /// <summary>Smoothing sweeps taken over the whole hierarchy so far.</summary>
    public long Sweeps { get; private set; }

    /// <summary>Smoothing sweeps before descending and after correcting.</summary>
    private const int PreSmooth = 2;

    /// <summary>Smoothing sweeps after prolongation.</summary>
    private const int PostSmooth = 2;

    /// <summary>Sweeps at the bottom, where there is nowhere further to go.</summary>
    /// <remarks>
    /// A handful rather than the hundreds the rediscretised hierarchy needs, because
    /// this one actually reaches a small grid: the bottom is a few dozen nodes rather
    /// than the entire fine mesh.
    /// </remarks>
    private const int CoarsestSweeps = 24;

    /// <summary>
    /// Builds every coarse level from a fine operator, down to the smallest grid.
    /// </summary>
    /// <param name="fine">The fine operator, with the geometry already in it.</param>
    /// <param name="fineMask">The fine geometry, for which nodes are free.</param>
    /// <param name="halfH2">Half the square of the finest cell spacing.</param>
    /// <param name="floor">Stop coarsening at or below this many nodes.</param>
    /// <returns>The hierarchy, or null where the grid cannot coarsen at all.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <b>No geometry guard.</b> That is the whole point: the rediscretised hierarchy
    /// has to stop while a coarse cell still resolves the smallest electrode, because
    /// past that the coarse grid is solving a different problem. This one inherits the
    /// electrode through the coefficients, so it descends until the grid runs out.
    /// </remarks>
    public static GalerkinHierarchy3D? Build(
        OperatorStencil3D fine, DirichletMask3D fineMask, double halfH2, int floor = 64)
    {
        ArgumentNullException.ThrowIfNull(fine);
        ArgumentNullException.ThrowIfNull(fineMask);

        if (!fine.Grid.CanCoarsen)
        {
            return null;
        }

        var levels = new List<GalerkinOperator3D>(8)
        {
            GalerkinOperator3D.Form(fine, fineMask, fine.Grid.Coarsen()),
        };

        while (levels[^1].Grid.CanCoarsen && levels[^1].Grid.NodeCount > floor)
        {
            levels.Add(GalerkinOperator3D.Form(levels[^1], levels[^1].Grid.Coarsen()));
        }

        return new GalerkinHierarchy3D([.. levels], halfH2);
    }

    /// <summary>
    /// Restricts a fine residual onto the first coarse level and returns the correction.
    /// </summary>
    /// <param name="residual">The fine residual, in the units the solver stores it.</param>
    /// <param name="fineMask">The fine geometry.</param>
    /// <returns>The correction on the first coarse grid.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public ScalarField3D Correct(ScalarField3D residual, DirichletMask3D fineMask)
    {
        ArgumentNullException.ThrowIfNull(residual);
        ArgumentNullException.ThrowIfNull(fineMask);

        var coarse = _levels[0].Grid;

        // -(A e) = halfH2 * r, so the right-hand side carried down is halfH2 times the
        // restricted residual - with halfH2 the FINEST level's, at every level.
        var rhs = Restrict(residual, residual.Grid, coarse, fineMask);

        for (var n = 0; n < rhs.Values.Length; n++)
        {
            rhs.Values[n] *= _halfH2;
        }

        var correction = new ScalarField3D(coarse);

        Cycle(0, correction, rhs);

        return correction;
    }

    /// <summary>One V-cycle on a level of the hierarchy.</summary>
    private void Cycle(int level, ScalarField3D solution, ScalarField3D rhs)
    {
        var operatorAt = _levels[level];

        for (var sweep = 0; sweep < PreSmooth; sweep++)
        {
            Smooth(operatorAt, solution, rhs);
        }

        Sweeps += PreSmooth;

        if (level == _levels.Length - 1)
        {
            for (var sweep = 0; sweep < CoarsestSweeps; sweep++)
            {
                Smooth(operatorAt, solution, rhs);
            }

            Sweeps += CoarsestSweeps;

            return;
        }

        var residual = new ScalarField3D(operatorAt.Grid);

        Residual(operatorAt, solution, rhs, residual);

        var below = _levels[level + 1].Grid;

        // No halfH2 here: the operator already carries it, so restricting the residual
        // of one Galerkin level onto the next is a plain transfer.
        var coarseRhs = Restrict(residual, operatorAt.Grid, below, null);

        var correction = new ScalarField3D(below);

        Cycle(level + 1, correction, coarseRhs);

        Prolong(correction, solution, operatorAt);

        for (var sweep = 0; sweep < PostSmooth; sweep++)
        {
            Smooth(operatorAt, solution, rhs);
        }

        Sweeps += PostSmooth;
    }

    /// <summary>Red-black Gauss-Seidel on a twenty-seven point row.</summary>
    private static void Smooth(GalerkinOperator3D op, ScalarField3D solution, ScalarField3D rhs)
    {
        var grid = op.Grid;

        for (var colour = 0; colour < 2; colour++)
        {
            for (var k = 0; k < grid.CountZ; k++)
            {
                for (var j = 0; j < grid.CountY; j++)
                {
                    for (var i = 0; i < grid.CountX; i++)
                    {
                        if (((i + j + k) & 1) != colour)
                        {
                            continue;
                        }

                        var node = grid.Index(i, j, k);
                        var diagonal = op.Diagonal(node);

                        // A node with no equation: every fine node it would interpolate
                        // onto is fixed, so there is nothing here to solve for.
                        if (diagonal == 0.0)
                        {
                            continue;
                        }

                        // -(A e) = rhs, so e = -(rhs + off-diagonal terms) / diagonal.
                        var sum = rhs[i, j, k];

                        for (var entry = 0; entry < GalerkinOperator3D.Entries; entry++)
                        {
                            if (entry == GalerkinOperator3D.Centre)
                            {
                                continue;
                            }

                            var coefficient = op.Coefficient(node, entry);

                            if (coefficient == 0.0)
                            {
                                continue;
                            }

                            var (di, dj, dk) = GalerkinOperator3D.Offset(entry);

                            var ni = i + di;
                            var nj = j + dj;
                            var nk = k + dk;

                            if (ni < 0 || nj < 0 || nk < 0
                                || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
                            {
                                continue;
                            }

                            sum += coefficient * solution[ni, nj, nk];
                        }

                        solution[i, j, k] = -sum / diagonal;
                    }
                }
            }
        }
    }

    /// <summary>The residual of a twenty-seven point row.</summary>
    private static void Residual(
        GalerkinOperator3D op, ScalarField3D solution, ScalarField3D rhs, ScalarField3D into)
    {
        var grid = op.Grid;

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    var node = grid.Index(i, j, k);

                    if (op.Diagonal(node) == 0.0)
                    {
                        into[i, j, k] = 0.0;
                        continue;
                    }

                    var applied = 0.0;

                    for (var entry = 0; entry < GalerkinOperator3D.Entries; entry++)
                    {
                        var coefficient = op.Coefficient(node, entry);

                        if (coefficient == 0.0)
                        {
                            continue;
                        }

                        var (di, dj, dk) = GalerkinOperator3D.Offset(entry);

                        var ni = i + di;
                        var nj = j + dj;
                        var nk = k + dk;

                        if (ni < 0 || nj < 0 || nk < 0
                            || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
                        {
                            continue;
                        }

                        applied += coefficient * solution[ni, nj, nk];
                    }

                    // r = rhs - (-(A e)) = rhs + A e.
                    into[i, j, k] = rhs[i, j, k] + applied;
                }
            }
        }
    }

    /// <summary>
    /// Full-weighting restriction, the same one the coarse operator was formed with.
    /// </summary>
    /// <remarks>
    /// Written here rather than reused from the solver because it must match
    /// <c>GalerkinOperator3D</c>'s restriction exactly - the same support, the same
    /// weights, the same treatment of fixed nodes and edges. A residual transferred by
    /// one operator into a matrix formed with another is a mismatch that shows up as
    /// slow convergence rather than as a failure.
    /// </remarks>
    private static ScalarField3D Restrict(
        ScalarField3D residual, Grid3D fine, Grid3D coarse, DirichletMask3D? fineMask)
    {
        var result = new ScalarField3D(coarse);

        for (var ck = 0; ck < coarse.CountZ; ck++)
        {
            for (var cj = 0; cj < coarse.CountY; cj++)
            {
                for (var ci = 0; ci < coarse.CountX; ci++)
                {
                    var total = 0.0;

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
                                    || fi >= fine.CountX || fj >= fine.CountY || fk >= fine.CountZ)
                                {
                                    continue;
                                }

                                if (fineMask is not null && fineMask.IsFixed(fi, fj, fk))
                                {
                                    continue;
                                }

                                var weight = Weight(di) * Weight(dj) * Weight(dk) / 8.0;

                                total += weight * residual[fi, fj, fk];
                            }
                        }
                    }

                    result[ci, cj, ck] = total;
                }
            }
        }

        return result;
    }

    /// <summary>Trilinear prolongation onto a Galerkin level.</summary>
    private static void Prolong(
        ScalarField3D correction, ScalarField3D solution, GalerkinOperator3D op)
    {
        var fine = op.Grid;
        var coarse = correction.Grid;

        for (var k = 0; k < fine.CountZ; k++)
        {
            for (var j = 0; j < fine.CountY; j++)
            {
                for (var i = 0; i < fine.CountX; i++)
                {
                    if (op.Diagonal(fine.Index(i, j, k)) == 0.0)
                    {
                        continue;
                    }

                    solution[i, j, k] += Sample(correction, coarse, i, j, k);
                }
            }
        }
    }

    /// <summary>
    /// Prolongs a fine field onto the finest grid, where the mask decides what is free.
    /// </summary>
    /// <param name="correction">The correction on the first coarse level.</param>
    /// <param name="potential">The field to add it to.</param>
    /// <param name="mask">The fine geometry.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static void Apply(
        ScalarField3D correction, ScalarField3D potential, DirichletMask3D mask)
    {
        ArgumentNullException.ThrowIfNull(correction);
        ArgumentNullException.ThrowIfNull(potential);
        ArgumentNullException.ThrowIfNull(mask);

        var fine = mask.Grid;
        var coarse = correction.Grid;

        for (var k = 0; k < fine.CountZ; k++)
        {
            for (var j = 0; j < fine.CountY; j++)
            {
                for (var i = 0; i < fine.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        continue;
                    }

                    potential[i, j, k] += Sample(correction, coarse, i, j, k);
                }
            }
        }
    }

    private static double Sample(ScalarField3D correction, Grid3D coarse, int i, int j, int k)
    {
        var total = 0.0;

        for (var dk = -1; dk <= 1; dk++)
        {
            for (var dj = -1; dj <= 1; dj++)
            {
                for (var di = -1; di <= 1; di++)
                {
                    // The coarse nodes whose prolongation reaches this fine node are
                    // those at (i - di)/2 with the offset even, which is the same
                    // parity argument the operator's own restriction uses.
                    var ci = i - di;
                    var cj = j - dj;
                    var ck = k - dk;

                    if ((ci & 1) != 0 || (cj & 1) != 0 || (ck & 1) != 0)
                    {
                        continue;
                    }

                    ci /= 2;
                    cj /= 2;
                    ck /= 2;

                    if (ci < 0 || cj < 0 || ck < 0
                        || ci >= coarse.CountX || cj >= coarse.CountY || ck >= coarse.CountZ)
                    {
                        continue;
                    }

                    total += Weight(di) * Weight(dj) * Weight(dk) * correction[ci, cj, ck];
                }
            }
        }

        return total;
    }

    private static double Weight(int offset) => offset == 0 ? 1.0 : 0.5;
}
