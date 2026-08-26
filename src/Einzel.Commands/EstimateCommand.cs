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
        var validation = ModelValidator.Validate(document, null);

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
            var (diffusiveSeconds, diffusiveMemory, diffusiveBasis) = Diffusive(model);

            seconds += diffusiveSeconds;
            memory = Math.Max(memory, diffusiveMemory);
            basis = diffusiveBasis;
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
    private static (double Seconds, double MemoryMiB, string Basis) Diffusive(CompiledModel model)
    {
        var grid = DiffusionRun.GridFor(model);
        var gas = Transport.Collisions.BackgroundGas.FromModel(model.Gas);
        var species = Transport.IonSpecies.FromModel(model);

        var mobility = model.Mobility is { Derived: false } declared
            ? declared.ZeroFieldSi
            : Transport.Diffusion.Mobility.FromCrossSection(gas, species).ZeroFieldSi;

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

            for (var j = 0; j < grid.CountY; j += 2)
            {
                for (var i = 0; i < grid.CountX; i += 2)
                {
                    var point = new Core.Geometry.Vec3(grid.X(i), grid.Y(j), 0.0);
                    var electric = field.ElectricFieldAt(in point);

                    fastestDrift = Math.Max(
                        fastestDrift,
                        Transport.Diffusion.DriftDiffusion.CrossingRate(
                            grid, sign * mobility * electric.X, sign * mobility * electric.Y));
                }
            }
        }

        var (step, limit) = Transport.Diffusion.DriftDiffusion.StepFor(grid, diffusion, fastestDrift);

        var steps = Math.Max(1.0, Math.Ceiling(model.MaximumFlightTimeSi / step));
        var cells = steps * grid.NodeCount;

        var seconds = cells / (MegaCellsPerSecond * 1e6);

        // Two density fields, current and next, at eight bytes a node.
        var memory = 2.0 * 8.0 * grid.NodeCount / (1024.0 * 1024.0);

        return (
            seconds,
            memory,
            $"diffusive run: {grid.CountX} by {grid.CountY} nodes, a stability-limited step of "
            + $"{step:G3} s set by {limit}, so about {steps:N0} steps over "
            + $"{model.MaximumFlightTimeSi * 1e6:G4} us, at {MegaCellsPerSecond:G2} million cell "
            + "updates per second measured on this codebase. "
            + (analytic
                ? "Both stability limits are included: this model's fields are analytic, so the "
                    + "drift limit costs nothing to evaluate."
                : "The drift limit is NOT included, because it needs a field this has not solved - "
                    + "so the real step can only be smaller and this is a lower bound.")
            + " Note that the diffusion coefficient goes as one over pressure: a thinner gas is "
            + "MORE expensive here, not less");
    }
}
