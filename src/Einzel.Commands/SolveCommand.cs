using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Commands;

/// <summary>How one solved field element went.</summary>
public sealed record SolvedElement
{
    /// <summary>Which field element of the model this is.</summary>
    public required int Index { get; init; }

    /// <summary>Node counts, x then y.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>Node spacing along each axis, in millimetres.</summary>
    public required IReadOnlyList<double> SpacingMm { get; init; }

    /// <summary>Whether the grid has square cells.</summary>
    public required bool SquareCells { get; init; }

    /// <summary>Electrodes rasterised onto the grid.</summary>
    public required int Electrodes { get; init; }

    /// <summary>Nodes holding a fixed potential.</summary>
    public required int FixedNodes { get; init; }

    /// <summary>Stencil arms cut short by a conductor surface between nodes.</summary>
    public required int CutLinks { get; init; }

    /// <summary>V-cycles taken.</summary>
    public required int Cycles { get; init; }

    /// <summary>Residual reduction per cycle.</summary>
    public required double ConvergenceFactor { get; init; }

    /// <summary>Residual relative to the initial one.</summary>
    public required double RelativeResidual { get; init; }

    /// <summary>Whether the solve met its tolerance.</summary>
    public required bool Converged { get; init; }

    /// <summary>Largest potential magnitude anywhere on the grid, in volts.</summary>
    /// <remarks>
    /// The maximum principle in one number. No potential in a Laplace solution may
    /// exceed the largest applied value, so a peak above the applied potentials is
    /// a tolerance-free proof that the solve diverged - the cheapest check there
    /// is, and the one that caught interior-electrode coarsening reaching 1e134 V.
    /// </remarks>
    public required double PeakPotentialVolts { get; init; }
}

/// <summary>The outcome of solving a model's fields.</summary>
public sealed record SolveOutcome
{
    /// <summary>The model file, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Content hash of the model document.</summary>
    public required string ModelHash { get; init; }

    /// <summary>One entry per solved field element, in document order.</summary>
    public required IReadOnlyList<SolvedElement> Elements { get; init; }

    /// <summary>Wall-clock milliseconds spent solving.</summary>
    public required double ElapsedMs { get; init; }

    /// <summary>Whether every element converged.</summary>
    public bool Converged => Elements.All(e => e.Converged);
}

/// <summary>
/// Solves a model's fields without tracking anything through them.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>run</c> because the field and the trajectory fail in
/// different ways and are worth being able to look at separately. A flight time
/// that comes out wrong is either a bad field or a bad integration, and the first
/// question is always which - answering it should not require running an ion.
/// </para>
/// <para>
/// It also reports what the discretisation actually did with the geometry: how
/// many nodes an electrode claimed, how many stencil arms were cut short by a
/// surface between nodes, whether the cells came out square. Those are the numbers
/// that explain a solve, and none of them are visible from a flight time.
/// </para>
/// </remarks>
public static class SolveCommand
{
    /// <summary>Solves every solved field element in a model.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static SolveOutcome Execute(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var text = File.ReadAllText(absolute);
        var document = Io.ModelJson.Parse(text);
        var validation = ModelValidator.Validate(document, null);

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors[0]);
        }

        var model = validation.Model!;
        var elements = new List<SolvedElement>();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var solve = model.Fields[index].Solve;

            if (solve is null)
            {
                // An analytic element has nothing to solve; it is not an error and
                // not something to report a residual for.
                continue;
            }

            var grid = GeometryBuilder.BuildGrid(solve);
            var mask = GeometryBuilder.BuildMask(solve, grid);

            var (potential, report) = PoissonSolver2D.Solve(
                mask,
                solve.Tolerance,
                maximumCycles: 400,
                coarsen: coarse => GeometryBuilder.BuildMask(solve, coarse));

            var peak = 0.0;

            foreach (var value in potential.Values)
            {
                peak = Math.Max(peak, Math.Abs(value));
            }

            elements.Add(new SolvedElement
            {
                Index = index,
                Nodes = [grid.CountX, grid.CountY],
                SpacingMm = [grid.SpacingX * 1e3, grid.SpacingY * 1e3],
                SquareCells = grid.IsSquare,
                Electrodes = solve.Electrodes.Count,
                FixedNodes = mask.FixedCount,
                CutLinks = mask.Cuts?.CutCount ?? 0,
                Cycles = report.Cycles,
                ConvergenceFactor = report.ConvergenceFactor,
                RelativeResidual = report.InitialResidual > 0.0
                    ? report.FinalResidual / report.InitialResidual
                    : 0.0,
                Converged = report.Converged,
                PeakPotentialVolts = peak,
            });
        }

        return new SolveOutcome
        {
            ModelPath = absolute,
            ModelHash = Project.ContentHash.OfText(text),
            Elements = elements,
            ElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        };
    }
}
