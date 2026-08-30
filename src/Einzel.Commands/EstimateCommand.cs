using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Commands;

/// <summary>What one field element will cost to solve.</summary>
public sealed record ElementEstimate
{
    /// <summary>Which field element of the model this is.</summary>
    public required int Index { get; init; }

    /// <summary>The field type.</summary>
    public required string Type { get; init; }

    /// <summary>Node counts, x then y.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>Total nodes.</summary>
    public required int NodeCount { get; init; }

    /// <summary>Working memory for the solve, in mebibytes.</summary>
    public required double MemoryMiB { get; init; }

    /// <summary>Estimated seconds to solve.</summary>
    public required double Seconds { get; init; }
}

/// <summary>What a model will cost to run.</summary>
public sealed record EstimateOutcome
{
    /// <summary>The model file, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>One entry per field element, in document order.</summary>
    public required IReadOnlyList<ElementEstimate> Elements { get; init; }

    /// <summary>Estimated total seconds.</summary>
    public required double Seconds { get; init; }

    /// <summary>Estimated peak working memory, in mebibytes.</summary>
    public required double MemoryMiB { get; init; }

    /// <summary>Whether this exceeds the cost threshold and should be confirmed.</summary>
    public required bool AboveThreshold { get; init; }

    /// <summary>What the threshold is, in seconds.</summary>
    public required double ThresholdSeconds { get; init; }

    /// <summary>How the estimate was arrived at, so it can be argued with.</summary>
    public required string Basis { get; init; }
}

/// <summary>
/// Estimates what a model costs before running it.
/// </summary>
/// <remarks>
/// <para>
/// GRD-8 gates operations above a cost threshold. Gating needs a number to gate
/// on, and it has to be available without doing the work - which rules out
/// anything measured and leaves a model of the cost.
/// </para>
/// <para>
/// The model here is deliberately crude and deliberately explicit about being
/// crude: multigrid work is proportional to node count, with a constant measured
/// on this codebase's own solves. It reports the basis it used alongside the
/// number so a caller can see it is an estimate and not a measurement. An
/// estimate presented with the same confidence as a result is worse than no
/// estimate, and this is the same argument GRD-1 makes about bare numbers.
/// </para>
/// <para>
/// It is honest about what it does not cover: trajectory integration cost depends
/// on the path, which depends on the field, which is the thing not yet solved.
/// </para>
/// </remarks>
public static class EstimateCommand
{
    /// <summary>
    /// Seconds per million nodes for a converged multigrid solve.
    /// </summary>
    /// <remarks>
    /// Measured on the shipped templates: a 129 by 129 quadrupole with four
    /// interior rods solves in roughly 210 ms, which is about 12 s per million
    /// nodes; the mirror pair's 513 by 33 boundary-value geometry is faster per
    /// node. The larger figure is used, because an estimate that runs under is
    /// worse than one that runs over.
    /// </remarks>
    private const double SecondsPerMegaNode = 13.0;

    /// <summary>Fields the solver allocates per node, at eight bytes each.</summary>
    /// <remarks>
    /// Potential, right-hand side, residual, and the correction and restriction
    /// buffers down the V-cycle hierarchy, which add about a third again since
    /// each level is a quarter of the one above.
    /// </remarks>
    private const double BytesPerNode = 8.0 * 6.0;

    /// <summary>Above this, GRD-8 asks for confirmation rather than proceeding.</summary>
    public const double ThresholdSeconds = 30.0;

    /// <summary>
    /// Cell updates per second for a diffusive step, in millions.
    /// </summary>
    /// <remarks>
    /// Measured on this codebase: 2.4 to 3.0 million across grids from 129 by 33 to
    /// 257 by 129. The low figure is used, for the same reason the solve estimate
    /// uses its high one - an estimate that runs under is worse than one that runs
    /// over, because the first is discovered by waiting.
    /// </remarks>
    private const double MegaCellsPerSecond = 2.4;

