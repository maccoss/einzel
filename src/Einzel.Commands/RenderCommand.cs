using System.Globalization;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Project;
using Einzel.Render;

namespace Einzel.Commands;

/// <summary>What a render produced.</summary>
public sealed record RenderOutcome
{
    /// <summary>The model that was drawn, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Content hash of the model document.</summary>
    public required string ModelHash { get; init; }

    /// <summary>What kind of figure.</summary>
    public required string Kind { get; init; }

    /// <summary>The output format.</summary>
    public required string Format { get; init; }

    /// <summary>Frames an animation produced, or zero for a single figure.</summary>
    public int Frames { get; init; }

    /// <summary>How long the animation runs, in seconds of playback.</summary>
    public double PlaybackSeconds { get; init; }

    /// <summary>Files written, or that would be written under a dry run.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>Whether the files were actually written.</summary>
    public required bool Written { get; init; }

    /// <summary>Page size, width then height, in millimetres.</summary>
    public required IReadOnlyList<double> PageMm { get; init; }

    /// <summary>Paths drawn, by layer.</summary>
    public required IReadOnlyDictionary<string, int> Paths { get; init; }

    /// <summary>Text runs drawn.</summary>
    public required int TextRuns { get; init; }

    /// <summary>
    /// The geometric tolerance every decimated polyline respects, in millimetres.
    /// </summary>
    /// <remarks>
    /// Reported, per GRD-12: a rendering never looks more precise than its data,
    /// and a tolerance applied but not stated is the case that requirement exists
    /// for. It appears here, in the file's own metadata, and stamped on the page.
    /// </remarks>
    public required double DecimationToleranceMm { get; init; }

    /// <summary>Trajectory points kept after decimation.</summary>
    public required int TrajectoryPoints { get; init; }

    /// <summary>Trajectory points before decimation.</summary>
    public required int TrajectoryPointsSampled { get; init; }

    /// <summary>Warnings carried onto the figure, per GRD-2.</summary>
    public required IReadOnlyList<WarningJson> Warnings { get; init; }
}

/// <summary>
/// Draws a model, headlessly, into a vector file.
/// </summary>
/// <remarks>
/// <para>
/// RND-1: rendering is an engine capability, not a shell feature. This command and
/// a future figure composer are peer consumers of <c>Einzel.Render</c>, which is
/// what stops figure composition from becoming something that exists only in a
/// window (AGT-2).
/// </para>
/// <para>
/// It also serves AGT-6 directly. A geometry error is obvious in a picture and
/// invisible in JSON: an electrode a millimetre from where it was meant to be
/// reads as a plausible number and an unmistakable drawing.
/// </para>
/// </remarks>
public static class RenderCommand
{
    /// <summary>Renders a section of a model.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <param name="project">The project the output belongs to.</param>
    /// <param name="spec">What to draw.</param>
    /// <param name="outputPath">Where to write, or null to name it after the model.</param>
    /// <param name="dryRun">Report what would be written without writing it.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    public static RenderOutcome Section(
        string modelPath,
        ProjectLayout project,
        RenderSpec spec,
        string? outputPath = null,
        bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(spec);

