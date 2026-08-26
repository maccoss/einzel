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

    /// <summary>Builds the Dirichlet mask on a grid.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="grid">The grid, which may be a coarse multigrid level.</param>
    /// <param name="potentialOf">
    /// What each electrode holds, for a basis solve. Its own potential when omitted.
    /// </param>
    /// <returns>The mask.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// Rebuilt from geometry at every level rather than projected down from the
    /// finest, which is what makes interior electrodes survive coarsening: an
    /// electrode too small to hold a coarse node still cuts the links around it, so
    /// the coarse operator still knows it is there.
    /// </remarks>
    public static DirichletMask3D BuildMask(
        Geometry3D geometry, Grid3D grid, Func<CompiledElectrode3D, double>? potentialOf = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(grid);

        var mask = new DirichletMask3D(grid);

        foreach (var electrode in geometry.Electrodes)
        {
            var potential = potentialOf?.Invoke(electrode) ?? electrode.Potential;

            Rasterise(mask, grid, electrode, potential);
        }

        PinFaces(mask, grid);
        AddCuts(geometry, grid, mask, potentialOf);

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
            coarsen: coarse => BuildMask(geometry, coarse));

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

        var grid = BuildGrid(geometry);

        var states = geometry.Stages.Count > 0
            ? geometry.Stages.Select(stage => stage.Electrodes).ToList()
            : [geometry.Electrodes];

        var groups = DriveChannels.Decompose([.. states.SelectMany(e => e).Select(Excited)]);

        var channels = new List<IElectrostaticField>(groups.Count);
        var direct = new List<double>(groups.Count);
        var harmonics = new List<IReadOnlyList<(double Amplitude, double Phase)>>(groups.Count);

        SolveReport worst = new(true, 0, 0.0, 0.0, 0.0);

        foreach (var group in groups)
        {
            var pattern = group.Pattern;

            var (potential, report) = PoissonSolver3D.Solve(
                BuildMask(geometry, grid, e => pattern.GetValueOrDefault(e.Name, 0.0)),
                geometry.Tolerance,
                maximumCycles: 200,
                coarsen: coarse => BuildMask(geometry, coarse, e => pattern.GetValueOrDefault(e.Name, 0.0)));

            channels.Add(new SolvedField3D(potential, geometry.Electrodes));
            direct.Add(group.Direct);
            harmonics.Add(group.Harmonics);

            if (report.Cycles > worst.Cycles)
            {
                worst = report;
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

            var weights = DriveChannels.Weigh(groups, [.. stage.Electrodes.Select(Excited)]);

            stageDirect.Add(weights.Direct);
            stageHarmonics.Add(weights.Harmonics);
        }

        var sequenced = new DrivenSolvedField(
            channels, direct, harmonics, frequency, waveform, boundaries, stageDirect, stageHarmonics);

        return (sequenced, worst);
    }

    /// <summary>How a three-dimensional electrode is excited, for the shared decomposition.</summary>
    private static Excitation Excited(CompiledElectrode3D electrode) =>
        new(electrode.Name, electrode.Potential, electrode.DriveAmplitude, electrode.DrivePhase);

    private static void Rasterise(
        DirichletMask3D mask, Grid3D grid, CompiledElectrode3D electrode, double potential)
    {
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
                    }
                }
            }
        }
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
