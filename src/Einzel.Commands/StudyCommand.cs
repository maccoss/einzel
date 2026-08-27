using System.Text.Json;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Project;
using Einzel.Sweeps;

namespace Einzel.Commands;

/// <summary>
/// How much one channel alone moves the figure of merit, in the figure's own unit.
/// </summary>
/// <param name="Parameter">The channel's parameter.</param>
/// <param name="Low">Figure at the low end of its range, or null when the ion did not arrive.</param>
/// <param name="High">Figure at the high end.</param>
/// <param name="Swing">
/// The larger absolute departure from nominal, which is the ranking quantity:
/// what is wanted is which parameter binds first, not which has the steepest slope
/// in some averaged sense.
/// </param>
/// <remarks>
/// The driver ranks in SI, because ranking needs one scale and not a unit. This is
/// the same numbers at the boundary, converted into the unit the figure is
/// reported in - a swing printed as 2.0361E-08 beside a nominal in microseconds is
/// a bare number of the exact kind GRD-1 exists to prevent.
/// </remarks>
public sealed record SweepSensitivity(string Parameter, double? Low, double? High, double Swing);

/// <summary>What a tolerance sweep found.</summary>
public sealed record SweepOutcome
{
    /// <summary>The study file, as an absolute path.</summary>
    public required string StudyPath { get; init; }

    /// <summary>The model that was perturbed, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Which figure of merit was recorded, and its unit.</summary>
    public required FigureOfMeritInfo FigureOfMerit { get; init; }

    /// <summary>The figure at unperturbed parameters, in the figure's unit.</summary>
    public required double Nominal { get; init; }

    /// <summary>How many draws produced a figure.</summary>
    public required int Succeeded { get; init; }

    /// <summary>How many draws were taken.</summary>
    public required int Draws { get; init; }

    /// <summary>The distribution across the draws, as a GRD-1 envelope.</summary>
    public MeasuredJson? Distribution { get; init; }

    /// <summary>
    /// One-at-a-time attribution, largest swing first.
    /// </summary>
    /// <remarks>
    /// Section 13 calls this "the actual deliverable, since what is wanted is not
    /// only whether 100 to 300 microns suffices but which parameter binds first".
    /// </remarks>
    public required IReadOnlyList<SweepSensitivity> Sensitivity { get; init; }

    /// <summary>
    /// What the draws earned, distinct by code and counted.
    /// </summary>
    /// <remarks>
    /// GRD-2, at the seam a sweep used to lose it: the driver ranks by a bare
    /// double, so a field that missed its tolerance or a mode outside its validity
    /// left no trace on the study. These are the evaluations' warnings, which are
    /// not the same as <see cref="Distribution"/>'s - those are the distribution's
    /// own.
    /// </remarks>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];

    /// <summary>Files written, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }
}

/// <summary>What an optimisation found.</summary>
public sealed record OptimiseOutcome
{
    /// <summary>The study file, as an absolute path.</summary>
    public required string StudyPath { get; init; }

    /// <summary>The model that was searched, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Which figure of merit was optimised, and its unit.</summary>
    public required FigureOfMeritInfo FigureOfMerit { get; init; }

    /// <summary>Which search ran.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Which way was better.</summary>
    public required string Sense { get; init; }

    /// <summary>The optimum, one envelope per variable.</summary>
    public required IReadOnlyDictionary<string, MeasuredJson> Best { get; init; }

    /// <summary>The figure at the optimum, as a GRD-1 envelope.</summary>
    public required MeasuredJson Objective { get; init; }

    /// <summary>Objective evaluations spent.</summary>
    public required int Evaluations { get; init; }

    /// <summary>Evaluations that produced no figure of merit.</summary>
    public required int Failures { get; init; }

    /// <summary>Whether the search met its tolerance rather than its budget.</summary>
    public required bool Converged { get; init; }

    /// <summary>Every improvement on the best-so-far, in order.</summary>
    public required IReadOnlyList<OptimisationStep> History { get; init; }

    /// <summary>
    /// What the objective evaluations earned, distinct by code and counted.
    /// </summary>
    /// <remarks>
    /// GRD-2 at the same seam a sweep loses it, and it matters more here: an
    /// optimiser walks towards whatever scores best, so a corner of the box where
    /// the solve stops converging is somewhere it will actively go.
    /// </remarks>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];

