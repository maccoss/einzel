using System.Text.Json.Serialization;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Project;
using Einzel.Transport;
using Einzel.Fields;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>The outcome of validating a model.</summary>
public sealed record ValidateOutcome
{
    /// <summary>Whether the document validated.</summary>
    public required bool Valid { get; init; }

    /// <summary>The model file, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Content hash of the model document.</summary>
    public required string ModelHash { get; init; }

    /// <summary>The declared schema version.</summary>
    public string? SchemaVersion { get; init; }

    /// <summary>Every error found, in document order.</summary>
    public required IReadOnlyList<EinzelError> Errors { get; init; }

    /// <summary>The exit code this outcome maps to (CLI-3).</summary>
    [JsonIgnore]
    public ExitCode ExitCode => Valid
        ? ExitCode.Success
        : Errors.Any(e => e.Code == ErrorCodes.RegimeInvalid)
            ? ExitCode.RegimeViolation
            : ExitCode.ValidationFailure;
}

/// <summary>The outcome of a run.</summary>
public sealed record RunOutcome
{
    /// <summary>The manifest that determines this run (PRJ-3).</summary>
    public required RunManifest Manifest { get; init; }

    /// <summary>Flight time, as the GRD-1 envelope.</summary>
    public required MeasuredJson FlightTime { get; init; }

    /// <summary>Why the integration stopped.</summary>
    public required string Outcome { get; init; }

    /// <summary>Where the ion ended, in millimetres.</summary>
    public required IReadOnlyList<double> FinalPositionMm { get; init; }

    /// <summary>Largest relative departure of total energy over the flight (ACC-4).</summary>
    public required double MaximumRelativeEnergyDrift { get; init; }

    /// <summary>Accepted integrator steps, finest tolerance.</summary>
    public required int AcceptedSteps { get; init; }

    /// <summary>Distance advanced analytically through field-free regions, in metres.</summary>
    public required double AnalyticDriftDistanceM { get; init; }

    /// <summary>Files written by this run, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }
}

/// <summary>
/// Validates a model, and runs one.
/// </summary>
/// <remarks>
/// <para>
/// AGT-2: "Every capability reachable from the window is reachable from the CLI
/// and from MCP, through the same command objects." These are those objects. The
/// CLI is a thin argument parser over them; the MCP server and the shell will be
/// too. Nothing here reads the console or writes to it.
/// </para>
/// <para>
/// The result of a run is a <see cref="MeasuredJson"/>, never a bare flight time.
/// The integrator's own <c>TrajectoryResult</c> stops at the Einzel.Transport
/// boundary, and what crosses into a reportable result comes from
/// <see cref="FlightTimeStudy"/> with its convergence evidence attached.
/// </para>
/// </remarks>
public static class RunCommand
{
    /// <summary>Validates a model document on disk.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <returns>The validation outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="FileNotFoundException">The model file does not exist.</exception>
    public static ValidateOutcome Validate(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var full = Path.GetFullPath(modelPath);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"model file not found: {full}", full);
        }

        var text = File.ReadAllText(full);
        var hash = ContentHash.OfText(text);

        ModelDocument document;

        try
        {
            document = ModelJson.Parse(text);
        }
        catch (EinzelException failure)
        {
            return new ValidateOutcome
            {
                Valid = false,
                ModelPath = full,
                ModelHash = hash,
                Errors = [failure.Error],
            };
        }

        var validation = ModelValidator.Validate(document);

        return new ValidateOutcome
        {
            Valid = validation.IsValid,
            ModelPath = full,
            ModelHash = hash,
            SchemaVersion = document.SchemaVersion,
            Errors = validation.Errors,
        };
    }

    /// <summary>Runs a model, writing a manifest, a result, and optionally a VTU trajectory.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <param name="project">The project the outputs belong to.</param>
    /// <param name="exportVtu">Whether to write the trajectory for ParaView.</param>
    /// <param name="timestampUtc">The run timestamp, supplied so the caller owns the clock.</param>
    /// <returns>The run outcome, or the validation failure that prevented it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    public static (RunOutcome? Run, ValidateOutcome Validation) Execute(
        string modelPath,
        ProjectLayout project,
        bool exportVtu,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(project);

        var validation = Validate(modelPath);

        if (!validation.Valid)
        {
            return (null, validation);
        }

        var document = ModelJson.Parse(File.ReadAllText(validation.ModelPath));
        var model = ModelValidator.Validate(document).Model!;

        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;
        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        // The reportable number comes from the convergence study, not from a
        // single integration: one run has no honest uncertainty to quote.
        var study = FlightTimeStudy.Run(launch, species, field, settings, detector);
        var finest = study.Runs[^1];

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,
            SchemaVersion = document.SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            TransportMode = model.TransportMode,
            ComputePath = EngineBuild.ComputePath,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);
        var artifacts = new List<string>();

        Directory.CreateDirectory(project.Results);
        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");
        File.WriteAllText(manifestPath, manifest.ToJson());
        artifacts.Add(Path.GetRelativePath(project.Root, manifestPath));

        if (exportVtu)
        {
            var recorder = new TrajectoryRecorder(model.SampleIntervalSi);

            TrajectoryIntegrator.Integrate(
                launch, species, field,
                settings with { RelativeTolerance = model.RelativeTolerance },
                detector, recorder);

            if (recorder.Samples.Count >= 2)
            {
                Directory.CreateDirectory(project.Scratch);
                var vtuPath = Path.Combine(project.Scratch, $"{stem}.trajectory.vtu");

                File.WriteAllText(vtuPath, VtuWriter.WriteTrajectory(
                    recorder.Samples,
                    [
                        $"engine: {EngineBuild.Version}",
                        $"model: {validation.ModelHash}",
                        $"samples: {recorder.Samples.Count} at {model.SampleIntervalSi:G6} s nominal interval",
                        recorder.Truncated
                            ? "TRUNCATED: sample capacity reached; the tail of this trajectory is missing"
                            : "complete",
                    ]));

                artifacts.Add(Path.GetRelativePath(project.Root, vtuPath));
            }
        }

        var run = new RunOutcome
        {
            Manifest = manifest,
            FlightTime = MeasuredJson.From(study.FlightTime, "us"),
            Outcome = finest.Outcome.ToString(),
            FinalPositionMm =
            [
                finest.FinalState.Position.X * 1e3,
                finest.FinalState.Position.Y * 1e3,
                finest.FinalState.Position.Z * 1e3,
            ],
            MaximumRelativeEnergyDrift = finest.MaximumRelativeEnergyDrift,
            AcceptedSteps = finest.AcceptedSteps,
            AnalyticDriftDistanceM = finest.AnalyticDriftDistance,
            Artifacts = artifacts,
        };

        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");
        File.WriteAllText(resultPath, CommandJson.Write(run));

        return (run with { Artifacts = [.. artifacts, Path.GetRelativePath(project.Root, resultPath)] }, validation);
    }
}
