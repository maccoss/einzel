using System.Numerics;
using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>
/// Turns the electrode geometry declared in a model document into a grid, a
/// Dirichlet mask, and a solve.
/// </summary>
/// <remarks>
/// <para>
/// The seam that makes LIB-1 true. Before this existed, a mirror was a C# class
/// and a quadrupole would have been another one; now both are documents naming
/// the same three primitives in different places, and adding a device requires no
/// change below Einzel.Library — which is exactly the test LIB-1 sets.
/// </para>
/// <para>
/// Nothing here knows what any arrangement is for. It rasterises shapes onto a
/// grid and hands the result to the solver.
/// </para>
/// </remarks>
public static class GeometryBuilder
{
    /// <summary>Builds the grid a declared domain calls for.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <returns>The grid, spanning the declared box with power-of-two interval counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Each axis gets its own interval count, from the same requested cell size,
    /// rounded up to a power of two so coarsening is exact. Two consequences,
    /// both wanted. The spacing is at least as fine as asked for in <em>both</em>
    /// directions and never coarser. And the grid spans exactly the declared box,
    /// rather than whatever box a single spacing happened to reach.
    /// </para>
    /// <para>
    /// The cost is that cells need not be square. Since both spacings lie in the
    /// half-open interval from half the requested cell size to the cell size, the
    /// worst anisotropy is two to one - fine for a point smoother, and much
    /// cheaper than the alternative, which was silently solving a different
    /// domain.
    /// </para>
    /// </remarks>
    public static Grid2D BuildGrid(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        return Grid2D.OverBox(
            solve.MinX,
            solve.MinY,
            solve.MaxX,
            solve.MaxY,
            Intervals(solve.MaxX - solve.MinX, solve.CellSize),
            Intervals(solve.MaxY - solve.MinY, solve.CellSize));
    }

    private static int Intervals(double extent, double cellSize) =>
        (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(4, (int)Math.Ceiling(extent / cellSize)));