    /// <summary>Files written, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }
}

/// <summary>
/// Runs a study file: a tolerance sweep or an optimisation.
/// </summary>
/// <remarks>
/// <para>
/// The drivers in Einzel.Sweeps take a function from a validated model to a
/// number, which is what keeps them device-agnostic. A file cannot carry a
/// function, so a study names one out of <see cref="FiguresOfMerit"/> - and that
/// registry is the seam section 12's Python objectives will register into without
/// the drivers changing.
/// </para>
/// <para>
/// Both write their result beside the study as JSON. A sweep that has to be re-run
/// to be read is a sweep nobody reads twice.
/// </para>
/// </remarks>
public static class StudyCommand
{
    /// <summary>
    /// Describes a figure of merit, whether the engine computes it or an extension
    /// does.
    /// </summary>
    /// <remarks>
    /// An extension objective has no unit the engine can know, so it is reported
    /// dimensionless and under its own name. That is honest rather than lazy: the
    /// extension returns a bare number, and inventing a unit for it here would be
    /// the platform asserting something only the extension author knows.
    /// </remarks>
    private static FigureOfMeritInfo Figure(string name) =>
        ExtensionObjective.Names(name)
            ? new FigureOfMeritInfo(
                name,
                "1",
                $"Computed by extension '{name[ExtensionObjective.Prefix.Length..]}', not by the engine.",
                false)
            : FiguresOfMerit.Describe(name);

    /// <summary>Builds the evaluator a sweep or an optimiser drives.</summary>
    private static Func<CompiledModel, double?> Evaluate(
        string name,
        ProjectLayout project,
        double energySpread,
        int ions,
        WarningLedger ledger,
        out ExtensionObjective.Provenance? provenance)
    {
        Func<CompiledModel, double?> inner;

        if (!ExtensionObjective.Names(name))
        {
            provenance = null;
            inner = FiguresOfMerit.Evaluator(name, energySpread, ions, ledger.Add);
        }
        else
        {
            inner = ExtensionObjective.Evaluator(
                name, project.Extensions, Path.Combine(project.Scratch, "ext"),
                out var used, energySpread, ions);

            provenance = used;
        }

        // One evaluation is one draw however many warnings it emitted, and the
        // count is closed even when the evaluation throws - a study that dies
        // half way still has to say what it saw on the way.
        return model =>
        {
            try
            {
                return inner(model);
            }
            finally
            {
                ledger.EndEvaluation();
            }
        };
    }

