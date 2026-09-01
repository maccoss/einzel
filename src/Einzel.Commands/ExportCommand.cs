using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Project;
using Einzel.Render;

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
    /// <summary>Exports the conductor surfaces as a Wavefront OBJ mesh.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be written and write nothing (CLI-4).</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// <b>The extraction is <see cref="ViewportCommand"/>'s, not a second one.</b> It already
    /// turns each electrode's signed distance into an oriented surface and knows what a solve
    /// claims about the third dimension — a cross-section extrudes, an axisymmetric half-plane
    /// revolves, a volume solve is extracted properly. Writing that again here would be two
    /// implementations of one geometry, which is how they come to disagree; this method is a
    /// file format and nothing else.
    /// </para>
    /// <para>
    /// <b>Each electrode keeps its own name and its potential travels with it</b> as a comment,
    /// because a grey mesh of eleven identical-looking plates is not much use for a figure and
    /// the number a reader wants to colour by is the one the model declared. The drive
    /// amplitude is written too where there is one: an electrode holding zero DC and all of its
    /// potential as RF reads as earthed otherwise, which is the mistake this project has made
    /// six times.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The model does not validate, or declares no conductors.</exception>
    public static ExportOutcome Mesh(string modelPath, ProjectLayout project, bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);

        var absolute = Path.GetFullPath(modelPath);
        var viewport = ViewportCommand.Execute(absolute);

        if (viewport.Conductors.Count == 0)
        {
            // The two ways to get here are different problems and the message has to say
            // which. A model with no electrodes has nothing to mesh and never did; a model
            // with electrodes that produced no surface is a defect in the extraction, and
            // telling that author to "check the model declares electrodes" sends them to
            // look at the one thing that is already right.
            var declared = ModelValidator.Validate(
                Io.ModelJson.Parse(File.ReadAllText(absolute)), null,
                Path.GetDirectoryName(absolute)).Model?.Fields
                .Sum(f => (f.Solve?.Electrodes.Count ?? 0) + (f.Solve3D?.Electrodes.Count ?? 0))
                ?? 0;

            throw new EinzelException(new EinzelError
            {
                Code = "NOTHING_TO_EXPORT",
                Path = "/fields",
                Constraint = "a mesh export needs at least one conductor surface",
                Observed = new ObservedValue(0, "conductor surfaces"),
                Suggestion = declared == 0
                    ? "this model declares no electrodes. Only a solved element has any - an "
                        + "analytic field is a formula and has no geometry to mesh - so check "
                        + "that it has a 'solve', 'solve3d' or axisymmetric element with "
                        + "electrodes in it"
                    : Vanished(declared),
                Severity = ErrorSeverity.Error,
            });
        }

        var parts = viewport.Conductors
            .Select(c => new NamedSurface(
                c.Name,
                c.VerticesMm,
                c.Normals,
                c.Triangles,
                c.DriveAmplitudeVolts != 0.0
                    ? FormattableString.Invariant(
                        $"{c.Name}: {c.PotentialVolts:G6} V DC, drive amplitude {c.DriveAmplitudeVolts:G6} V")
                    : FormattableString.Invariant($"{c.Name}: {c.PotentialVolts:G6} V")))
            .ToList();

        var stem = Path.GetFileNameWithoutExtension(absolute);
        var path = Path.Combine(project.Scratch, $"{stem}.conductors.obj");

        var triangles = parts.Sum(p => p.Triangles.Count / 3);

        if (!dryRun)
        {
            Directory.CreateDirectory(project.Scratch);
            File.WriteAllText(
                path,
                ObjWriter.Write(
                    parts,
                    [
                        FormattableString.Invariant($"model: {stem}"),
                        FormattableString.Invariant(
                            $"{parts.Count} conductor(s), {triangles} triangles"),
                        "surfaces are the zero level set of each electrode's signed distance",
                    ]));
        }

        return new ExportOutcome
        {
            What = "conductors",
            Format = "obj",
            Artifacts = [path],
            Written = !dryRun,
        };
    }

    /// <summary>What to say when electrodes were declared and none of them meshed.</summary>
    /// <remarks>
    /// Split into its own method because <c>FormattableString.Invariant</c> takes one
    /// interpolated string and not a concatenation of one with several literals, which is
    /// the shape a long message naturally has.
    /// </remarks>
    private static string Vanished(int declared)
    {
        var count = FormattableString.Invariant($"this model declares {declared} electrode(s)");

        return count
            + " and none of them produced a surface, which is a defect in the extraction "
            + "rather than in the model. It has happened twice for sub-cell geometry: an "
            + "electrode thinner than the sampling step falls between lattice planes and "
            + "vanishes silently";
    }

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
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(text), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0]);
        }

        var model = validation.Model!;
        var stem = Path.GetFileNameWithoutExtension(absolute);
        var artifacts = new List<string>();
        var solved = 0;

        var solvedElements = model.Fields.Count(f => f.Solve is not null || f.Solve3D is not null);

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var solve = model.Fields[index].Solve;
            var solve3d = model.Fields[index].Solve3D;

            if (solve is null && solve3d is null)
            {
                continue;
            }

            solved++;

            // Named by element index as well as model, because a model with two
            // solved elements would otherwise write one file twice.
            var name = solvedElements > 1
                ? $"{stem}.field{index}.vti"
                : $"{stem}.field.vti";

            var path = Path.Combine(project.Scratch, name);

            if (solve3d is not null)
            {
                var geometry = new Geometry3D(
                    solve3d.MinX, solve3d.MinY, solve3d.MinZ,
                    solve3d.MaxX, solve3d.MaxY, solve3d.MaxZ,
                    solve3d.CellSize,
                    solve3d.Electrodes,
                    solve3d.Tolerance)
                {
                    Drives = solve3d.Drives,
                    Stages = solve3d.Stages,
                    Faces = Geometry3D.FacesOf(solve3d.Faces),
                    ReflectAboutX = solve3d.ReflectAboutX,
                };

                var channels = GeometryBuilder3D.SolveChannels(geometry);

                foreach (var channel in channels)
                {
                    if (!channel.Report.Converged)
                    {
                        throw new EinzelException(new EinzelError
                        {
                            Code = ErrorCodes.ConvergenceFailed,
                            Path = $"/fields/{index}",
                            Constraint =
                                $"basis channel {channel.Index} did not converge in "
                                + $"{channel.Report.Cycles} cycles",
                            Suggestion = "a picture of an unconverged field would look like a "
                                + "result; run 'einzel solve' to see the residual and the "
                                + "convergence factor for every channel",
                        });
                    }
                }

                // One file per basis channel, because that is what was solved. A
                // driven structure has no single potential to draw - the thing an
                // ion sees is a weighted sum that changes within an RF cycle - and
                // writing one file called "the field" would be picking a phase and
                // not saying which.
                foreach (var channel in channels)
                {
                    var channelPath = channels.Count > 1
                        ? path.Replace(".vti", $".channel{channel.Index}.vti", StringComparison.Ordinal)
                        : path;

                    artifacts.Add(channelPath);

                    if (!dryRun)
                    {
                        Directory.CreateDirectory(project.Scratch);

                        File.WriteAllText(channelPath, Io.VtuWriter.WriteScalarField(
                            channel.Potential,
                            channels.Count > 1 ? $"potential_channel{channel.Index}_V" : "potential_V"));
                    }
                }

                continue;
            }

            var grid = GeometryBuilder.BuildGrid(solve!);
            var mask = GeometryBuilder.BuildMask(solve!, grid);

            var (potential, report) = PoissonSolver2D.Solve(
                mask,
                solve!.Tolerance,
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
                Suggestion = "only a 'solved2d' or 'solved3d' field element has a grid to write; "
                    + "analytic fields are formulas and have nothing to sample",
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