    /// <summary>Rasterises the declared electrodes onto a mask.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <param name="grid">The grid to rasterise onto.</param>
    /// <param name="potentialOf">
    /// Optional override of each electrode's potential, by name. Used to build
    /// basis fields, where one electrode sits at one volt and the rest at zero.
    /// </param>
    /// <returns>The mask.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static DirichletMask BuildMask(
        CompiledSolvedField solve, Grid2D grid, Func<CompiledElectrode, double>? potentialOf = null)
    {
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(grid);

        var mask = new DirichletMask(grid)
        {
            LeftEdge = Translate(solve.LeftEdge),
            RightEdge = Translate(solve.RightEdge),
            BottomEdge = Translate(solve.BottomEdge),
            TopEdge = Translate(solve.TopEdge),
        };

        foreach (var electrode in solve.Electrodes)
        {
            var potential = potentialOf?.Invoke(electrode) ?? electrode.Potential;

            switch (electrode.Shape)
            {
                case ElectrodeShape.Rectangle:
                    RasteriseRectangle(mask, grid, electrode, potential);
                    break;

                case ElectrodeShape.Disc:
                    RasteriseDisc(mask, grid, electrode, potential);
                    break;

                case ElectrodeShape.EdgeProfile:
                    RasteriseEdgeProfile(mask, grid, electrode, potentialOf is null ? 1.0 : potential);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(solve), electrode.Shape, "unhandled electrode shape");
            }
        }

        PinDirichletEdges(mask, grid);
        AddCuts(solve, grid, mask, potentialOf);

        return mask;
    }

    /// <summary>
    /// Grounds every node on a Dirichlet domain edge that no electrode has already
    /// claimed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Dirichlet edge has to mean the potential is zero <em>on the edge</em>. The
    /// alternative reading — a ghost node one cell outside the grid held at zero,
    /// with the edge node itself solved — is self-consistent on any single grid,
    /// and it was what the solver did. It is wrong the moment there is more than
    /// one grid: the ghost sits one cell out, so the boundary is one cell out at
    /// the fine level, two at the next, four at the next. Every level of a V-cycle
    /// then solves a slightly larger domain than the one above it, and a coarse
    /// correction computed on the wrong domain does not correct anything.
    /// </para>
    /// <para>
    /// It diverged rather than merely converging slowly: 1e50 V on a cap plate in
    /// a grounded box. The reason it went unnoticed is that the coarsening limit
    /// happened to stop these geometries before they reached a second level, so
    /// the solver fell back on plain Gauss-Seidel and reported a convergence
    /// factor of 0.84 — poor, but not obviously a bug.
    /// </para>
    /// <para>
    /// Electrodes are rasterised first and are not overwritten, so a plate that
    /// reaches the edge of the domain still holds the edge.
    /// </para>
    /// </remarks>
    private static void PinDirichletEdges(DirichletMask mask, Grid2D grid)
    {
        for (var i = 0; i < grid.CountX; i++)
        {
            if (mask.BottomEdge == EdgeCondition.Dirichlet && !mask.IsFixed(i, 0))
            {
                mask.Fix(i, 0, 0.0);
            }

            if (mask.TopEdge == EdgeCondition.Dirichlet && !mask.IsFixed(i, grid.CountY - 1))
            {
                mask.Fix(i, grid.CountY - 1, 0.0);
            }
        }

        for (var j = 0; j < grid.CountY; j++)
        {
            if (mask.LeftEdge == EdgeCondition.Dirichlet && !mask.IsFixed(0, j))
            {
                mask.Fix(0, j, 0.0);
            }

            if (mask.RightEdge == EdgeCondition.Dirichlet && !mask.IsFixed(grid.CountX - 1, j))
            {
                mask.Fix(grid.CountX - 1, j, 0.0);
            }
        }
    }

    /// <summary>
    /// Records where each electrode surface crosses between nodes, so the solver
    /// can place the boundary where it is rather than at the nearest node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately independent of which nodes were rasterised. Asking "is my
    /// neighbour a fixed node?" would tie the sub-cell boundary back to the
    /// staircase it exists to remove, and would miss an electrode thinner than a
    /// cell entirely — which is every coarse multigrid level of a thin plate.
    /// Asking the geometry directly finds the surface whether or not any node
    /// happens to lie behind it.
    /// </para>
    /// <para>
    /// A node may be cut by more than one electrode in the same direction, at a
    /// gap between two plates narrower than a cell. The nearest one wins, because
    /// it is the one the stencil can see; the far one is in shadow behind a
    /// conductor.
    /// </para>
    /// </remarks>
    private static void AddCuts(
        CompiledSolvedField solve, Grid2D grid, DirichletMask mask, Func<CompiledElectrode, double>? potentialOf)
    {
        var cuts = new CutLinks(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (mask.IsFixed(i, j))
                {
                    continue;
                }

                CutTowards(solve, grid, cuts, potentialOf, i, j, 1, 0, StencilDirection.East);
                CutTowards(solve, grid, cuts, potentialOf, i, j, -1, 0, StencilDirection.West);
                CutTowards(solve, grid, cuts, potentialOf, i, j, 0, 1, StencilDirection.North);
                CutTowards(solve, grid, cuts, potentialOf, i, j, 0, -1, StencilDirection.South);
            }
        }

        mask.Cuts = cuts.HasCuts ? cuts : null;
    }

    private static void CutTowards(
        CompiledSolvedField solve,
        Grid2D grid,
        CutLinks cuts,
        Func<CompiledElectrode, double>? potentialOf,
        int i,
        int j,
        int di,
        int dj,
        StencilDirection direction)
    {
        var ni = i + di;
        var nj = j + dj;

        if (ni < 0 || nj < 0 || ni >= grid.CountX || nj >= grid.CountY)
        {
            return;
        }

        var fromX = grid.X(i);
        var fromY = grid.Y(j);
        var toX = grid.X(ni);
        var toY = grid.Y(nj);

        var nearest = 1.0;
        var potential = 0.0;
        var found = false;

        foreach (var electrode in solve.Electrodes)
        {
            if (electrode.FirstEntry(fromX, fromY, toX, toY) is not { } entry || entry >= nearest)
            {
                continue;
            }

            // A surface at zero would put the node itself on the conductor, where
            // rasterisation should already have fixed it. Ignoring it keeps a
            // rounding disagreement between the two from producing a stencil with
            // no extent at all.
            if (entry <= 0.0)
            {
                continue;
            }

            nearest = entry;
            potential = potentialOf?.Invoke(electrode) ?? electrode.Potential;
            found = true;
        }

        if (found)
        {
            cuts.Cut(i, j, direction, nearest, potential);
        }
    }

    private static EdgeCondition Translate(BoundaryKind kind) =>
        kind == BoundaryKind.Neumann ? EdgeCondition.Neumann : EdgeCondition.Dirichlet;

    private static void RasteriseRectangle(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double potential)
    {
        // Half-open in index space but inclusive in coordinate space: a node lying
        // on the boundary of the rectangle belongs to it, so two abutting
        // electrodes share their contact nodes rather than leaving a gap.
        var i0 = (int)Math.Ceiling((electrode.MinX - grid.OriginX) / grid.SpacingX);
        var i1 = (int)Math.Floor((electrode.MaxX - grid.OriginX) / grid.SpacingX);
        var j0 = (int)Math.Ceiling((electrode.MinY - grid.OriginY) / grid.SpacingY);
        var j1 = (int)Math.Floor((electrode.MaxY - grid.OriginY) / grid.SpacingY);

        mask.FixRectangle(i0, j0, i1, j1, potential);
    }

    private static void RasteriseDisc(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double potential)
    {
        var radiusSquared = electrode.Radius * electrode.Radius;

        var i0 = Math.Max(0, (int)Math.Floor((electrode.CentreX - electrode.Radius - grid.OriginX) / grid.SpacingX));
        var i1 = Math.Min(grid.CountX - 1,
            (int)Math.Ceiling((electrode.CentreX + electrode.Radius - grid.OriginX) / grid.SpacingX));
        var j0 = Math.Max(0, (int)Math.Floor((electrode.CentreY - electrode.Radius - grid.OriginY) / grid.SpacingY));
        var j1 = Math.Min(grid.CountY - 1,
            (int)Math.Ceiling((electrode.CentreY + electrode.Radius - grid.OriginY) / grid.SpacingY));

        for (var j = j0; j <= j1; j++)
        {
            var dy = grid.Y(j) - electrode.CentreY;

            for (var i = i0; i <= i1; i++)
            {
                var dx = grid.X(i) - electrode.CentreX;

                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    mask.Fix(i, j, potential);
                }
            }
        }
    }

    /// <summary>
    /// Fixes an entire domain edge to a potential that varies along it.
    /// </summary>
    /// <remarks>
    /// The <paramref name="scale"/> multiplies the profile, so a basis solve can
    /// raise a whole printed board to unit potential without restating its shape.
    /// A profile is one electrode even though it spans many nodes, because that is
    /// how it is driven: one supply feeding a resistive divider.
    /// </remarks>
    private static void RasteriseEdgeProfile(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double scale)
    {
        switch (electrode.Edge)
        {
            case GridEdge.Bottom:
            case GridEdge.Top:
            {
                var j = electrode.Edge == GridEdge.Bottom ? 0 : grid.CountY - 1;

                for (var i = 0; i < grid.CountX; i++)
                {
                    mask.Fix(i, j, scale * electrode.ProfileAt(grid.X(i)));
                }

                break;
            }

            case GridEdge.Left:
            case GridEdge.Right:
            {
                var i = electrode.Edge == GridEdge.Left ? 0 : grid.CountX - 1;

                for (var j = 0; j < grid.CountY; j++)
                {
                    mask.Fix(i, j, scale * electrode.ProfileAt(grid.Y(j)));
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(electrode), electrode.Edge, "unhandled edge");
        }
    }

    /// <summary>Builds, solves, and wraps a declared geometry as a field.</summary>
    /// <param name="solve">The declared geometry.</param>
    /// <returns>The field, and how the solve went.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    public static (IElectrostaticField Field, SolveReport Report) Build(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        var grid = BuildGrid(solve);
        var mask = BuildMask(solve, grid);
        var (potential, report) = PoissonSolver2D.Solve(
            mask, solve.Tolerance, maximumCycles: 400, coarsen: coarse => BuildMask(solve, coarse));

        IElectrostaticField field = new SolvedField2D(
            potential,
            new BicubicInterpolant(potential),
            boundaryIsDiscontinuous: solve.BoundaryIsDiscontinuous);

        if (solve.ReflectAboutX is { } plane)
        {
            // The reflected half is the same solve seen backwards, so the two are
            // identical by construction and no difference between them can come
            // from their having been meshed differently.
            field = new SuperposedField([field, new ReflectedField(field, plane)]);
        }

        return (field, report);
    }
}
