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


/// <summary>Whether a volume mask asks each electrode only about the nodes near it.</summary>
/// <remarks>
/// CMP-1 requires that a reference implementation is never deleted or allowed to rot,
/// and a reference nothing can select is one that rots quietly. The unculled loops are
/// what the culled ones are checked against, node for node and arm for arm, on every
/// shipped volume geometry - so they are kept as written and kept reachable.
/// </remarks>
internal enum MaskCulling
{
    /// <summary>Each electrode is asked only about the nodes and links its bounding box reaches.</summary>
    Bounds,

    /// <summary>Every electrode is asked about every node and every link: the reference.</summary>
    None,
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
    /// <para>
    /// Sub-cell: conductor surfaces between nodes are recorded as cut links, which
    /// is where the accuracy of a solve comes from. Coarse multigrid levels are
    /// built by <see cref="Coarsener(Geometry3D, Func{CompiledElectrode3D, double}?)"/>
    /// instead and are deliberately different.
    /// </para>
    /// <para>
    /// <b>Each electrode is asked only about the nodes its bounding box reaches.</b>
    /// Asking every electrode about every node and every link costs nodes times
    /// electrodes, and on the Astral's foil solve - 1.1 million nodes, 86 electrodes -
    /// that was half a billion closed-form entry tests per mask and half of a whole
    /// <c>einzel solve</c> of the template - 10.3 s of 20.6 in Release, where the culled
    /// mask takes 0.15 s. The mask is bit-identical to the unculled one on every shipped
    /// volume geometry, checked node for node and arm for arm against the reference
    /// loops.
    /// </para>
    /// </remarks>
    public static DirichletMask3D BuildMask(
        Geometry3D geometry, Grid3D grid, Func<CompiledElectrode3D, double>? potentialOf = null) =>
        BuildMask(geometry, grid, potentialOf, MaskCulling.Bounds);

