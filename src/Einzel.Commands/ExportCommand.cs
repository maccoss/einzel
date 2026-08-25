using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>The outcome of an export.</summary>
public sealed record ExportOutcome
{
    /// <summary>What was exported.</summary>
    public required string What { get; init; }

    /// <summary>The format written.</summary>
    public required string Format { get; init; }

    /// <summary>Files written, as absolute paths.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>Whether anything was actually written.</summary>
    /// <remarks>False under <c>--dry-run</c> (CLI-4).</remarks>
    public required bool Written { get; init; }
}

/// <summary>
/// Writes a model's solved fields out for something else to look at.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 21 puts VTU export in Phase 1 deliberately, so ParaView supplies
/// the whole visualisation story a year before the shell exists. That argument
/// only works if export is reachable without running an ion through anything -
/// looking at a field is the commonest reason to want a picture, and it is the
/// step before a trajectory is worth computing.
/// </para>
/// <para>
/// Trajectories are exported by <c>run --vtu</c>, where they are produced. This
/// covers the field, which nothing else wrote out.
/// </para>
/// </remarks>
public static class ExportCommand
{
    /// <summary>Exports a model's solved potential fields as VTU.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be written and write nothing (CLI-4).</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The model does not validate, or has no solved field.</exception>
    public static ExportOutcome Vtu(string modelPath, ProjectLayout project, bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);

        var absolute = Path.GetFullPath(modelPath);
        var text = File.ReadAllText(absolute);
        var validation = ModelValidator.Validate(Io.ModelJson.Parse(text), null);

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0]);
        }

        var model = validation.Model!;
        var stem = Path.GetFileNameWithoutExtension(absolute);
        var artifacts = new List<string>();
        var solved = 0;

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var solve = model.Fields[index].Solve;

            if (solve is null)
            {
                continue;
            }

            solved++;
            var grid = GeometryBuilder.BuildGrid(solve);
            var mask = GeometryBuilder.BuildMask(solve, grid);

            var (potential, report) = PoissonSolver2D.Solve(
                mask,
                solve.Tolerance,
                maximumCycles: 400,
                coarsen: coarse => GeometryBuilder.BuildMask(solve, coarse));

            if (!report.Converged)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.ConvergenceFailed,
                    Path = $"/fields/{index}",
                    Constraint = $"the solve did not converge in {report.Cycles} cycles",
                    Suggestion = "a picture of an unconverged field would look like a result; run "
                        + "'einzel solve' to see the residual and the convergence factor",
                });
            }

            // Named by element index as well as model, because a model with two
            // solved elements would otherwise write one file twice.
            var name = model.Fields.Count(f => f.Solve is not null) > 1
                ? $"{stem}.field{index}.vti"
                : $"{stem}.field.vti";

            var path = Path.Combine(project.Scratch, name);
            artifacts.Add(path);

            if (!dryRun)
            {
                Directory.CreateDirectory(project.Scratch);
                File.WriteAllText(path, Io.VtuWriter.WriteScalarField(potential, "potential_V"));
            }
        }

        if (solved == 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/fields",
                Constraint = "this model has no solved field to export",
                Suggestion = "only a 'solved2d' field element has a grid to write; analytic fields are "
                    + "formulas and have nothing to sample",
            });
        }

        return new ExportOutcome
        {
            What = "field",
            Format = "vti",
            Artifacts = artifacts,
            Written = !dryRun,
        };
    }
}
