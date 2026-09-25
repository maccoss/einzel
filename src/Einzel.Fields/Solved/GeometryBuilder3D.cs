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
    /// <summary>The generators this geometry is operated with. Empty when static.</summary>
    public IReadOnlyList<Core.Model.CompiledDrive> Drives { get; init; } = [];

    /// <summary>The primary drive - the first declared - or null when static.</summary>
    public Core.Model.CompiledDrive? Drive => Drives.Count > 0 ? Drives[0] : null;

    /// <summary>The timed sequence it is operated through, or empty for one state.</summary>
    public IReadOnlyList<CompiledStage3D> Stages { get; init; } = [];

    /// <summary>What each face of the domain is, in the order lower/upper x, y, z.</summary>
    /// <remarks>
    /// <para>
    /// <b>Dirichlet by default, which is a grounded box and is a third electrode.</b> That
    /// is right for a device inside a housing and wrong for one whose geometry is invariant
    /// along an axis - a stripe electrode running the length of an analyser's drift makes
    /// the field independent of the drift direction, so grounding those faces imposes an
    /// axial field the real instrument does not have.
    /// </para>
    /// <para>
    /// A Neumann face is a mirror, so declaring both z faces Neumann says the structure
    /// repeats forever along z, which is what a stripe electrode means. The solver has
    /// always supported this; until now no document could ask for it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<EdgeCondition> Faces { get; init; } = [];

    /// <summary>Where to mirror the solved half, in metres along x, or null for none.</summary>
    /// <remarks>
    /// <para>
    /// <b>Solve half an instrument and reflect it.</b> An analyser symmetric about a plane
    /// costs twice what it needs to: the far half is the near half seen backwards, and a
    /// volume solve is the one place where halving the domain halves the dominant cost. On a
    /// 3-D Astral, where the solve is 94 per cent of a run, that is the cheapest factor of
    /// two available.
    /// </para>
    /// <para>
    /// The plane path has had this from the beginning. It composes here unchanged because
    /// <see cref="ReflectedField"/> mirrors a coordinate of any field rather than knowing
    /// anything about how that field was meshed - so the two halves are identical by
    /// construction and no difference between them can come from their having been
    /// discretised differently.
    /// </para>
    /// <para>
    /// Declare the mid-plane face Neumann alongside it. A mirror plane is a symmetry plane,
    /// and grounding it instead puts a conductor down the middle of the instrument.
    /// </para>
    /// </remarks>
    public double? ReflectAboutX { get; init; }

    /// <summary>The six faces a compiled solve declares, in this type's own terms.</summary>
    /// <param name="declared">The compiled faces, or empty for a grounded box.</param>
    /// <returns>Six conditions, or empty.</returns>
    /// <remarks>
    /// One conversion in one place: <c>Einzel.Core</c> cannot name this assembly's
    /// <see cref="EdgeCondition"/>, and two enums that must agree are exactly the pair that
    /// stops agreeing. Every construction site calls this rather than mapping it again.
    /// <para>
    /// There were briefly three of these enums, the third being a Core-side one invented for
    /// the volume path alone - although <see cref="Core.Model.BoundaryKind"/> already meant
    /// dirichlet-or-neumann for the plane path. Two is the irreducible number, because an
    /// assembly boundary sits between them; three was a duplicate.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<EdgeCondition> FacesOf(
        IReadOnlyList<Core.Model.BoundaryKind> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (declared.Count != 6)
        {
            return [];
        }

        var faces = new EdgeCondition[6];

        for (var face = 0; face < 6; face++)
        {
            faces[face] = declared[face] == Core.Model.BoundaryKind.Neumann
                ? EdgeCondition.Neumann
                : EdgeCondition.Dirichlet;
        }

        return faces;
    }
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

    /// <summary>Mirrors a solved half about the declared plane, or returns it unchanged.</summary>
    private static IElectrostaticField Reflected(IElectrostaticField field, Geometry3D geometry) =>
        geometry.ReflectAboutX is { } plane
            ? new SuperposedField([field, new ReflectedField(field, plane)])
            : field;

    private static DirichletMask3D Assemble(
        Geometry3D geometry,
        Grid3D grid,
        Func<CompiledElectrode3D, double>? potentialOf,
        bool coarse)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(grid);

        var mask = new DirichletMask3D(grid);

        if (geometry.Faces.Count == 6)
        {
            mask.LowerX = geometry.Faces[0];
            mask.UpperX = geometry.Faces[1];
            mask.LowerY = geometry.Faces[2];
            mask.UpperY = geometry.Faces[3];
            mask.LowerZ = geometry.Faces[4];
            mask.UpperZ = geometry.Faces[5];
        }

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

        return (
            new SolvedField3D(potential, geometry.Electrodes),
            report with { Warnings = SharedFaceNodes.Warnings(NodesOnSharedFaces(geometry, grid, mask)) });
    }

    /// <summary>
    /// Finds where a mesh samples a face two conductors share while holding different
    /// excitations - a node on it, or a stencil arm that first meets metal on it - so that
    /// which conductor the sample is credited to is decided by rounding.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="grid">The finest grid it is solved on.</param>
    /// <param name="mask">
    /// Any finest-level mask of this geometry on this grid, for which nodes are fixed; asked of
    /// the geometry node by node when omitted. Every channel's mask fixes the same nodes - an
    /// electrode's nodes are fixed whatever its weight in the channel - so whichever the
    /// caller has will do.
    /// </param>
    /// <returns>What was found, or null when the mesh samples no such face.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <para>
    /// A mesh samples the geometry in two places, and each gets its own test. A <b>node</b>
    /// is on a surface when its signed distance is within
    /// <see cref="SharedFaceNodes.ToleranceFraction"/> of the finest spacing. An <b>arm</b>
    /// from a free node samples a face when the point where it first meets one conductor is
    /// within the same distance of the other's surface, and nothing is nearer along it - that
    /// is the entry the cut link records, and which of the two conductors it records is then
    /// whichever the rounding put in front.
    /// </para>
    /// <para>
    /// <b>The arms are the case that mattered.</b> <c>astral-3d</c>'s foil stripes are thinner
    /// than a cell and hold no node at all, so with its mesh shift removed the node test finds
    /// nothing; the coin was tossed on the arms lying in the plane of each shared face.
    /// </para>
    /// <para>
    /// A pair counts when it disagrees in <em>any</em> state the instrument has - asked
    /// through <see cref="ElectrodeOverlap3D.StatesOf"/> and
    /// <see cref="ElectrodeOverlap3D.Agrees"/>, the overlap check's own functions, so the two
    /// questions asked of a pair cannot come to disagree about what "different excitations"
    /// means. A pair that agrees everywhere shares a face harmlessly: either answer to the
    /// coin toss is the same potential.
    /// </para>
    /// <para>
    /// <b>Only nodes near both bounding boxes are visited</b>: a point on both surfaces is in
    /// both boxes, and an arm's entry is within one cell of its node, so the box is widened by
    /// a cell and nothing further. For abutting conductors that is a slab three nodes thick
    /// round the shared face, and a node is let through to the arm test only when it is within
    /// a cell of both surfaces - so the cost is a face's worth of signed distances per
    /// touching pair, on a geometry whose rasterization makes a pass over the whole grid per
    /// electrode.
    /// </para>
    /// <para>
    /// Only the finest grid. The coarse levels exist to accelerate, their Dirichlet values
    /// are not used, and the answer is decided where the cut cells are.
    /// </para>
    /// </remarks>
    public static SharedFaceNodes? NodesOnSharedFaces(
        Geometry3D geometry, Grid3D grid, DirichletMask3D? mask = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(grid);

        var electrodes = geometry.Electrodes;
        var states = ElectrodeOverlap3D.StatesOf(electrodes, geometry.Stages);
        var tolerance = SharedFaceNodes.ToleranceFraction * grid.MinimumSpacing;
        var reach = Math.Max(grid.SpacingX, Math.Max(grid.SpacingY, grid.SpacingZ));

        var nodes = new HashSet<long>();
        var arms = new HashSet<long>();
        (string First, string Second, double X, double Y, double Z)? nodeExample = null;
        (string First, string Second, double X, double Y, double Z)? armExample = null;

        for (var i = 0; i < electrodes.Count; i++)
        {
            for (var j = i + 1; j < electrodes.Count; j++)
            {
                var (i1, i2) = (i, j);

                if (states.All(state => ElectrodeOverlap3D.Agrees(state[i1], state[i2])))
                {
                    continue;
                }

                var a = electrodes[i];
                var b = electrodes[j];

                var (aMinX, aMinY, aMinZ, aMaxX, aMaxY, aMaxZ) = a.Bounds;
                var (bMinX, bMinY, bMinZ, bMaxX, bMaxY, bMaxZ) = b.Bounds;

                var x0 = Math.Max(aMinX, bMinX) - tolerance;
                var x1 = Math.Min(aMaxX, bMaxX) + tolerance;
                var y0 = Math.Max(aMinY, bMinY) - tolerance;
                var y1 = Math.Min(aMaxY, bMaxY) + tolerance;
                var z0 = Math.Max(aMinZ, bMinZ) - tolerance;
                var z1 = Math.Min(aMaxZ, bMaxZ) + tolerance;

                // Boxes that do not meet cannot share a surface, and that settles almost
                // every pair - on the Astral, all but a few dozen of three thousand.
                if (x1 < x0 || y1 < y0 || z1 < z0)
                {
                    continue;
                }

                var (iLo, iHi) = SharedFaceNodes.Range(grid.OriginX, grid.SpacingX, grid.CountX, x0 - reach, x1 + reach);
                var (jLo, jHi) = SharedFaceNodes.Range(grid.OriginY, grid.SpacingY, grid.CountY, y0 - reach, y1 + reach);
                var (kLo, kHi) = SharedFaceNodes.Range(grid.OriginZ, grid.SpacingZ, grid.CountZ, z0 - reach, z1 + reach);

                for (var k = kLo; k <= kHi; k++)
                {
                    var z = grid.Z(k);

                    for (var jj = jLo; jj <= jHi; jj++)
                    {
                        var y = grid.Y(jj);

                        for (var ii = iLo; ii <= iHi; ii++)
                        {
                            var x = grid.X(ii);

                            // The same coordinates the rasterizer and the cut links use,
                            // which is the point: the coin is tossed in this arithmetic.
                            var da = Math.Abs(a.SignedDistance(x, y, z));

                            if (da > reach + tolerance)
                            {
                                continue;
                            }

                            var db = Math.Abs(b.SignedDistance(x, y, z));

                            if (db > reach + tolerance)
                            {
                                continue;
                            }

                            var node = ((((long)k * grid.CountY) + jj) * grid.CountX) + ii;

                            if (da <= tolerance && db <= tolerance)
                            {
                                if (nodes.Add(node))
                                {
                                    nodeExample ??= (a.Name, b.Name, x, y, z);
                                }

                                continue;
                            }

                            for (var arm = 0; arm < 6; arm++)
                            {
                                var (di, dj, dk) = Arms[arm];
                                var (ni, nj, nk) = (ii + di, jj + dj, k + dk);

                                if (ni < 0 || nj < 0 || nk < 0
                                    || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
                                {
                                    continue;
                                }

                                var (tx, ty, tz) = (grid.X(ni), grid.Y(nj), grid.Z(nk));

                                // Where the arm meets one of them, if that point is on the other's
                                // surface too - the nearer such point, when both qualify.
                                var entry = double.PositiveInfinity;

                                if (SharedFaceNodes.Interior(a.FirstEntry(x, y, z, tx, ty, tz), out var ea)
                                    && Math.Abs(b.SignedDistance(
                                        x + (ea * (tx - x)), y + (ea * (ty - y)), z + (ea * (tz - z)))) <= tolerance)
                                {
                                    entry = ea;
                                }

                                if (SharedFaceNodes.Interior(b.FirstEntry(x, y, z, tx, ty, tz), out var eb)
                                    && Math.Abs(a.SignedDistance(
                                        x + (eb * (tx - x)), y + (eb * (ty - y)), z + (eb * (tz - z)))) <= tolerance)
                                {
                                    entry = Math.Min(entry, eb);
                                }

                                if (double.IsPositiveInfinity(entry))
                                {
                                    continue;
                                }

                                // A fixed node has no stencil to cut. Asked of the mask when the
                                // caller has one, and otherwise of the geometry the way the finest
                                // mask is built - which costs one pass over the electrodes for a
                                // candidate, where building the mask costs a pass over the grid
                                // for every electrode: on astral-3d, minutes.
                                if (mask?.IsFixed(ii, jj, k) ?? IsFixedFine(geometry, grid, ii, jj, k))
                                {
                                    break;
                                }

                                if (Shadowed(electrodes, x, y, z, tx, ty, tz, entry))
                                {
                                    continue;
                                }

                                if (arms.Add((node * 6) + arm))
                                {
                                    armExample ??= (
                                        a.Name, b.Name,
                                        x + (entry * (tx - x)), y + (entry * (ty - y)), z + (entry * (tz - z)));
                                }
                            }
                        }
                    }
                }
            }
        }

        return (nodeExample ?? armExample) is { } first
            ? new SharedFaceNodes(
                nodes.Count, arms.Count, first.First, first.Second, first.X, first.Y, first.Z,
                ExampleIsArm: nodeExample is null)
            : null;
    }

    /// <summary>
    /// Whether the finest mask would fix a node: inside a conductor, or on a Dirichlet face.
    /// </summary>
    /// <remarks>
    /// The same two rules <see cref="Assemble"/> applies on the finest level -
    /// <see cref="Rasterise"/> by <c>Contains</c>, then <see cref="PinFaces"/> - asked of
    /// one node. The coarse-level pinning of an electrode too small to hold a node does not
    /// happen on the finest level, so it has no part here.
    /// </remarks>
    private static bool IsFixedFine(Geometry3D geometry, Grid3D grid, int i, int j, int k)
    {
        bool Dirichlet(int face) =>
            geometry.Faces.Count != 6 || geometry.Faces[face] == EdgeCondition.Dirichlet;

        if ((i == 0 && Dirichlet(0)) || (i == grid.CountX - 1 && Dirichlet(1))
            || (j == 0 && Dirichlet(2)) || (j == grid.CountY - 1 && Dirichlet(3))
            || (k == 0 && Dirichlet(4)) || (k == grid.CountZ - 1 && Dirichlet(5)))
        {
            return true;
        }

        var (x, y, z) = (grid.X(i), grid.Y(j), grid.Z(k));

        return geometry.Electrodes.Any(electrode => electrode.Contains(x, y, z));
    }

    /// <summary>The six stencil arms, in the order <see cref="AddCuts"/> visits them.</summary>
    private static readonly (int Di, int Dj, int Dk)[] Arms =
        [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>
    /// Whether some conductor meets the arm clearly before <paramref name="entry"/>, so the
    /// cut records that one and a tie at <paramref name="entry"/> is in its shadow.
    /// </summary>
    private static bool Shadowed(
        IReadOnlyList<CompiledElectrode3D> electrodes,
        double fromX, double fromY, double fromZ, double toX, double toY, double toZ, double entry)
    {
        foreach (var electrode in electrodes)
        {
            if (electrode.FirstEntry(fromX, fromY, fromZ, toX, toY, toZ) is { } nearer
                && nearer > 0.0
                && nearer < entry - SharedFaceNodes.ToleranceFraction)
            {
                return true;
            }
        }

        return false;
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

        if (geometry.Drives.Count == 0 && geometry.Stages.Count == 0)
        {
            var (statik, staticReport) = Build(geometry);
            return (Reflected(statik, geometry), staticReport);
        }

        var groups = Groups(geometry);
        var solves = SolveGroups(geometry, groups);

        // Once per geometry rather than per channel: which nodes sit on a shared face is a
        // property of the mesh and the conductors, and every channel solves on both.
        var meshWarnings = SharedFaceNodes.Warnings(solves.Count > 0
            ? NodesOnSharedFaces(geometry, solves[0].Mask.Grid, solves[0].Mask)
            : null);

        var channels = new List<IElectrostaticField>(groups.Count);
        var direct = new List<double>(groups.Count);
        var harmonics = new List<IReadOnlyList<WeightTerm>>(groups.Count);

        SolveReport worst = new(true, 0, 0.0, 0.0, 0.0);

        for (var index = 0; index < groups.Count; index++)
        {
            // Reflected per channel rather than once over the composition, matching the
            // plane path: superposition is linear either way, and mirroring each basis
            // field keeps the drive weights attached to the thing they weight.
            channels.Add(Reflected(
                new SolvedField3D(solves[index].Potential, geometry.Electrodes), geometry));
            direct.Add(groups[index].Direct);
            harmonics.Add(groups[index].Harmonics);

            if (solves[index].Report.Cycles > worst.Cycles)
            {
                worst = solves[index].Report;
            }
        }

        var (frequencies, waveforms, quadrature) = Clocks(geometry.Drives);

        worst = worst with { Warnings = meshWarnings };

        if (geometry.Stages.Count == 0)
        {
            return (
                new DrivenSolvedField(channels, direct, harmonics, frequencies, waveforms), worst);
        }

        var boundaries = new List<double>(geometry.Stages.Count);
        var stageDirect = new List<IReadOnlyList<double>>(geometry.Stages.Count);
        var stageHarmonics =
            new List<IReadOnlyList<IReadOnlyList<WeightTerm>>>(geometry.Stages.Count);
        var stageEndDirect = new List<IReadOnlyList<double>?>(geometry.Stages.Count);
        var stageEndHarmonics = new List<IReadOnlyList<IReadOnlyList<WeightTerm>>?>(geometry.Stages.Count);
        var anyRamp = false;

        var elapsed = 0.0;

        foreach (var stage in geometry.Stages)
        {
            elapsed += stage.DurationSeconds;
            boundaries.Add(elapsed);

            var weights = DriveChannels.Weigh(
                groups, [.. stage.Electrodes.Select(Excited)], quadrature);

            if (stage.EndElectrodes is null)
            {
                stageDirect.Add(weights.Direct);
                stageHarmonics.Add(weights.Harmonics);
                stageEndDirect.Add(null);
                stageEndHarmonics.Add(null);
                continue;
            }

            // A ramp: weighed at both ends, the oscillating terms aligned so every start
            // term has its end partner, exactly as the plane path does it.
            anyRamp = true;
            var endWeights = DriveChannels.Weigh(
                groups, [.. stage.EndElectrodes.Select(Excited)], quadrature);
            var alignedStart = new List<IReadOnlyList<WeightTerm>>(groups.Count);
            var alignedEnd = new List<IReadOnlyList<WeightTerm>>(groups.Count);
            for (var k = 0; k < groups.Count; k++)
            {
                var (a, b) = GeometryBuilder.Align(weights.Harmonics[k], endWeights.Harmonics[k]);
                alignedStart.Add(a);
                alignedEnd.Add(b);
            }

            stageDirect.Add(weights.Direct);
            stageHarmonics.Add(alignedStart);
            stageEndDirect.Add(endWeights.Direct);
            stageEndHarmonics.Add(alignedEnd);
        }

        var sequenced = new DrivenSolvedField(
            channels, direct, harmonics, frequencies, waveforms,
            boundaries, stageDirect, stageHarmonics,
            anyRamp ? stageEndDirect : null, anyRamp ? stageEndHarmonics : null);

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

        if (geometry.Drives.Count == 0 && geometry.Stages.Count == 0)
        {
            var grid = BuildGrid(geometry);
            var mask = BuildMask(geometry, grid);

            var (potential, report) = PoissonSolver3D.Solve(
                mask, geometry.Tolerance, maximumCycles: 200, coarsen: Coarsener(geometry));

            return
            [
                new ChannelSolve(
                    0, mask, potential,
                    report with { Warnings = SharedFaceNodes.Warnings(NodesOnSharedFaces(geometry, grid, mask)) }),
            ];
        }

        // Every channel carries the same finding, because every channel solves on the same
        // mesh round the same conductors; einzel solve reports it once per element.
        var solves = SolveGroups(geometry, Groups(geometry));

        var warnings = SharedFaceNodes.Warnings(solves.Count > 0
            ? NodesOnSharedFaces(geometry, solves[0].Mask.Grid, solves[0].Mask)
            : null);

        return [.. solves.Select(channel => channel with { Report = channel.Report with { Warnings = warnings } })];
    }

    /// <summary>
    /// The waveform, frequency and quadrature flag of every generator, in
    /// declaration order.
    /// </summary>
    /// <remarks>
    /// A geometry with stages and no drive still switches; it just switches between
    /// states that do not oscillate. There is then one nominal clock whose frequency
    /// nothing uses, and one hertz keeps the step cap out of the way.
    /// </remarks>
    private static (List<double> Frequencies, List<Analytic.RfWaveform> Waveforms, List<bool> Quadrature)
        Clocks(IReadOnlyList<Core.Model.CompiledDrive> drives)
    {
        var frequencies = new List<double>(drives.Count);
        var waveforms = new List<Analytic.RfWaveform>(drives.Count);
        var quadrature = new List<bool>(drives.Count);

        foreach (var drive in drives)
        {
            frequencies.Add(drive.FrequencyHz);

            waveforms.Add(drive.Waveform == Core.Model.DriveWaveform.Rectangular
                ? new Analytic.RfWaveform.Rectangular(drive.DutyCycle)
                : new Analytic.RfWaveform.Sinusoid());

            quadrature.Add(drive.Waveform != Core.Model.DriveWaveform.Rectangular);
        }

        if (frequencies.Count == 0)
        {
            frequencies.Add(1.0);
            waveforms.Add(new Analytic.RfWaveform.Sinusoid());
            quadrature.Add(true);
        }

        return (frequencies, waveforms, quadrature);
    }

    private static List<DriveChannel> Groups(Geometry3D geometry)
    {
        // Every state the electrodes pass through, a ramp's end included: a ramp from
        // zero has its whole pattern at its end, and a pattern gathered from starts alone
        // would leave it unsolved.
        var states = new List<IReadOnlyList<CompiledElectrode3D>>();
        if (geometry.Stages.Count == 0)
        {
            states.Add(geometry.Electrodes);
        }
        else
        {
            foreach (var stage in geometry.Stages)
            {
                states.Add(stage.Electrodes);
                if (stage.EndElectrodes is not null)
                {
                    states.Add(stage.EndElectrodes);
                }
            }
        }

        var (_, _, quadrature) = Clocks(geometry.Drives);

        // One state at a time, never the states flattened together: a supply's coefficients
        // are keyed by electrode name, so a flattened decomposition lets the same electrode
        // in two states collide and leaves a pattern belonging to no stage. See
        // DriveChannels.DecomposeStates.
        return DriveChannels.DecomposeStates(
            [.. states.Select(state => (IReadOnlyList<Excitation>)[.. state.Select(Excited)])],
            quadrature);
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
        new(electrode.Name, electrode.Potential, [.. electrode.Taps.Select(
            t => new DriveTap(t.Drive, t.Amplitude, t.Phase))]);

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
