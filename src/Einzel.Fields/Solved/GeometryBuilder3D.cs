using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>A three-dimensional geometry to solve: a box, a mesh size, and electrodes.</summary>
/// <param name="MinX">Lower x, in metres.</param>
/// <param name="MinY">Lower y, in metres.</param>
/// <param name="MinZ">Lower z, in metres.</param>
/// <param name="MaxX">Upper x, in metres.</param>
/// <param name="MaxY">Upper y, in metres.</param>
/// <param name="MaxZ">Upper z, in metres.</param>
/// <param name="CellSize">Requested node spacing, in metres.</param>
/// <param name="Electrodes">The electrodes.</param>
/// <param name="Tolerance">Relative residual the solve must reach.</param>
public sealed record Geometry3D(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ,
    double CellSize,
    IReadOnlyList<CompiledElectrode3D> Electrodes,
    double Tolerance = 1e-10)
{
    /// <summary>The drive this geometry is operated with, or null when static.</summary>
    public Core.Model.CompiledDrive? Drive { get; init; }

    /// <summary>The timed sequence it is operated through, or empty for one state.</summary>
    public IReadOnlyList<CompiledStage3D> Stages { get; init; } = [];
}


/// <summary>Builds and solves a three-dimensional geometry.</summary>
public static class GeometryBuilder3D
{
    /// <summary>Builds the grid a geometry asks for.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The grid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    public static Grid3D BuildGrid(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return Grid3D.OverBox(
            geometry.MinX, geometry.MinY, geometry.MinZ,
            geometry.MaxX, geometry.MaxY, geometry.MaxZ,
            geometry.CellSize);
    }

    /// <summary>Builds the Dirichlet mask on the finest grid.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="potentialOf">
    /// What each electrode holds, for a basis solve. Its own potential when omitted.
    /// </param>
    /// <returns>The mask.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// Sub-cell: conductor surfaces between nodes are recorded as cut links, which
    /// is where the accuracy of a solve comes from. Coarse multigrid levels are
    /// built by <see cref="Coarsener"/> instead and are deliberately different.
    /// </remarks>
    public static DirichletMask3D BuildMask(
        Geometry3D geometry, Grid3D grid, Func<CompiledElectrode3D, double>? potentialOf = null) =>
        Assemble(geometry, grid, potentialOf, coarse: false);

    /// <summary>
    /// A builder for the coarse levels of a multigrid hierarchy, memoised by grid.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="potentialOf">
    /// What each electrode holds, for a basis solve. Its own potential when omitted.
    /// </param>
    /// <returns>A function from a coarse grid to its mask.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Handed out as a whole function rather than as a flag on <see cref="BuildMask"/>
    /// because the difference between the two is load-bearing and the obvious
    /// spelling has to be the safe one. A coarse level built the fine way is not
    /// slightly worse - it is ill-conditioned, and the solve converges contentedly
    /// somewhere else.
    /// </para>
    /// <para>
    /// Coarse levels are node-aligned: no cuts, and an electrode too small to hold
    /// a node is pinned to its nearest free one so it does not drop out of the
    /// problem. The values are irrelevant on these levels - a V-cycle solves for the
    /// error, whose Dirichlet data is zero - so only the pattern of fixed nodes
    /// matters, and the pattern is what has to stay recognisable.
    /// </para>
    /// <para>
    /// Memoised because the hierarchy is rebuilt on every cycle otherwise: for the
    /// twelve-rod segmented quadrupole that is over a million <c>Contains</c> calls
    /// per cycle, producing a mask that is identical every time.
    /// </para>
    /// </remarks>
    public static Func<Grid3D, DirichletMask3D> Coarsener(
        Geometry3D geometry, Func<CompiledElectrode3D, double>? potentialOf = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var cache = new Dictionary<(int X, int Y, int Z), DirichletMask3D>();

        return grid =>
        {
            var key = (grid.CountX, grid.CountY, grid.CountZ);

            if (!cache.TryGetValue(key, out var mask))
            {
                mask = Assemble(geometry, grid, potentialOf, coarse: true);
                cache[key] = mask;
            }

            return mask;
        };
    }

    private static DirichletMask3D Assemble(
        Geometry3D geometry,
        Grid3D grid,
        Func<CompiledElectrode3D, double>? potentialOf,
        bool coarse)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(grid);

        var mask = new DirichletMask3D(grid);
        List<CompiledElectrode3D>? missed = null;

        foreach (var electrode in geometry.Electrodes)
        {
            var potential = potentialOf?.Invoke(electrode) ?? electrode.Potential;

            if (!Rasterise(mask, grid, electrode, potential) && coarse)
            {
                (missed ??= []).Add(electrode);
            }
        }

        PinFaces(mask, grid);

        // After the faces, so a pin can never overwrite a grounded boundary, and
        // only onto a free node, so two electrodes whose centres round together
        // leave one of them present rather than both of them confused. An electrode
        // that rasterises to nothing has stopped being part of the problem and the
        // coarse grid then solves a different one; keeping it at the smallest size
        // the level can express is the least-wrong thing available.
        foreach (var electrode in missed ?? [])
        {
            var (cx, cy, cz) = electrode.Centre;

            var i = Math.Clamp((int)Math.Round((cx - grid.OriginX) / grid.SpacingX), 0, grid.CountX - 1);
            var j = Math.Clamp((int)Math.Round((cy - grid.OriginY) / grid.SpacingY), 0, grid.CountY - 1);
            var k = Math.Clamp((int)Math.Round((cz - grid.OriginZ) / grid.SpacingZ), 0, grid.CountZ - 1);

            if (!mask.IsFixed(i, j, k))
            {
                mask.Fix(i, j, k, potentialOf?.Invoke(electrode) ?? electrode.Potential);
            }
        }

        // Sub-cell surfaces on the fine level only. A coarse level exists to
        // accelerate, not to be accurate, and a cut there is actively harmful: an
        // electrode a fraction of a coarse cell across produces arms a thousandth
        // of a cell long, whose coefficients are enormous, and the correction that
        // comes back does not converge slowly - it converges somewhere else.
        //
        // Node-aligned geometry on the coarse levels is cruder and stable. The
        // accuracy still comes from the fine level, which is unchanged.
        if (!coarse)
        {
            AddCuts(geometry, grid, mask, potentialOf);
        }

        foreach (var electrode in geometry.Electrodes)
        {
            mask.SmallestFeature = Math.Min(mask.SmallestFeature, electrode.CharacteristicSize);
        }

        return mask;
    }

    /// <summary>Builds, solves, and wraps a geometry as a field.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The field, and how the solve went.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    public static (SolvedField3D Field, SolveReport Report) Build(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var grid = BuildGrid(geometry);
        var mask = BuildMask(geometry, grid);

        var (potential, report) = PoissonSolver3D.Solve(
            mask,
            geometry.Tolerance,
            maximumCycles: 200,
            coarsen: Coarsener(geometry));

        return (new SolvedField3D(potential, geometry.Electrodes), report);
    }

    /// <summary>
    /// Builds a geometry as a field, driven or sequenced if it declares either.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The field, and the worst of the basis solves.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <remarks>
    /// The channel decomposition is the same code the plane uses, because nothing
    /// about it is dimensional: what makes a channel a channel is how the electrodes
    /// are wired, not where they are. A segmented quadrupole with three sections at
    /// different working points is three patterns, whatever the mesh is.
    /// </remarks>
    public static (IElectrostaticField Field, SolveReport Report) BuildField(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.Drive is null && geometry.Stages.Count == 0)
        {
            var (statik, staticReport) = Build(geometry);
            return (statik, staticReport);
        }

        var groups = Groups(geometry);
        var solves = SolveGroups(geometry, groups);

        var channels = new List<IElectrostaticField>(groups.Count);
        var direct = new List<double>(groups.Count);
        var harmonics = new List<IReadOnlyList<(double Amplitude, double Phase)>>(groups.Count);

        SolveReport worst = new(true, 0, 0.0, 0.0, 0.0);

        for (var index = 0; index < groups.Count; index++)
        {
            channels.Add(new SolvedField3D(solves[index].Potential, geometry.Electrodes));
            direct.Add(groups[index].Direct);
            harmonics.Add(groups[index].Harmonics);

            if (solves[index].Report.Cycles > worst.Cycles)
            {
                worst = solves[index].Report;
            }
        }

        var drive = geometry.Drive;

        Analytic.RfWaveform waveform = drive is { Waveform: Core.Model.DriveWaveform.Rectangular }
            ? new Analytic.RfWaveform.Rectangular(drive.DutyCycle)
            : new Analytic.RfWaveform.Sinusoid();

        var frequency = drive?.FrequencyHz ?? 1.0;

        if (geometry.Stages.Count == 0)
        {
            return (new DrivenSolvedField(channels, direct, harmonics, frequency, waveform), worst);
        }

        var boundaries = new List<double>(geometry.Stages.Count);
        var stageDirect = new List<IReadOnlyList<double>>(geometry.Stages.Count);
        var stageHarmonics =
            new List<IReadOnlyList<IReadOnlyList<(double Amplitude, double Phase)>>>(geometry.Stages.Count);

        var elapsed = 0.0;

        foreach (var stage in geometry.Stages)
        {
            elapsed += stage.DurationSeconds;
            boundaries.Add(elapsed);

            var weights = DriveChannels.Weigh(
                groups, [.. stage.Electrodes.Select(Excited)], Quadrature(geometry));

            stageDirect.Add(weights.Direct);
            stageHarmonics.Add(weights.Harmonics);
        }

        var sequenced = new DrivenSolvedField(
            channels, direct, harmonics, frequency, waveform, boundaries, stageDirect, stageHarmonics);

        return (sequenced, worst);
    }

    /// <summary>One basis channel of a solve, with the evidence that solve produced.</summary>
    /// <param name="Index">Which channel, in the order the decomposition found them.</param>
    /// <param name="Mask">The finest-level mask, for the node and cut counts.</param>
    /// <param name="Potential">The solved potential.</param>
    /// <param name="Report">How the solve went.</param>
    public sealed record ChannelSolve(
        int Index, DirichletMask3D Mask, ScalarField3D Potential, SolveReport Report);

    /// <summary>Solves a geometry channel by channel, and hands back the diagnostics.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>One entry per basis channel; exactly one for a static geometry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <remarks>
    /// What <c>einzel solve</c> reports against. A driven structure is not one solve
    /// but one per spatial pattern, and a residual quoted for "the field" would be
    /// quoting whichever of them happened to be last.
    /// </remarks>
    public static IReadOnlyList<ChannelSolve> SolveChannels(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.Drive is null && geometry.Stages.Count == 0)
        {
            var grid = BuildGrid(geometry);
            var mask = BuildMask(geometry, grid);

            var (potential, report) = PoissonSolver3D.Solve(
                mask, geometry.Tolerance, maximumCycles: 200, coarsen: Coarsener(geometry));

            return [new ChannelSolve(0, mask, potential, report)];
        }

        return SolveGroups(geometry, Groups(geometry));
    }

    /// <summary>
    /// Whether the drive is a sinusoid, so every phase resolves into two fixed
    /// quadrature components rather than into a supply of its own.
    /// </summary>
    private static bool Quadrature(Geometry3D geometry) =>
        geometry.Drive is null or { Waveform: Core.Model.DriveWaveform.Sinusoid };

    private static List<DriveChannel> Groups(Geometry3D geometry)
    {
        var states = geometry.Stages.Count > 0
            ? geometry.Stages.Select(stage => stage.Electrodes).ToList()
            : [geometry.Electrodes];

        return DriveChannels.Decompose(
            [.. states.SelectMany(e => e).Select(Excited)], Quadrature(geometry));
    }

    private static List<ChannelSolve> SolveGroups(
        Geometry3D geometry, List<DriveChannel> groups)
    {
        var grid = BuildGrid(geometry);
        var solves = new List<ChannelSolve>(groups.Count);

        for (var index = 0; index < groups.Count; index++)
        {
            var pattern = groups[index].Pattern;
            double Weight(CompiledElectrode3D e) => pattern.GetValueOrDefault(e.Name, 0.0);

            var mask = BuildMask(geometry, grid, Weight);

            var (potential, report) = PoissonSolver3D.Solve(
                mask,
                geometry.Tolerance,
                maximumCycles: 200,
                coarsen: Coarsener(geometry, Weight));

            solves.Add(new ChannelSolve(index, mask, potential, report));
        }

        return solves;
    }

    /// <summary>How a three-dimensional electrode is excited, for the shared decomposition.</summary>
    private static Excitation Excited(CompiledElectrode3D electrode) =>
        new(electrode.Name, electrode.Potential, electrode.DriveAmplitude, electrode.DrivePhase);

    /// <summary>Fixes every node inside an electrode, and says whether it found any.</summary>
    private static bool Rasterise(
        DirichletMask3D mask, Grid3D grid, CompiledElectrode3D electrode, double potential)
    {
        var any = false;

        for (var k = 0; k < grid.CountZ; k++)
        {
            var z = grid.Z(k);

            for (var j = 0; j < grid.CountY; j++)
            {
                var y = grid.Y(j);

                for (var i = 0; i < grid.CountX; i++)
                {
                    if (electrode.Contains(grid.X(i), y, z))
                    {
                        mask.Fix(i, j, k, potential);
                        any = true;
                    }
                }
            }
        }

        return any;
    }

    /// <summary>
    /// Grounds the nodes on every Dirichlet face.
    /// </summary>
    /// <remarks>
    /// The face node itself, not a ghost one cell outside it. The alternative reading
    /// puts the boundary a cell further out at every level of a multigrid hierarchy,
    /// so the domain grows as it coarsens and the coarse problem is a different one -
    /// which in two dimensions sent a cap plate in a grounded box to 1e50 volts.
    /// </remarks>
    private static void PinFaces(DirichletMask3D mask, Grid3D grid)
    {
        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    var onDirichletFace =
                        (i == 0 && mask.LowerX == EdgeCondition.Dirichlet)
                        || (i == grid.CountX - 1 && mask.UpperX == EdgeCondition.Dirichlet)
                        || (j == 0 && mask.LowerY == EdgeCondition.Dirichlet)
                        || (j == grid.CountY - 1 && mask.UpperY == EdgeCondition.Dirichlet)
                        || (k == 0 && mask.LowerZ == EdgeCondition.Dirichlet)
                        || (k == grid.CountZ - 1 && mask.UpperZ == EdgeCondition.Dirichlet);

                    if (onDirichletFace && !mask.IsFixed(i, j, k))
                    {
                        mask.Fix(i, j, k, 0.0);
                    }
                }
            }
        }
    }

    private static void AddCuts(
        Geometry3D geometry,
        Grid3D grid,
        DirichletMask3D mask,
        Func<CompiledElectrode3D, double>? potentialOf)
    {
        var cuts = new CutLinks3D(grid);

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        // A node inside metal has no free stencil to cut.
                        continue;
                    }

                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 1, 0, 0, Arm3D.East);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, -1, 0, 0, Arm3D.West);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, 1, 0, Arm3D.North);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, -1, 0, Arm3D.South);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, 0, 1, Arm3D.Up);
                    CutTowards(geometry, grid, cuts, potentialOf, i, j, k, 0, 0, -1, Arm3D.Down);
                }
            }
        }

        if (cuts.CutCount > 0)
        {
            mask.Cuts = cuts;
        }
    }

    private static void CutTowards(
        Geometry3D geometry,
        Grid3D grid,
        CutLinks3D cuts,
        Func<CompiledElectrode3D, double>? potentialOf,
        int i,
        int j,
        int k,
        int di,
        int dj,
        int dk,
        Arm3D arm)
    {
        var ni = i + di;
        var nj = j + dj;
        var nk = k + dk;

        if (ni < 0 || nj < 0 || nk < 0 || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
        {
            return;
        }

        var fromX = grid.X(i);
        var fromY = grid.Y(j);
        var fromZ = grid.Z(k);

        var toX = grid.X(ni);
        var toY = grid.Y(nj);
        var toZ = grid.Z(nk);

        var nearest = 1.0;
        var potential = 0.0;
        var found = false;

        foreach (var electrode in geometry.Electrodes)
        {
            if (electrode.FirstEntry(fromX, fromY, fromZ, toX, toY, toZ) is not { } entry || entry >= nearest)
            {
                continue;
            }

            // A surface at zero puts the node itself on the conductor, where
            // rasterisation should already have fixed it. The two tests are
            // different arithmetic - a signed distance against a quadratic root -
            // so they can disagree by a rounding at a node that sits exactly on the
            // surface, and the disagreement is expensive: it makes an arm a
            // thousandth of a cell long holding the full electrode potential, which
            // is an enormous coefficient at precisely the nodes next to the metal.
            // The solve then converges contentedly to a wrong answer, and the
            // maximum principle is the only thing that notices.
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
            cuts.Cut(i, j, k, arm, nearest, potential);
        }
    }
}
