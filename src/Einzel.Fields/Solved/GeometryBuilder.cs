using System.Numerics;
using Einzel.Fields.Analytic;
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
            Symmetry = solve.Symmetry,
        };

        // The axis of an axisymmetric solve is a mirror plane whatever the document
        // says, because it is one. Forced rather than validated: requiring the
        // author to declare it is an opportunity to get it wrong, and there is no
        // second thing it could be.
        if (solve.Symmetry == SolveSymmetry.Cylindrical && grid.OriginY <= 0.5 * grid.SpacingY)
        {
            mask.BottomEdge = EdgeCondition.Neumann;
        }

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

        if (solve.Drive is not null || solve.Stages.Count > 0)
        {
            return BuildDriven(solve);
        }

        var grid = BuildGrid(solve);
        var mask = BuildMask(solve, grid);
        var (potential, report) = PoissonSolver2D.Solve(
            mask, solve.Tolerance, maximumCycles: 400, coarsen: coarse => BuildMask(solve, coarse));

        IElectrostaticField field = new SolvedField2D(
            potential,
            new BicubicInterpolant(potential),
            boundaryIsDiscontinuous: solve.BoundaryIsDiscontinuous,
            conductors: solve.Electrodes);

        // Wrapped before any reflection, so a reflection composes in space rather
        // than in the half-plane - it mirrors the axial coordinate of a field that
        // already knows how to be three-dimensional.
        if (solve.Symmetry == SolveSymmetry.Cylindrical)
        {
            field = new AxisymmetricField(field);
        }

        if (solve.ReflectAboutX is { } plane)
        {
            // The reflected half is the same solve seen backwards, so the two are
            // identical by construction and no difference between them can come
            // from their having been meshed differently.
            field = new SuperposedField([field, new ReflectedField(field, plane)]);
        }

        return (field, report);
    }

    /// <summary>
    /// Builds a driven geometry: one solve per independent channel, superposed with
    /// weights that are functions of time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing about the Poisson equation is time-dependent. The field is linear in
    /// the applied potentials, so solving once per channel at unit potential and
    /// then varying the weights <em>is</em> the RF - which is why driving a real
    /// geometry costs the same per step as a static one times the channel count,
    /// and no re-solves at all.
    /// </para>
    /// <para>
    /// Electrodes are grouped by their potential as a function of time, up to sign.
    /// A quadrupole's two pairs are exact negatives, so the whole device is one
    /// channel; add a grounded housing and it is two.
    /// </para>
    /// </remarks>
    private static (IElectrostaticField Field, SolveReport Report) BuildDriven(CompiledSolvedField solve)
    {
        var grid = BuildGrid(solve);
        var drive = solve.Drive;

        // Every stage's electrodes go into the decomposition together, so a pattern
        // that appears in two stages is solved once and simply weighted differently
        // in each. A trap that fills and then extracts usually shares most of its
        // patterns between the two, and paying for them twice would be paying for
        // the sequencer rather than for the physics.
        var states = solve.Stages.Count > 0
            ? solve.Stages.Select(stage => stage.Electrodes).ToList()
            : [solve.Electrodes];

        var groups = DriveChannels.Decompose(
            [.. states.SelectMany(e => e).Select(Excited)]);

        var channels = new List<IElectrostaticField>(groups.Count);
        var direct = new List<double>(groups.Count);
        var harmonics = new List<IReadOnlyList<(double Amplitude, double Phase)>>(groups.Count);

        SolveReport worst = new(true, 0, 0.0, 0.0, 0.0);

        foreach (var group in groups)
        {
            // The channel's relative potentials, and zero on every electrode it
            // does not reach. The scale was taken out during normalisation and put
            // back on the weight, so this solve is in units of that weight.
            var pattern = group.Pattern;

            var mask = BuildMask(solve, grid, e => pattern.GetValueOrDefault(e.Name, 0.0));

            var (potential, report) = PoissonSolver2D.Solve(
                mask,
                solve.Tolerance,
                maximumCycles: 400,
                coarsen: coarse => BuildMask(solve, coarse, e => pattern.GetValueOrDefault(e.Name, 0.0)));

            IElectrostaticField basis = new SolvedField2D(
                potential,
                new BicubicInterpolant(potential),
                boundaryIsDiscontinuous: solve.BoundaryIsDiscontinuous,
                conductors: solve.Electrodes);

            if (solve.Symmetry == SolveSymmetry.Cylindrical)
            {
                basis = new AxisymmetricField(basis);
            }

            if (solve.ReflectAboutX is { } plane)
            {
                basis = new SuperposedField([basis, new ReflectedField(basis, plane)]);
            }

            channels.Add(basis);
            direct.Add(group.Direct);
            harmonics.Add(group.Harmonics);

            if (report.Cycles > worst.Cycles)
            {
                worst = report;
            }
        }

        RfWaveform waveform = drive is { Waveform: DriveWaveform.Rectangular }
            ? new RfWaveform.Rectangular(drive.DutyCycle)
            : new RfWaveform.Sinusoid();

        // A sequence with no drive still switches; it just switches between states
        // that do not oscillate. The frequency is then only a scale for the phase
        // argument, which nothing uses, so any positive number will do and one
        // hertz keeps the step cap out of the way.
        var frequency = drive?.FrequencyHz ?? 1.0;

        if (solve.Stages.Count == 0)
        {
            return (new DrivenSolvedField(channels, direct, harmonics, frequency, waveform), worst);
        }

        var boundaries = new List<double>(solve.Stages.Count);
        var stageDirect = new List<IReadOnlyList<double>>(solve.Stages.Count);
        var stageHarmonics =
            new List<IReadOnlyList<IReadOnlyList<(double Amplitude, double Phase)>>>(solve.Stages.Count);

        var elapsed = 0.0;

        foreach (var stage in solve.Stages)
        {
            elapsed += stage.DurationSeconds;
            boundaries.Add(elapsed);

            // The same channels, re-weighted for this stage. Every pattern already
            // has a solve; what a stage changes is only how much of each is on.
            var weights = DriveChannels.Weigh(groups, [.. stage.Electrodes.Select(Excited)]);

            stageDirect.Add(weights.Direct);
            stageHarmonics.Add(weights.Harmonics);
        }

        var sequenced = new DrivenSolvedField(
            channels, direct, harmonics, frequency, waveform, boundaries, stageDirect, stageHarmonics);

        return (sequenced, worst);
    }


    /// <summary>How a two-dimensional electrode is excited, for the shared decomposition.</summary>
    private static Excitation Excited(CompiledElectrode electrode) =>
        new(electrode.Name, electrode.Potential, electrode.DriveAmplitude, electrode.DrivePhase);
}