    /// <summary>
    /// Gauss-Seidel sweeps an implicit density step is assumed to take.
    /// </summary>
    /// <remarks>
    /// Measured at 3.0 on the shipped funnel at gains from 4 to 256 and 4.9 at 1024,
    /// and it is <em>not</em> a property of the gain: what decides it is how far past
    /// the diffusion limit the step lands, and on a problem already at that limit it
    /// reaches 88.7. So this is the figure for the case the implicit scheme exists for
    /// - a drift-limited driven structure - and the basis says as much rather than
    /// implying the number is general.
    /// </remarks>
    private const double SweepsPerStep = 3.0;

    /// <summary>Estimates a model's cost.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <returns>The estimate.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static EstimateOutcome Execute(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var document = Io.ModelJson.Parse(File.ReadAllText(absolute));
        var validation = ModelValidator.Validate(
            document, null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors[0]);
        }

        var model = validation.Model!;
        var elements = new List<ElementEstimate>();
        var seconds = 0.0;
        var memory = 0.0;
        var basis = $"{SecondsPerMegaNode:G3} s per million nodes for a converged V-cycle, measured on "
            + "the shipped templates. Trajectory integration is not included: its cost depends on the "
            + "path, which depends on the field this has not solved yet.";

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var element = model.Fields[index];

            if (element.Solve is null)
            {
                elements.Add(new ElementEstimate
                {
                    Index = index,
                    Type = element.Kind.ToString(),
                    Nodes = [],
                    NodeCount = 0,
                    MemoryMiB = 0.0,
                    Seconds = 0.0,
                });

                continue;
            }

            // Building the grid is arithmetic on the declared box, so asking it
            // how big it will be costs nothing and beats estimating the estimate.
            var grid = GeometryBuilder.BuildGrid(element.Solve);
            var nodes = grid.NodeCount;
            var elementSeconds = SecondsPerMegaNode * nodes / 1e6;
            var elementMemory = BytesPerNode * nodes / (1024.0 * 1024.0);

            elements.Add(new ElementEstimate
            {
                Index = index,
                Type = element.Kind.ToString(),
                Nodes = [grid.CountX, grid.CountY],
                NodeCount = nodes,
                MemoryMiB = elementMemory,
                Seconds = elementSeconds,
            });

            seconds += elementSeconds;
            memory = Math.Max(memory, elementMemory);
        }

        // A diffusive run is a time-stepped solve over a whole grid, and its step is
        // set by stability rather than declared - so unlike trajectory integration
        // its cost is knowable before the run, and unlike a field solve it is the
        // dominant term rather than a preamble.
        if (model.TransportMode == "diffusion")
        {
            var (diffusiveSeconds, diffusiveMemory, diffusiveBasis) = Diffusive(model, Path.GetDirectoryName(absolute) ?? Directory.GetCurrentDirectory());

            seconds += diffusiveSeconds;
            memory = Math.Max(memory, diffusiveMemory);
            basis = diffusiveBasis;
        }

        if (model.ModelsSpaceCharge)
        {
            var (chargeSeconds, chargeBasis) = SelfField(model);

            seconds += chargeSeconds;
            basis = basis + " " + chargeBasis;
        }

        return new EstimateOutcome
        {
            ModelPath = absolute,
            Elements = elements,
            Seconds = seconds,
            MemoryMiB = memory,
            AboveThreshold = seconds > ThresholdSeconds,
            ThresholdSeconds = ThresholdSeconds,
            Basis = basis,
        };
    }

    /// <summary>What the mutual force will cost, by whichever method computes it.</summary>
    /// <remarks>
    /// <para>
    /// GRD-8 gates an operation above a cost threshold and needs a number to gate
    /// on without doing the work. Direct space charge is the first thing here whose
    /// cost is <em>quadratic</em> in a number a user types: raising a cloud from 150
    /// trajectories to 2,000 is a factor of 178, and 87 seconds becomes four hours.
    /// A linear intuition is exactly wrong, so the basis says so in words.
    /// </para>
    /// <para>
    /// <b>Particle-in-cell is linear in that same number and is not simply cheaper.</b>
    /// It pays for one Poisson solve per refresh whatever the cloud, so below the
    /// crossing the reference method is faster - and an estimate that reported the
    /// asymptotics would recommend the approximation exactly where it loses. Both are
    /// costed in the same currency, pair-equivalents a stage, so their ratio at a
    /// given cloud is something the basis can state rather than imply.
    /// </para>
    /// <para>
    /// The step count is the one thing here that is not knowable in advance - it is
    /// whatever the adaptive controller decides - so it is taken from the flight
    /// time and a step scale, and the estimate is an order of magnitude rather than
    /// the exact number the diffusive estimate can give.
    /// </para>
    /// </remarks>
    private static (double Seconds, string Basis) SelfField(CompiledModel model)
    {
        var trajectories = Math.Max(model.Cloud.Ions, 2);
        var pairs = 0.5 * trajectories * (trajectories - 1.0);
        var steps = EstimatedSteps;

        // Seven Dormand-Prince stages, each evaluating the mutual force.
        //
        // Measured on this codebase: 150 trajectories through the rectilinear trap
        // took 87 s over roughly eleven thousand steps, which is 8.7e8 pair
        // evaluations. Rounded to one figure, because it is a rate on one machine.
        double Cost(double workPerStage) => workPerStage * StagesPerStep * steps / PairsPerSecond;

        var nodes = model.SpaceChargeGrid?.Nodes ?? 32;

        // Two terms, because the node count is now something a document sets and the
        // one it scales is the one that dominates. The gather is linear in the cloud;
        // the solve is cubic in the node count and does not see the cloud at all.
        var gridWork =
            (PicGatherPerTrajectory * trajectories)
            + (PicSolveWork * Math.Pow(nodes / (double)PicCalibrationNodes, 3.0));

        if (!string.Equals(model.SpaceChargeMode, "pic", StringComparison.Ordinal))
        {
            var basis =
                $"Space charge is summed over every pair: {trajectories:N0} trajectories give {pairs:N0} "
                + $"pairs, {StagesPerStep} stages a step and about {steps:N0} steps, at {PairsPerSecond:G2} "
                + "pair evaluations a second. THE COST IS QUADRATIC IN THE TRAJECTORY COUNT, so doubling "
                + "the cloud quadruples this - the linear intuition is exactly wrong here. Lower "
                + "\"ions\" and raise \"population\" to keep the packet's charge while computing fewer "
                + "of them."
                + (trajectories > PicCrossingTrajectories
                    ? $" Above about {PicCrossingTrajectories:N0} trajectories the grid method is the "
                        + $"cheaper one - here by about {pairs / gridWork:N1}x - so \"spaceCharge\": "
                        + "\"pic\" is worth considering, bearing in mind that this one is the reference "
                        + "it was validated against."
                    : string.Empty);

            return (Cost(pairs), basis);
        }

        // Linear in the cloud and cubic in the node count, calibrated on two things
        // actually measured: the two methods cross near 850 macroparticles at 32 nodes,
        // and the work there splits about 43 to 57 between solving and gathering.
        // Anchoring on measurements rather than on a fitted rate keeps the number a
        // reader acts on exact.
        var picBasis =
            $"Space charge is deposited onto a grid and solved: {trajectories:N0} trajectories, "
            + $"{StagesPerStep} stages a step and about {steps:N0} steps. THE COST IS LINEAR IN THE "
            + "TRAJECTORY COUNT AND CUBIC IN THE NODE COUNT, so doubling the cloud doubles the "
            + "gather while the solve is paid for whatever the cloud - which is why it is not "
            + "simply cheaper than the sum: at the default "
            + $"{PicCalibrationNodes} nodes the two methods cross near "
            + $"{PicCrossingTrajectories:N0} trajectories"
            + (gridWork > pairs
                ? $", and here the pairwise sum is about {gridWork / pairs:N1}x faster. "
                    + $"\"spaceCharge\": \"direct\" is also the reference method."
                : $", and this cloud is above it by about {pairs / gridWork:N1}x.")
            + (nodes == PicCalibrationNodes
                ? string.Empty
                : $" THE SOLVE IS CUBIC IN THE NODE COUNT: {nodes} nodes rather than "
                    + $"{PicCalibrationNodes} is "
                    + $"{Math.Pow(nodes / (double)PicCalibrationNodes, 3.0):N1}x that part of the "
                    + "work, and a finer grid is not a better answer here - accuracy has an optimum "
                    + "near one cell per mean macroparticle spacing.");

        return (Cost(gridWork), picBasis);
    }

    /// <summary>Dormand-Prince stages that each evaluate the mutual force.</summary>
    private const int StagesPerStep = 7;

    /// <summary>Pair evaluations a second, measured on this codebase's own runs.</summary>
    private const double PairsPerSecond = 1e7;

    /// <summary>Where the grid method starts beating the pairwise sum.</summary>
    /// <remarks>
    /// Measured rather than derived: 0.16x at 250 macroparticles, 0.42x at 500, 1.21x
    /// at 1,000, 3.21x at 2,000. It is the number worth stating because it is the one
    /// a reader acts on - quoting the asymptotics alone would recommend the
    /// approximation exactly where it loses.
    /// </remarks>
    private const double PicCrossingTrajectories = 850.0;

    /// <summary>The node count the crossing was measured at.</summary>
    private const int PicCalibrationNodes = 32;

    /// <summary>
    /// How the grid method's work splits between gathering and solving, at the
    /// calibration node count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured by running the same packet at 16, 32, 64 and 128 nodes and at 200 and
    /// 800 macroparticles - eight points, two knobs. A two-term fit of
    /// <c>solves x c_solve x nodes^3 + calls x c_gather x N</c> lands within 12% at
    /// every point except the smallest grid, where a per-solve overhead the model does
    /// not carry becomes visible. At 32 nodes and the crossing cloud that split is
    /// about 43% solve to 57% gather.
    /// </para>
    /// <para>
    /// It matters because the two scale with different things: 200 macroparticles took
    /// <b>0.99 s at 16 nodes and 124 s at 128</b>, a factor of 126 for a knob a
    /// document can now set. An estimate blind to it would gate on a number missing
    /// its dominant term.
    /// </para>
    /// </remarks>
    private const double PicSolveFraction = 0.43;

    /// <summary>
    /// The solve's work at <see cref="PicCalibrationNodes"/>, in pair-equivalents.
    /// </summary>
    private const double PicSolveWork =
        PicSolveFraction * 0.5 * PicCrossingTrajectories * (PicCrossingTrajectories - 1.0);

    /// <summary>
    /// The gather's work per trajectory, in the pairwise sum's own currency.
    /// </summary>
    /// <remarks>
    /// Both constants are pinned by one measured fact and one measured ratio: at
    /// <see cref="PicCrossingTrajectories"/> and <see cref="PicCalibrationNodes"/> the
    /// two methods cost the same, and <see cref="PicSolveFraction"/> says how that
    /// total divides. Costing both in pair-equivalents is what lets the estimate
    /// compare them at all, and ties the model to something measured rather than to a
    /// rate that was fitted.
    /// </remarks>
    private const double PicGatherPerTrajectory =
        (1.0 - PicSolveFraction) * 0.5 * (PicCrossingTrajectories - 1.0);

    /// <summary>
    /// Steps a packet flight takes, as an order of magnitude.
    /// </summary>
    /// <remarks>
    /// The one quantity here that is not knowable before the run: it is whatever the
    /// adaptive controller decides. Ten thousand is what the shipped templates take.
    /// </remarks>
    private const double EstimatedSteps = 1e4;

    /// <summary>
    /// What a diffusive run will cost, from the mesh and the mobility alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The step is bounded from above by the diffusion limit, which needs no field:
    /// D comes from the mobility and the temperature, and the mesh is declared. The
    /// drift limit needs the field, which is the thing not solved yet, so the real
    /// step can only be smaller and the real cost only larger - which is stated
    /// rather than left for someone to discover by waiting.
    /// </para>
    /// <para>
    /// The direction of the pressure dependence is the part worth reading. D goes as
    /// one over pressure, so a <em>thinner</em> gas diffuses faster, needs a smaller
    /// step, and costs more. That is the opposite of the event-driven mode, where a
    /// thinner gas means fewer collisions, and the opposite of most people's
    /// intuition about which regime is expensive.
    /// </para>
    /// </remarks>
    private static (double Seconds, double MemoryMiB, string Basis) Diffusive(
        CompiledModel model, string modelDirectory)
    {
        var grid = DiffusionRun.GridFor(model);

        // Resolved, because an imported pressure field changes the mobility and so
        // the stability limit, which is the whole number this function reports. An
        // estimate taken in a gas the model does not declare is an estimate of a
        // different run - and GRD-8 exists to be relied on before the work is done.
        var gas = Io.GasFlowImport.Resolve(model.Gas, modelDirectory);
        var species = Transport.IonSpecies.FromModel(model);

        var declaredMobility = model.Mobility is { Derived: false } given
            ? new Transport.Diffusion.Mobility(
                given.ZeroFieldSi, given.Alpha, given.ValidToTownsend)
            : Transport.Diffusion.Mobility.FromCrossSection(gas, species);

        // At the thinnest gas ON THIS GRID, when the density varies. Mobility goes
        // as the reciprocal of density, so both stability limits - the Courant one on
        // the drift and the diffusive one - are set where there is least to collide
        // with, and the run finds that worst case from its own per-node arrays.
        //
        // Over the tracked grid rather than over the whole imported field, and the
        // difference is not academic: a field solved on a larger box may be thinner
        // somewhere no ion is tracked through, and taking its minimum over-predicted
        // a shipped case by 50% - 2,252 steps against 1,502 - because the field ran to
        // 0.5 mbar while the grid only reached 0.75. GRD-8's claim is that estimate
        // and run agree exactly, so the two have to be asking about the same region.
        //
        // At the declared density this is ZeroFieldSi exactly - the ratio is 1.0 and
        // a zero field makes the expansion term one - so an ungraded model is
        // bit-identical to what it was and skips the sweep entirely.
        var thinnestOnGrid = gas.NumberDensitySi;

        if (gas.IsGraded)
        {
            thinnestOnGrid = double.PositiveInfinity;

            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    thinnestOnGrid = Math.Min(
                        thinnestOnGrid,
                        gas.NumberDensityAt(new Core.Geometry.Vec3(grid.X(i), grid.Y(j), 0.0)));
                }
            }
        }

        var mobility = declaredMobility.At(0.0, thinnestOnGrid, gas.NumberDensitySi);

        var diffusion = Transport.Diffusion.Mobility.DiffusionSi(
            gas.TemperatureK, species.ChargeSi, mobility);

        // The drift limit needs the field. Where every element is analytic that is
        // free to evaluate, so it is included and the estimate becomes exact; where
        // anything has to be solved it is omitted and the estimate says so, because
        // solving the field to estimate the cost of the run defeats the purpose of
        // estimating.
        var analytic = model.Fields.All(f => f.Solve is null && f.Solve3D is null);

        var fastestDrift = 0.0;

        if (analytic)
        {
            var (field, _) = Fields.FieldAssembly.BuildReported(model);
            var sign = Math.Sign(species.ChargeSi);

            // Every node where the gas is graded, every other one where it is not.
            // The run samples all of them; a stride is harmless on a field that is
            // smooth compared with the mesh and is not harmless when the mobility
            // varies too, because the fastest drift is then a product of two things
            // that peak in different places.
            var stride = gas.IsGraded ? 1 : 2;

            for (var j = 0; j < grid.CountY; j += stride)
            {
                for (var i = 0; i < grid.CountX; i += stride)
                {
                    var point = new Core.Geometry.Vec3(grid.X(i), grid.Y(j), 0.0);
                    var electric = field.ElectricFieldAt(in point);

                    var local = gas.IsGraded
                        ? declaredMobility.At(
                            0.0, gas.NumberDensityAt(in point), gas.NumberDensitySi)
                        : mobility;

                    fastestDrift = Math.Max(
                        fastestDrift,
                        Transport.Diffusion.DriftDiffusion.CrossingRate(
                            grid, sign * local * electric.X, sign * local * electric.Y));
                }
            }
        }

        // The same weight the run will use, from the same function, so the estimate
        // and the run cannot disagree about what a step is. On the axis of a
        // cylindrical solve it is four, and an estimate that assumed a plane would
        // report a quarter of the steps.
        var cylindrical = model.Fields.Any(
            f => f.Solve?.Symmetry == Core.Model.SolveSymmetry.Cylindrical);

        var weight = new Transport.Diffusion.DensityField(grid, cylindrical).LargestRadialWeight();

        var (stable, limit) = Transport.Diffusion.DriftDiffusion.StepFor(
            grid, diffusion, fastestDrift, weight);

        // The implicit scheme steps past the stability limit and pays Gauss-Seidel
        // sweeps for it, so both halves have to enter the estimate or it is an
        // estimate of a run nobody asked for. Ignoring the gain would over-state the
        // cost by that factor - the safe direction, but GRD-8 gates on this number and
        // a gate that refuses a run costing a minute is as wrong as one that waves
        // through an hour.
        var implicitly = model.DensityStep.IsImplicit;

        var step = implicitly ? stable * model.DensityStep.Gain : stable;

        var steps = Math.Max(1.0, Math.Ceiling(model.MaximumFlightTimeSi / step));

        // Measured at 3.0 on the shipped funnel across gains from 4 to 256, rising to
        // 4.9 at 1024. A sweep costs about what an explicit step costs - it is the
        // same pass over the same coefficients - so this is the multiplier on the work
        // and not a small correction.
        var sweeps = implicitly ? SweepsPerStep : 1.0;

        var cells = steps * sweeps * grid.NodeCount;

        var seconds = cells / (MegaCellsPerSecond * 1e6);

        // Two density fields, current and next, at eight bytes a node.
        var memory = 2.0 * 8.0 * grid.NodeCount / (1024.0 * 1024.0);

        return (
            seconds,
            memory,
            $"diffusive run: {grid.CountX} by {grid.CountY} nodes, a stability-limited step of "
            + $"{stable:G3} s set by {limit}, "
            + (implicitly
                ? $"stepped implicitly at {model.DensityStep.Gain:N0}x that - {step:G3} s - so "
                    + $"about {steps:N0} steps at an assumed {sweeps:F1} Gauss-Seidel sweeps each "
                    + "over "
                : $"so about {steps:N0} steps over ")
            + $"{model.MaximumFlightTimeSi * 1e6:G4} us, at {MegaCellsPerSecond:G2} million cell "
            + "updates per second measured on this codebase. "
            + (analytic
                ? "Both stability limits are included: this model's fields are analytic, so the "
                    + "drift limit costs nothing to evaluate."
                : "The drift limit is NOT included, because it needs a field this has not solved - "
                    + "so the real step can only be smaller and this is a lower bound.")
            + (implicitly
                ? " The sweep count is the one quantity here that is not knowable in advance - it "
                    + "depends on how far past the DIFFUSION limit the step lands, not on the gain "
                    + "itself - so this is an order of magnitude where the explicit estimate is "
                    + "exact."
                : string.Empty)
            + " Note that the diffusion coefficient goes as one over pressure: a thinner gas is "
            + "MORE expensive here, not less");
    }
}
