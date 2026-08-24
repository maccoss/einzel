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
    /// <returns>The grid, with square cells and power-of-two interval counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    /// <remarks>
    /// The requested cell size is honoured as closely as the multigrid coarsening
    /// allows: interval counts are rounded up to a power of two, so the actual
    /// spacing is at least as fine as asked for and never coarser.
    /// </remarks>
    public static Grid2D BuildGrid(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        var width = solve.MaxX - solve.MinX;
        var intervalsX = (int)BitOperations.RoundUpToPowerOf2(
            (uint)Math.Max(4, (int)Math.Ceiling(width / solve.CellSize)));

        return Grid2D.OverBox(solve.MinX, solve.MinY, solve.MaxX, solve.MaxY, intervalsX);
    }

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

        return mask;
    }

    private static EdgeCondition Translate(BoundaryKind kind) =>
        kind == BoundaryKind.Neumann ? EdgeCondition.Neumann : EdgeCondition.Dirichlet;

    private static void RasteriseRectangle(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double potential)
    {
        // Half-open in index space but inclusive in coordinate space: a node lying
        // on the boundary of the rectangle belongs to it, so two abutting
        // electrodes share their contact nodes rather than leaving a gap.
        var i0 = (int)Math.Ceiling((electrode.MinX - grid.OriginX) / grid.Spacing);
        var i1 = (int)Math.Floor((electrode.MaxX - grid.OriginX) / grid.Spacing);
        var j0 = (int)Math.Ceiling((electrode.MinY - grid.OriginY) / grid.Spacing);
        var j1 = (int)Math.Floor((electrode.MaxY - grid.OriginY) / grid.Spacing);

        mask.FixRectangle(i0, j0, i1, j1, potential);
    }

    private static void RasteriseDisc(
        DirichletMask mask, Grid2D grid, CompiledElectrode electrode, double potential)
    {
        var radiusSquared = electrode.Radius * electrode.Radius;

        var i0 = Math.Max(0, (int)Math.Floor((electrode.CentreX - electrode.Radius - grid.OriginX) / grid.Spacing));
        var i1 = Math.Min(grid.CountX - 1,
            (int)Math.Ceiling((electrode.CentreX + electrode.Radius - grid.OriginX) / grid.Spacing));
        var j0 = Math.Max(0, (int)Math.Floor((electrode.CentreY - electrode.Radius - grid.OriginY) / grid.Spacing));
        var j1 = Math.Min(grid.CountY - 1,
            (int)Math.Ceiling((electrode.CentreY + electrode.Radius - grid.OriginY) / grid.Spacing));

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
        var (potential, report) = PoissonSolver2D.Solve(mask, solve.Tolerance, maximumCycles: 400);

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