    /// <summary>
    /// Writes the manifest a study result references.
    /// </summary>
    /// <remarks>
    /// GRD-7: every result references a manifest. Studies wrote results and no
    /// manifest at all until this, which is the requirement missing rather than
    /// merely thin - a sweep is exactly the operation whose thousand draws are
    /// worth being able to regenerate, and nothing recorded what produced them.
    /// </remarks>
    private static string WriteManifest(
        ProjectLayout project,
        string studyPath,
        string kind,
        string modelText,
        string schemaVersion,
        string transportMode,
        long seed,
        ExtensionObjective.Provenance? extension,
        DateTimeOffset timestampUtc)
    {
        var manifest = new RunManifest
        {
            ModelHash = ContentHash.OfText(modelText),
            SchemaVersion = schemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            TransportMode = transportMode,
            ComputePath = EngineBuild.ComputePath,
            Seeds = [seed],
            Extensions = extension is null ? [] : [extension.Identity],
            Interpreter = extension?.Interpreter,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(studyPath);
        var path = Path.Combine(project.Results, $"{stem}.{kind}.manifest.json");

        File.WriteAllText(path, manifest.ToJson());

        return Path.GetRelativePath(project.Root, path);
    }

    private static readonly JsonSerializerOptions Reading = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a study file.</summary>
    /// <param name="studyPath">Path to the study.</param>
    /// <returns>The study, and the model it names, as absolute paths.</returns>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The file is not a study, or names no model.</exception>
    public static (StudyDocument Study, string ModelPath, string StudyPath) Load(string studyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studyPath);

        var absolute = Path.GetFullPath(studyPath);
        StudyDocument? study;

        try
        {
            study = JsonSerializer.Deserialize<StudyDocument>(File.ReadAllText(absolute), Reading);
        }
        catch (JsonException failure)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"{absolute} is not valid JSON: {failure.Message}",
                Suggestion = "a study is a JSON document; run 'einzel schema --study' for its shape",
            });
        }

        if (study is null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"{absolute} is empty",
                Suggestion = "run 'einzel schema --study' for the shape of a study file",
            });
        }

        if (string.IsNullOrWhiteSpace(study.Model))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/model",
                Constraint = "a study names the model it studies",
                Suggestion = "add \"model\": \"../models/yours.json\", relative to this study file",
            });
        }

        // Relative to the study, not to the working directory: a study travels
        // with the project and should mean the same thing from anywhere in it.
        var modelPath = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(absolute) ?? ".", study.Model));

        return (study, modelPath, absolute);
    }

    /// <summary>Runs a tolerance sweep.</summary>
    /// <param name="studyPath">Path to the study file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be done and compute nothing (CLI-4).</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The study or the model does not validate.</exception>
    public static SweepOutcome Sweep(string studyPath, ProjectLayout project, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (study, modelPath, absolute) = Load(studyPath);
        var figure = Figure(Required(study.FigureOfMerit));
        var channels = StudyBinding.Channels(study);
        var document = ModelJson.Parse(File.ReadAllText(modelPath));

        if (dryRun)
        {
            return new SweepOutcome
            {
                StudyPath = absolute,
                ModelPath = modelPath,
                FigureOfMerit = figure,
                Nominal = double.NaN,
                Succeeded = 0,
                Draws = study.Draws,
                Sensitivity = [],
                Artifacts = [],
            };
        }

        var ledger = new WarningLedger();

        var evaluate = Evaluate(
            figure.Name, project, study.EnergySpread, study.Ions, ledger, out var extension);

        var result = ToleranceStudy.Run(
            document, channels, evaluate, study.Draws, study.Seed, study.OneAtATime, figure.Dimension);

        // One conversion from SI into the figure's unit, applied to everything the
        // sweep reports, so nothing leaves here as a bare number under a label it
        // does not match.
        var scale = 1.0 / Core.Units.Quantity.From(1.0, figure.Unit).SiValue;

        var outcome = new SweepOutcome
        {
            StudyPath = absolute,
            ModelPath = modelPath,
            FigureOfMerit = figure,
            Nominal = result.Nominal * scale,
            Succeeded = result.Succeeded,
            Draws = result.Draws.Count,
            Distribution = result.Distribution is { } distribution
                ? MeasuredJson.From(distribution, figure.Unit)
                : null,
            Sensitivity = [.. result.Sensitivity.Select(c => new SweepSensitivity(
                c.Parameter, c.Low * scale, c.High * scale, c.Swing * scale))],
            Warnings = ledger.Collected,
            Artifacts = [],
        };

        return outcome with
        {
            Artifacts =
            [
                Write(project, absolute, "sweep", outcome),
                WriteManifest(
                    project, absolute, "sweep", File.ReadAllText(modelPath), document.SchemaVersion,
                    document.Transport?.Mode ?? "trajectory", study.Seed, extension,
                    DateTimeOffset.UtcNow),
            ],
        };
    }

    /// <summary>Runs an optimisation.</summary>
    /// <param name="studyPath">Path to the study file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be done and compute nothing (CLI-4).</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="studyPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The study or the model does not validate.</exception>
    public static OptimiseOutcome Optimise(string studyPath, ProjectLayout project, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(project);

        var (study, modelPath, absolute) = Load(studyPath);
        var figure = Figure(Required(study.FigureOfMerit));
        var variables = StudyBinding.Variables(study);
        var algorithm = StudyBinding.Algorithm(study);
        var sense = StudyBinding.Sense(study, figure);
        var document = ModelJson.Parse(File.ReadAllText(modelPath));

        if (dryRun)
        {
            return new OptimiseOutcome
            {
                StudyPath = absolute,
                ModelPath = modelPath,
                FigureOfMerit = figure,
                Algorithm = algorithm.ToString(),
                Sense = sense.ToString(),
                Best = new Dictionary<string, MeasuredJson>(StringComparer.Ordinal),
                Objective = MeasuredJson.From(Empty(figure), figure.Unit),
                Evaluations = 0,
                Failures = 0,
                Converged = false,
                History = [],
                Artifacts = [],
            };
        }

        var ledger = new WarningLedger();

        var result = Optimiser.Run(
            document,
            variables,
            Evaluate(figure.Name, project, study.EnergySpread, study.Ions, ledger, out var extension),
            sense,
            algorithm,
            new OptimisationSettings
            {
                MaximumEvaluations = study.MaximumEvaluations,
                ParameterTolerance = study.ParameterTolerance,
                ObjectiveTolerance = study.ObjectiveTolerance,
                ObjectiveDimension = figure.Dimension,
                Seed = study.Seed,
            });

        var best = new Dictionary<string, MeasuredJson>(StringComparer.Ordinal);

        foreach (var (name, measured) in result.Best.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // Reported in the parameter's own dimension. A length that came back
            // in metres when the model was written in millimetres is technically
            // correct and practically useless.
            best[name] = MeasuredJson.From(measured, UnitFor(measured));
        }

        var outcome = new OptimiseOutcome
        {
            StudyPath = absolute,
            ModelPath = modelPath,
            FigureOfMerit = figure,
            Algorithm = algorithm.ToString(),
            Sense = sense.ToString(),
            Best = best,
            Objective = MeasuredJson.From(result.Objective, figure.Unit),
            Evaluations = result.Evaluations,
            Failures = result.Failures,
            Converged = result.Converged,
            History = result.History,
            Warnings = ledger.Collected,
            Artifacts = [],
        };

        return outcome with
        {
            Artifacts =
            [
                Write(project, absolute, "optimise", outcome),
                WriteManifest(
                    project, absolute, "optimise", File.ReadAllText(modelPath), document.SchemaVersion,
                    document.Transport?.Mode ?? "trajectory", study.Seed, extension,
                    DateTimeOffset.UtcNow),
            ],
        };
    }

    /// <summary>An empty envelope, for a dry run that computed nothing.</summary>
    private static Core.Results.Measured Empty(FigureOfMeritInfo figure)
    {
        var zero = Core.Units.Quantity.From(0.0, figure.Unit);

        return new Core.Results.Measured(
            zero,
            Core.Results.UncertaintyInterval.Symmetric(zero, zero, 1.0),
            new Core.Results.Evidence.Search(0, Converged: false, SpreadSi: 0.0),
            [
                new Core.Results.ValidityWarning(
                    "study.dry-run",
                    "nothing was computed: this is what the study would do, not what it found",
                    Core.Results.WarningSeverity.Qualified),
            ]);
    }

    /// <summary>A display unit for a parameter's dimension.</summary>
    /// <remarks>
    /// Millimetres for a length and volts for a potential, because that is what
    /// the models are written in; anything else falls back to coherent SI, which
    /// the envelope can always render.
    /// </remarks>
    private static string UnitFor(Core.Results.Measured measured)
    {
        if (measured.Dimension == Core.Units.Quantity.From(1.0, "mm").Dimension)
        {
            return "mm";
        }

        if (measured.Dimension == Core.Units.Quantity.From(1.0, "V").Dimension)
        {
            return "V";
        }

        return "1";
    }

    private static string Required(string? figureOfMerit)
    {
        if (!string.IsNullOrWhiteSpace(figureOfMerit))
        {
            return figureOfMerit;
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/figureOfMerit",
            Constraint = "a study says which figure of merit it records",
            Suggestion = $"one of: {string.Join(", ", FiguresOfMerit.All.Select(f => f.Name))}",
        });
    }

    private static string Write<T>(ProjectLayout project, string studyPath, string kind, T outcome)
    {
        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(studyPath);
        var path = Path.Combine(project.Results, $"{stem}.{kind}.json");
        File.WriteAllText(path, CommandJson.Write(outcome));

        return Path.GetRelativePath(project.Root, path);
    }
}
