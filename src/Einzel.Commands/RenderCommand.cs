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
        var validation = ModelValidator.Validate(ModelJson.Parse(text), null);

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
        // worth drawing, and the renderer says which of the two it got.
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

                density = DiffusionRun.Execute(validation.Model!, built, fieldWarnings).Result.Density;
            }
            catch (EinzelException)
            {
                density = null;
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