    /// <summary>Builds the finest-level mask, culled or by the reference loops.</summary>
    internal static DirichletMask3D BuildMask(
        Geometry3D geometry,
        Grid3D grid,
        Func<CompiledElectrode3D, double>? potentialOf,
        MaskCulling culling) =>
        Assemble(geometry, grid, potentialOf, coarse: false, culling);

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
    /// Handed out as a whole function rather than as a flag on
    /// <see cref="BuildMask(Geometry3D, Grid3D, Func{CompiledElectrode3D, double}?)"/>
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
        Geometry3D geometry, Func<CompiledElectrode3D, double>? potentialOf = null) =>
        Coarsener(geometry, potentialOf, MaskCulling.Bounds);

    /// <summary>The coarse-level builder, culled or by the reference loops.</summary>
    internal static Func<Grid3D, DirichletMask3D> Coarsener(
        Geometry3D geometry, Func<CompiledElectrode3D, double>? potentialOf, MaskCulling culling)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var cache = new Dictionary<(int X, int Y, int Z), DirichletMask3D>();

        return grid =>
        {
            var key = (grid.CountX, grid.CountY, grid.CountZ);

            if (!cache.TryGetValue(key, out var mask))
            {
                mask = Assemble(geometry, grid, potentialOf, coarse: true, culling);
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
        bool coarse,
        MaskCulling culling)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(grid);

        var mask = new DirichletMask3D(grid);

        // Where each electrode can be, in node indices, or null for the reference loops.
        var reach = culling == MaskCulling.Bounds ? Reach(geometry.Electrodes, grid) : null;

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

        for (var index = 0; index < geometry.Electrodes.Count; index++)
        {
            var electrode = geometry.Electrodes[index];
            var potential = potentialOf?.Invoke(electrode) ?? electrode.Potential;

            var found = reach is null
                ? Rasterise(mask, grid, electrode, potential)
                : Rasterise(mask, grid, electrode, potential, reach[index]);

            if (!found && coarse)
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
            if (reach is null)
            {
                AddCuts(geometry, grid, mask, potentialOf);
            }
            else
            {
                AddCuts(geometry, grid, mask, potentialOf, reach);
            }
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

        if (geometry.Drives.Count == 0 && geometry.Stages.Count == 0)
        {
            var (statik, staticReport) = Build(geometry);
            return (Reflected(statik, geometry), staticReport);
        }

        var groups = Groups(geometry);
        var solves = SolveGroups(geometry, groups);

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

            return [new ChannelSolve(0, mask, potential, report)];
        }

        return SolveGroups(geometry, Groups(geometry));
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

    /// <summary>
    /// The nodes an electrode's bounding box reaches: an index range per axis, with one
    /// node of slack at each end.
    /// </summary>
    /// <param name="LowI">First node index along x.</param>
    /// <param name="HighI">Last node index along x, inclusive.</param>
    /// <param name="LowJ">First node index along y.</param>
    /// <param name="HighJ">Last node index along y, inclusive.</param>
    /// <param name="LowK">First node index along z.</param>
    /// <param name="HighK">Last node index along z, inclusive.</param>
    private readonly record struct NodeBox(
        int LowI, int HighI, int LowJ, int HighJ, int LowK, int HighK)
    {
        /// <summary>Whether no node of the grid is near the electrode at all.</summary>
        public bool IsEmpty => LowI > HighI || LowJ > HighJ || LowK > HighK;

        /// <summary>Whether a link spanning these node indices meets the box.</summary>
        /// <remarks>
        /// A link is closed at both ends, so an arm running along the face of a box and an
        /// arm ending on it both count. The slack in the box itself already puts a whole
        /// cell between anything this refuses and the electrode's surface.
        /// </remarks>
        public bool Meets(int lowI, int highI, int lowJ, int highJ, int lowK, int highK) =>
            highI >= LowI && lowI <= HighI
            && highJ >= LowJ && lowJ <= HighJ
            && highK >= LowK && lowK <= HighK;
    }

    /// <summary>Where every electrode can be, in node indices, in declaration order.</summary>
    /// <remarks>
    /// <para>
    /// <b>The bounding box is trusted as conservative</b> - every point where
    /// <see cref="CompiledElectrode3D.Contains"/> holds lies inside it - which the volume
    /// overlap check already relies on to settle most pairs without a search. It is the
    /// electrode's own statement about where it is, so nothing here switches on a shape
    /// and a sixth primitive is culled the day it has a bounding box.
    /// </para>
    /// <para>
    /// <b>Floor and ceiling, then a node more each way, never rounding to nearest.</b>
    /// <c>Contains</c> is a signed distance at most zero, so a node lying exactly on a face
    /// is inside, and a bound converted to an index is a division that may land a rounding
    /// either side of the integer that node sits at. Floor and ceiling already keep every
    /// such node; the extra node is there so that a bound a few ulps short of the metal it
    /// bounds - a tilted box's corners come from a rotation, its signed distance from the
    /// inverse one - can never drop a node the reference loops would have fixed. It costs a
    /// layer of nodes per face of each box and is worth it: the claim is bit-identity with
    /// the unculled mask, and slack is what makes that a property rather than a coincidence
    /// of where the faces fell on this mesh.
    /// </para>
    /// </remarks>
    private static NodeBox[] Reach(IReadOnlyList<CompiledElectrode3D> electrodes, Grid3D grid)
    {
        var boxes = new NodeBox[electrodes.Count];

        for (var index = 0; index < electrodes.Count; index++)
        {
            var (minX, minY, minZ, maxX, maxY, maxZ) = electrodes[index].Bounds;

            var (lowI, highI) = Span(minX, maxX, grid.OriginX, grid.SpacingX, grid.CountX);
            var (lowJ, highJ) = Span(minY, maxY, grid.OriginY, grid.SpacingY, grid.CountY);
            var (lowK, highK) = Span(minZ, maxZ, grid.OriginZ, grid.SpacingZ, grid.CountZ);

            boxes[index] = new NodeBox(lowI, highI, lowJ, highJ, lowK, highK);
        }

        return boxes;
    }

    /// <summary>The node indices along one axis that an interval reaches, with a node of slack.</summary>
    /// <returns>An inclusive range, empty (low above high) when it misses the grid.</returns>
    private static (int Low, int High) Span(
        double lower, double upper, double origin, double spacing, int count)
    {
        var low = Math.Floor((Math.Min(lower, upper) - origin) / spacing) - 1.0;
        var high = Math.Ceiling((Math.Max(lower, upper) - origin) / spacing) + 1.0;

        if (double.IsNaN(low) || double.IsNaN(high))
        {
            // A box that cannot say where it is is asked about everywhere, which is what
            // the reference does for every electrode.
            return (0, count - 1);
        }

        // Clamped in floating point before the cast, so a bound far outside the grid - or
        // an infinite one - never overflows an int on its way to being clamped.
        low = Math.Max(low, 0.0);
        high = Math.Min(high, count - 1.0);

        return low > high ? (0, -1) : ((int)low, (int)high);
    }

    /// <summary>Fixes every node inside an electrode, and says whether it found any.</summary>
    /// <remarks>
    /// Only the nodes its bounding box reaches are asked. The loop body is the reference's,
    /// and the order is the reference's too - electrode by electrode in declaration order,
    /// then z, y, x - which matters because the mask is written electrode by electrode and
    /// where two overlap at one potential the last one written wins.
    /// </remarks>
    private static bool Rasterise(
        DirichletMask3D mask, Grid3D grid, CompiledElectrode3D electrode, double potential, NodeBox box)
    {
        var any = false;

        for (var k = box.LowK; k <= box.HighK; k++)
        {
            var z = grid.Z(k);

            for (var j = box.LowJ; j <= box.HighJ; j++)
            {
                var y = grid.Y(j);

                for (var i = box.LowI; i <= box.HighI; i++)
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

    /// <summary>Fixes every node inside an electrode, and says whether it found any.</summary>
    /// <remarks>
    /// The reference (CMP-1): every node of the grid, asked of every electrode. Kept as
    /// written and reachable through <see cref="MaskCulling.None"/>, because it is what the
    /// culled loop is checked against and a reference nothing runs is one nobody can trust.
    /// </remarks>
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

    /// <summary>Records every cut link, asking every electrode about every arm.</summary>
    /// <remarks>
    /// The reference (CMP-1), reachable through <see cref="MaskCulling.None"/>. Six arms
    /// of every free node against every electrode is half a billion entry tests on the
    /// Astral's foil solve, which is why it is not the path a solve takes.
    /// </remarks>
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

    /// <summary>How many nodes a side a candidate block covers, as a power of two.</summary>
    /// <remarks>
    /// Eight. Small enough that a thin electrode's list stays out of blocks it is nowhere
    /// near, large enough that the lists cost little beside the mask they serve - the
    /// Astral's foil grid is three thousand blocks.
    /// </remarks>
    private const int BlockShift = 3;

    /// <summary>Records every cut link, asking only the electrodes near each arm.</summary>
    /// <remarks>
    /// <para>
    /// <b>Two screens, and the order the reference asks in survives both.</b> The grid is
    /// divided into blocks, each listing the electrodes whose reach any link from one of
    /// its nodes can touch; then each arm asks only those of its block's electrodes whose
    /// box the arm itself meets. A list is built by walking the electrodes in declaration
    /// order and is therefore in that order, which is load-bearing: two surfaces at exactly
    /// the same fraction along one arm go to the one declared first, as in the reference.
    /// </para>
    /// <para>
    /// <b>What is skipped could only ever have been a miss.</b> An arm that does not meet an
    /// electrode's box is at least a cell from its surface, where the reference's entry test
    /// returns nothing - so skipping it changes no fraction, no potential, and no count.
    /// </para>
    /// </remarks>
    private static void AddCuts(
        Geometry3D geometry,
        Grid3D grid,
        DirichletMask3D mask,
        Func<CompiledElectrode3D, double>? potentialOf,
        NodeBox[] reach)
    {
        var cuts = new CutLinks3D(grid);

        var blocksX = ((grid.CountX - 1) >> BlockShift) + 1;
        var blocksY = ((grid.CountY - 1) >> BlockShift) + 1;
        var blocks = Candidates(reach, grid, blocksX, blocksY);

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

                    var near = blocks[
                        (i >> BlockShift) + (blocksX * ((j >> BlockShift) + (blocksY * (k >> BlockShift))))];

                    if (near.Length == 0)
                    {
                        continue;
                    }

                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 1, 0, 0, Arm3D.East);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, -1, 0, 0, Arm3D.West);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, 1, 0, Arm3D.North);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, -1, 0, Arm3D.South);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, 0, 1, Arm3D.Up);
                    CutTowards(geometry, reach, near, grid, cuts, potentialOf, i, j, k, 0, 0, -1, Arm3D.Down);
                }
            }
        }

        if (cuts.CutCount > 0)
        {
            mask.Cuts = cuts;
        }
    }

    /// <summary>
    /// For each block of nodes, the electrodes a link from one of its nodes could meet, in
    /// declaration order.
    /// </summary>
    /// <remarks>
    /// A link reaches one node beyond the node it starts from, so a block is handed every
    /// electrode whose reach, widened by that one node, overlaps it.
    /// </remarks>
    private static int[][] Candidates(NodeBox[] reach, Grid3D grid, int blocksX, int blocksY)
    {
        var blocksZ = ((grid.CountZ - 1) >> BlockShift) + 1;
        var lists = new List<int>?[blocksX * blocksY * blocksZ];

        for (var index = 0; index < reach.Length; index++)
        {
            var box = reach[index];

            if (box.IsEmpty)
            {
                continue;
            }

            var fromI = Math.Max(box.LowI - 1, 0) >> BlockShift;
            var toI = Math.Min(box.HighI + 1, grid.CountX - 1) >> BlockShift;
            var fromJ = Math.Max(box.LowJ - 1, 0) >> BlockShift;
            var toJ = Math.Min(box.HighJ + 1, grid.CountY - 1) >> BlockShift;
            var fromK = Math.Max(box.LowK - 1, 0) >> BlockShift;
            var toK = Math.Min(box.HighK + 1, grid.CountZ - 1) >> BlockShift;

            for (var bk = fromK; bk <= toK; bk++)
            {
                for (var bj = fromJ; bj <= toJ; bj++)
                {
                    for (var bi = fromI; bi <= toI; bi++)
                    {
                        (lists[bi + (blocksX * (bj + (blocksY * bk)))] ??= []).Add(index);
                    }
                }
            }
        }

        var blocks = new int[lists.Length][];

        for (var block = 0; block < lists.Length; block++)
        {
            blocks[block] = lists[block] is { } list ? [.. list] : [];
        }

        return blocks;
    }

    /// <summary>Cuts one arm against the electrodes near it.</summary>
    /// <remarks>
    /// The body is the reference's, line for line, over a shorter list: the same endpoints,
    /// the same entry test, the same refusal of a surface at zero and the same strict
    /// comparison, so the same electrode wins every arm.
    /// </remarks>
    private static void CutTowards(
        Geometry3D geometry,
        NodeBox[] reach,
        int[] near,
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

        var lowI = Math.Min(i, ni);
        var highI = Math.Max(i, ni);
        var lowJ = Math.Min(j, nj);
        var highJ = Math.Max(j, nj);
        var lowK = Math.Min(k, nk);
        var highK = Math.Max(k, nk);

        var fromX = grid.X(i);
        var fromY = grid.Y(j);
        var fromZ = grid.Z(k);

        var toX = grid.X(ni);
        var toY = grid.Y(nj);
        var toZ = grid.Z(nk);

        var nearest = 1.0;
        var potential = 0.0;
        var found = false;

        foreach (var index in near)
        {
            if (!reach[index].Meets(lowI, highI, lowJ, highJ, lowK, highK))
            {
                continue;
            }

            var electrode = geometry.Electrodes[index];

            if (electrode.FirstEntry(fromX, fromY, fromZ, toX, toY, toZ) is not { } entry || entry >= nearest)
            {
                continue;
            }

            // A surface at zero is the node itself, which rasterization has already
            // decided; see the reference for why accepting it is expensive.
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