        var absolute = Path.GetFullPath(modelPath);
        var text = File.ReadAllText(absolute);
        var validation = ModelValidator.Validate(
            ModelJson.Parse(text), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0]);
        }

        var hash = ContentHash.OfText(text);

        // PRJ-3 in a figure: enough to regenerate it, and enough to notice when it
        // has stopped matching the model beside it.
        var provenance = new List<string>
        {
            $"einzel {EngineBuild.Version}, solver behaviour {EngineBuild.SolverBehaviourVersion}",
            $"model {Path.GetFileName(absolute)} hash {hash}",
        };

        // A diffusive model has no trajectory to draw, and RND-8 forbids inventing
        // one. What it has instead is a density, so the transport is run and the
        // result handed to the renderer - the same trade the trajectory path already
        // makes, where the ion is flown to draw its path.
        //
        // Failure here is not fatal to the figure. A model whose transport refuses -
        // a regime violation, a missing mobility - still has geometry and a field
        // worth drawing, and the figure says which of the two it got.
        //
        // It did not, until now: the catch below discarded the exception, so a
        // refused run and a figure that never asked for a density produced the same
        // output and the same words. That is the fifth thing in this branch to
        // swallow evidence about why a result is missing, and the comment above
        // promised the opposite. The reason goes into the provenance block, which is
        // stamped on the page (GRD-12) and returned in --json.
        Transport.Diffusion.DensityField? density = null;

        // Not gated on spec.Trajectory. That toggle means "fly the ion and draw its
        // path", and a diffusive model has no path to draw by definition - so
        // conflating the two made --no-trajectory silently suppress the one output
        // such a model has. Two independent things, asked about independently.
        if (spec.DensityContours > 0
            && string.Equals(
                validation.Model!.TransportMode, "diffusion", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var (built, fieldWarnings) = Fields.FieldAssembly.BuildReported(validation.Model!);

                // At the declared instant when there is one, and at the end otherwise.
                // A run reports the density it ENDED with, which for a model whose ions
                // have all arrived is an empty box - correctly, and uselessly, because
                // the picture worth having is the packet in flight. Until snapshots
                // existed the only way to get one was to shorten maximumFlightTime,
                // which throws away everything after the moment being looked at.
                var outcome = spec.AtSeconds > 0.0
                    ? DiffusionRun.Execute(
                        validation.Model!,
                        built,
                        fieldWarnings,
                        snapshotSeconds: [spec.AtSeconds])
                    : DiffusionRun.Execute(validation.Model!, built, fieldWarnings);

                if (spec.AtSeconds > 0.0 && outcome.Result.Snapshots.Count == 0)
                {
                    provenance.Add(
                        $"the run ended before t = {spec.AtSeconds * 1e6:G6} us, so the density "
                        + "drawn is the one it finished with");
                }

                density = outcome.Result.Snapshots.Count > 0
                    ? outcome.Result.Snapshots[0].Density
                    : outcome.Result.Density;

                if (outcome.Result.Snapshots.Count > 0)
                {
                    provenance.Add(
                        $"density at t = {outcome.Result.Snapshots[0].AtSeconds * 1e6:G6} us, "
                        + $"asked for {spec.AtSeconds * 1e6:G6} us");
                }
            }
            catch (EinzelException refused)
            {
                density = null;

                provenance.Add(
                    "no density drawn: the transport refused - "
                    + refused.Error.Constraint);
            }
        }

        var figure = SectionRenderer.Render(validation.Model!, spec, provenance, density);

        var extension = spec.Format == FigureFormat.Pdf ? ".pdf" : ".svg";

        var path = outputPath is { } named
            ? Path.GetFullPath(named)
            : Path.Combine(
                project.Figures, Path.GetFileNameWithoutExtension(absolute) + ".section" + extension);

        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (spec.Format == FigureFormat.Pdf)
            {
                File.WriteAllBytes(path, PdfWriter.Write(figure.Scene));
            }
            else
            {
                File.WriteAllText(path, SvgWriter.Write(figure.Scene));
            }
        }

        var byLayer = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var drawn in figure.Scene.Paths)
        {
            byLayer[drawn.Layer] = byLayer.GetValueOrDefault(drawn.Layer) + 1;
        }

        return new RenderOutcome
        {
            ModelPath = absolute,
            ModelHash = hash,
            Kind = "section",
            Format = spec.Format == FigureFormat.Pdf ? "pdf" : "svg",
            Artifacts = [path],
            Written = !dryRun,
            PageMm = [figure.Scene.WidthMm, figure.Scene.HeightMm],
            Paths = byLayer,
            TextRuns = figure.Scene.Texts.Count,
            DecimationToleranceMm = figure.DecimationToleranceMm,
            TrajectoryPoints = figure.TrajectoryPoints,
            TrajectoryPointsSampled = figure.TrajectoryPointsBeforeDecimation,
            Warnings = [.. figure.Warnings.Select(w => new WarningJson
            {
                Code = w.Code,
                Message = w.Message,
                Severity = w.Severity.ToString(),
                Suppressible = w.IsSuppressible,
            })],
        };
    }

    /// <summary>Draws a flight as a sequence of vector frames (RND-7).</summary>
    /// <param name="modelPath">The model to animate.</param>
    /// <param name="project">Where figures go.</param>
    /// <param name="spec">What to draw, carrying the declared time mapping.</param>
    /// <param name="outputDirectory">Where to write the frames, or null for the default.</param>
    /// <param name="dryRun">Report what would happen and write nothing (CLI-4).</param>
    /// <returns>What was drawn.</returns>
    /// <exception cref="ArgumentException">The model path is blank.</exception>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">
    /// The model does not validate, the spec declares no time mapping, or the transport
    /// mode produces no trajectories.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A numbered sequence and a manifest, not a video. Nothing here rasterises and
    /// LIC-1 keeps ffmpeg out of the default build, so assembling the frames is an
    /// out-of-process step with a tool the user supplies - its absence degrades a
    /// feature rather than blocking the platform.
    /// </para>
    /// <para>
    /// The manifest is written because the mapping is the thing a reader has to be able
    /// to check. Every frame carries its rate on the page, and <c>frames.json</c>
    /// carries the whole schedule - which instant each frame shows and at what rate -
    /// so the compression can be audited rather than taken on trust.
    /// </para>
    /// </remarks>
    public static RenderOutcome Animation(
        string modelPath,
        ProjectLayout project,
        RenderSpec spec,
        string? outputDirectory = null,
        bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Animation is null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/animation",
                Constraint = "this render spec declares no time mapping, and an animation "
                    + "cannot be drawn without one",
                Suggestion = "add an \"animation\" block with \"framesPerSecond\" and a list of "
                    + "\"phases\", each naming the simulated time it runs to and the rate it "
                    + "plays at. RND-7 makes this explicit rather than defaulted because six "
                    + "orders of magnitude of timescale cannot be shown honestly at one rate and "
                    + "a viewer cannot detect the compression",
            });
        }

        var absolute = Path.GetFullPath(modelPath);
        var text = File.ReadAllText(absolute);
        var validation = ModelValidator.Validate(
            ModelJson.Parse(text), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0]);
        }

        var hash = ContentHash.OfText(text);
        var animation = spec.Animation.Compile();

        var provenance = new List<string>
        {
            $"einzel {EngineBuild.Version}, solver behaviour {EngineBuild.SolverBehaviourVersion}",
            $"model {Path.GetFileName(absolute)} hash {hash}",
            $"animation {animation.FramesPerSecond} fps over "
                + $"{TimeMapping.PlaybackSeconds(animation):G4} s of playback",
        };

        // A diffusive model has no trajectory and RND-8 forbids inventing one, so what
        // moves between its frames is the density. Running the transport is the command
        // layer's job - the renderer is handed the result, exactly as the section path
        // already does it - so the frames' instants become the run's snapshot list and
        // one run supplies the whole animation.
        IReadOnlyList<Transport.Diffusion.DensityField>? densities = null;

        if (string.Equals(
            validation.Model!.TransportMode, "diffusion", StringComparison.OrdinalIgnoreCase))
        {
            var instants = TimeMapping.Frames(animation)
                .Select(f => f.SimulatedSeconds)
                .ToList();

            var (built, fieldWarnings) = Fields.FieldAssembly.BuildReported(validation.Model!);

            var run = DiffusionRun.Execute(
                validation.Model!, built, fieldWarnings, snapshotSeconds: instants);

            if (run.Result.Snapshots.Count < instants.Count)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = "/animation/phases",
                    Constraint = $"the mapping runs to "
                        + $"{instants[^1] * 1e6:G6} us and the run reached "
                        + $"{run.Result.ElapsedSeconds * 1e6:G6} us, so "
                        + $"{instants.Count - run.Result.Snapshots.Count} frames have no density",
                    Suggestion = "shorten the last phase, or raise "
                        + "'transport.maximumFlightTime'. Repeating the last density for the "
                        + "frames past the end would show a packet sitting still rather than a "
                        + "run that finished",
                });
            }

            densities = [.. run.Result.Snapshots.Select(x => x.Density)];

            provenance.Add(
                $"density recorded at {densities.Count} instants over "
                + $"{run.Result.Steps} steps");
        }

        var frames = AnimationRenderer.Render(
            validation.Model!, spec, animation, provenance, densities);

        var extension = spec.Format == FigureFormat.Pdf ? ".pdf" : ".svg";
        var name = Path.GetFileNameWithoutExtension(absolute);

        var directory = outputDirectory is { } named
            ? Path.GetFullPath(named)
            : Path.Combine(project.Figures, name + ".animation");

        var artifacts = new List<string>(frames.Count + 1);

        foreach (var frame in frames)
        {
            artifacts.Add(Path.Combine(
                directory,
                $"frame-{frame.Frame.Index.ToString("D5", CultureInfo.InvariantCulture)}{extension}"));
        }

        var manifest = Path.Combine(directory, "frames.json");

        artifacts.Add(manifest);

        if (!dryRun)
        {
            Directory.CreateDirectory(directory);

            for (var i = 0; i < frames.Count; i++)
            {
                if (spec.Format == FigureFormat.Pdf)
                {
                    File.WriteAllBytes(artifacts[i], PdfWriter.Write(frames[i].Figure.Scene));
                }
                else
                {
                    File.WriteAllText(artifacts[i], SvgWriter.Write(frames[i].Figure.Scene));
                }
            }

            File.WriteAllText(manifest, CommandJson.Write(new AnimationManifest
            {
                Model = Path.GetFileName(absolute),
                ModelHash = hash,
                FramesPerSecond = animation.FramesPerSecond,
                PlaybackSeconds = TimeMapping.PlaybackSeconds(animation),
                Format = spec.Format == FigureFormat.Pdf ? "pdf" : "svg",
                Frames =
                [
                    .. frames.Select(f => new AnimationFrameJson
                    {
                        Index = f.Frame.Index,
                        File = Path.GetFileName(artifacts[f.Frame.Index]),
                        PlaybackSeconds = f.Frame.PlaybackSeconds,
                        SimulatedSeconds = f.Frame.SimulatedSeconds,
                        RateSiPerPlaybackSecond = f.Frame.RateSiPerPlaybackSecond,
                        Phase = f.Frame.PhaseLabel,
                    }),
                ],
            }));
        }

        var byLayer = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var drawn in frames[^1].Figure.Scene.Paths)
        {
            byLayer[drawn.Layer] = byLayer.GetValueOrDefault(drawn.Layer) + 1;
        }

        return new RenderOutcome
        {
            ModelPath = absolute,
            ModelHash = hash,
            Kind = "animation",
            Format = spec.Format == FigureFormat.Pdf ? "pdf" : "svg",
            Artifacts = artifacts,
            Written = !dryRun,
            Frames = frames.Count,
            PlaybackSeconds = TimeMapping.PlaybackSeconds(animation),
            PageMm = [frames[^1].Figure.Scene.WidthMm, frames[^1].Figure.Scene.HeightMm],
            Paths = byLayer,
            TextRuns = frames[^1].Figure.Scene.Texts.Count,
            DecimationToleranceMm = frames[^1].Figure.DecimationToleranceMm,
            TrajectoryPoints = frames[^1].Figure.TrajectoryPoints,
            TrajectoryPointsSampled = frames[^1].Figure.TrajectoryPointsBeforeDecimation,
            Warnings = [.. frames[^1].Figure.Warnings.Select(w => new WarningJson
            {
                Code = w.Code,
                Message = w.Message,
                Severity = w.Severity.ToString(),
                Suppressible = w.IsSuppressible,
            })],
        };
    }

    /// <summary>Reads a render spec from a file.</summary>
    /// <param name="specPath">Path to the spec.</param>
    /// <returns>The spec, and the model path it names, relative to the spec's folder.</returns>
    /// <exception cref="ArgumentException"><paramref name="specPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The spec does not name a model.</exception>
    /// <remarks>
    /// RND-2: the spec is text, lives beside the model, and is versioned with it, so
    /// the figure in a paper is regenerable from the repository rather than being a
    /// file someone once exported.
    /// </remarks>
    public static (RenderSpec Spec, string ModelPath) ReadSpec(string specPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specPath);

        var absolute = Path.GetFullPath(specPath);

        RenderSpec? spec;

        try
        {
            spec = CommandJson.Read<RenderSpec>(File.ReadAllText(absolute));
        }
        catch (System.Text.Json.JsonException malformed)
        {
            // AGT-3: a spec a person or an agent wrote wrongly is a validation
            // failure with a path and a correction, not an engine defect with a
            // stack trace. Reaching the catch-all in the CLI would have told the
            // reader to file a bug report about their own typo.
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = malformed.Path ?? "/",
                Constraint = malformed.Message,
                Suggestion = "'kind' is one of: section. 'format' is one of: svg, pdf. Every other "
                    + "field is a number, a string, or a boolean",
            });
        }

        if (spec is null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = "this file is not a render spec",
                Suggestion = "a render spec is a JSON object with at least a 'model' field naming "
                    + "the model to draw",
            });
        }

        if (string.IsNullOrWhiteSpace(spec.Model))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/model",
                Constraint = "a render spec must name the model it draws",
                Suggestion = "set 'model' to the model's path, relative to this spec's own folder",
            });
        }

        var folder = Path.GetDirectoryName(absolute) ?? ".";

        return (spec, Path.GetFullPath(Path.Combine(folder, spec.Model)));
    }
}
