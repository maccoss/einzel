using System.Text.Json.Serialization;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Project;
using Einzel.Transport;
using Einzel.Fields;
using Einzel.Analysis;
using Einzel.Core.Results;
using Einzel.Core.Units;
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

/// <summary>What a cloud of ions did, when a model launches one.</summary>
/// <remarks>
/// The Class S half of a result: transmission, acceptance, efficiency, each with a
/// stated interval. Absent when the model launches a single ion, because a
/// transmission of "one out of one" is not a statistic and reporting it as one
/// would be worse than saying nothing.
/// </remarks>
public sealed record EnsembleOutcome
{
    /// <summary>How many ions were launched.</summary>
    public required int Launched { get; init; }

    /// <summary>How many reached the detector.</summary>
    public required int Arrived { get; init; }

    /// <summary>Fraction arriving, with its binomial interval.</summary>
    public required MeasuredJson Transmission { get; init; }

    /// <summary>
    /// Where the ions that did not arrive went, by surface, largest first.
    /// </summary>
    /// <remarks>
    /// ACC-5 refuses a bare transmission figure and asks for it itemised by loss
    /// surface and mechanism. Empty when everything arrived, which is a statement
    /// rather than an omission.
    /// </remarks>
    public required IReadOnlyList<LossChannel> Losses { get; init; }

    /// <summary>
    /// The width enclosing the central half of the arrivals, in nanoseconds.
    /// </summary>
    /// <remarks>
    /// The model-free one, and the width the resolving power is computed from.
    /// Reported alongside <see cref="GaussianFwhmNs"/> rather than instead of it
    /// because the two disagree whenever the peak is not Gaussian, and a reader
    /// given only one of them beside a resolving power will try to reconcile the
    /// wrong pair.
    /// </remarks>
    public required double CentralWidthNs { get; init; }

    /// <summary>
    /// The Gaussian-equivalent full width at half maximum, in nanoseconds.
    /// </summary>
    /// <remarks>
    /// What the literature quotes, and what the turn-around closed form gives, so
    /// it is the one to compare against a published number. It exceeds the central
    /// width whenever the peak has a tail, which a second-order energy aberration
    /// always does.
    /// </remarks>
    public required double GaussianFwhmNs { get; init; }

    /// <summary>
    /// Asymmetry of the peak, which is why the two widths differ.
    /// </summary>
    /// <remarks>
    /// Zero for a symmetric peak. A mirror away from its focus produces a
    /// one-sided second-order tail, and the sign says which side.
    /// </remarks>
    public required double Skewness { get; init; }

    /// <summary>
    /// The part of the Gaussian width imposed before the ion left, by the source
    /// temperature. Zero for a cold cloud.
    /// </summary>
    /// <remarks>
    /// In the same convention as <see cref="GaussianFwhmNs"/> so the two are
    /// directly comparable: this is how much of that width the extraction is
    /// responsible for, and how much room there is to improve anything else.
    /// </remarks>
    public required double TurnAroundFwhmNs { get; init; }

    /// <summary>Arrival-time resolving power, model-free at half maximum.</summary>
    public required MeasuredJson ResolvingPower { get; init; }

    /// <summary>Ions in the physical packet, which is what pushes on itself.</summary>
    public required int Population { get; init; }

    /// <summary>
    /// The flight-time error the packet's own charge implies, as a fraction.
    /// </summary>
    /// <remarks>
    /// Reported whether or not it crosses a threshold, because a number that only
    /// appears when it is bad teaches nobody where the edge is. Zero for a single
    /// ion or a packet with no declared extent.
    /// </remarks>
    /// <remarks>
    /// Null when the packet has no beam energy for it to be a fraction of, which a
    /// packet still sitting in its trap does not.
    /// </remarks>
    public required double? SpaceChargeTimingFraction { get; init; }

    /// <summary>
    /// How many ions this packet could hold before space charge reaches the 1 ppm
    /// flight-time budget.
    /// </summary>
    public required double SpaceChargePopulationLimit { get; init; }

    /// <summary>
    /// Geometric emittance of the arriving packet in its wider transverse plane,
    /// in millimetre-milliradians.
    /// </summary>
    /// <remarks>
    /// The phase-space area the packet occupies, and what decides whether it fits
    /// through whatever comes next. Optics trade size against divergence and cannot
    /// reduce the product, so this is the number that says what no downstream lens
    /// can fix.
    /// </remarks>
    /// <remarks>
    /// Null when there was no packet to measure it on - fewer than three ions
    /// arrived. Null rather than zero, because a zero emittance is a real and
    /// meaningful answer (a perfectly parallel beam has one) and a reader cannot
    /// tell a measured zero from an absent measurement if both print as zero.
    /// </remarks>
    public required double? EmittanceMmMrad { get; init; }

    /// <summary>
    /// The same area in the narrower plane, in millimetre-milliradians.
    /// </summary>
    /// <remarks>
    /// Both planes, because a packet is rarely round and a single figure would
    /// average away the axis that is about to clip.
    /// </remarks>
    public required double? EmittanceMinorMmMrad { get; init; }

    /// <summary>
    /// Normalised emittance in the wider plane, in millimetre-milliradians.
    /// </summary>
    /// <remarks>
    /// Measured against transverse momentum rather than angle, so acceleration
    /// leaves it alone. A geometric emittance can be improved by nothing more than
    /// raising the beam energy; this one cannot, which makes it the fair way to
    /// compare two sources.
    /// </remarks>
    public required double? NormalisedEmittanceMmMrad { get; init; }

    /// <summary>
    /// Root-mean-square radius of the arriving packet in its wider plane, in
    /// millimetres.
    /// </summary>
    public required double? PacketRadiusMm { get; init; }

    /// <summary>
    /// The Twiss alpha of the arriving packet: positive while still converging,
    /// zero at a waist, negative once past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Says which side of the focus the detector sits on, which a width alone
    /// cannot. A packet measured at twice its waist size reads the same either way
    /// and needs the opposite correction.
    /// </para>
    /// <para>
    /// Null when the packet has no phase-space area to form an ellipse from - every
    /// ion exactly parallel, which a cloud with spatial spread and no temperature
    /// is. There is no orientation to report, and reporting one anyway is how a
    /// not-a-number reaches a serialiser that has no way to write it.
    /// </para>
    /// </remarks>
    public required double? PacketTwissAlpha { get; init; }
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

    /// <summary>What the cloud did, when the model launches one.</summary>
    public EnsembleOutcome? Ensemble { get; init; }
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
    /// <summary>Flies the declared cloud and reports what it did.</summary>
    private static EnsembleOutcome? Ensemble(CompiledModel model)
    {
        CloudFlight flight;

        try
        {
            flight = FiguresOfMerit.FlyCloud(model);
        }
        catch (ArgumentException)
        {
            // Fewer than two ions arrived. There is no peak to describe, and
            // inventing one from a single survivor is exactly the failure the
            // transmission figure exists to make visible.
            return null;
        }

        var peak = flight.Peak;

        // Three ions to place two second moments and their covariance. Below that
        // the area is not underdetermined so much as meaningless, and reporting a
        // zero would read as a perfectly collimated beam.
        var packet = flight.Arrived.Count >= 3
            ? Emittance.FromPacket(flight.Arrived)
            : ((Emittance Wider, Emittance Narrower)?)null;

        var turnAround = FiguresOfMerit.Evaluator("turnAroundTime")(model) ?? 0.0;

        var species = IonSpecies.FromModel(model);

        // The acceleration potential is a declaration of the flight energy, and for
        // a beam it is the right scale. A pulsed extraction trap declares none -
        // its packet starts at rest and the instrument does the accelerating - so
        // the scale has to be measured instead, from the energy the ions actually
        // arrived with. Measured only when nothing was declared, so no existing
        // result moves and the estimate stays independent of transmission losses
        // wherever a source states its own energy.
        var flightPotential = model.AccelerationPotentialSi != 0.0
            ? model.AccelerationPotentialSi
            : MeanArrivalPotential(flight.Arrived, species);

        var charge = SpaceCharge.Estimate(model.Cloud, species, flightPotential);

        var limit = charge.EffectiveRadiusM > 0.0 && flightPotential != 0.0
            ? SpaceCharge.PopulationLimit(
                Quantity.Si(charge.EffectiveRadiusM, Quantity.From(1.0, "m").Dimension),
                species,
                flightPotential,
                AccuracyBudget)
            : 0.0;

        var warnings = SpaceChargeWarnings(charge, limit);

        return new EnsembleOutcome
        {
            Launched = peak.Launched,
            Arrived = peak.Arrived,
            Transmission = MeasuredJson.From(Carry(peak.Transmission(), warnings), "1"),
            Losses = flight.Losses,
            CentralWidthNs = peak.CentralWidthSeconds(0.5) * 1e9,
            GaussianFwhmNs = peak.GaussianEquivalentFwhmSeconds * 1e9,
            Skewness = peak.Skewness,
            TurnAroundFwhmNs = turnAround * 1e9,
            ResolvingPower = MeasuredJson.From(Carry(peak.ResolvingPower(), warnings), "1"),
            Population = charge.Population,
            SpaceChargeTimingFraction = charge.TimingFraction,
            SpaceChargePopulationLimit = limit,

            // A micrometre is a millimetre-milliradian, since a radian is
            // dimensionless. The engine carries metres and the report carries the
            // unit the field is quoted in.
            EmittanceMmMrad = packet?.Wider.MillimetreMilliradian,
            EmittanceMinorMmMrad = packet?.Narrower.MillimetreMilliradian,
            NormalisedEmittanceMmMrad = packet?.Wider.NormalisedM * 1e6,
            PacketRadiusMm = packet?.Wider.RmsSizeM * 1e3,

            // A packet with no area has no ellipse and so no orientation. Left
            // null rather than passed through, because the alternative is a
            // not-a-number in a document format that cannot represent one.
            PacketTwissAlpha = packet is { Wider.GeometricM: > 0.0 } p ? p.Wider.TwissAlpha : null,
        };
    }

    /// <summary>
    /// The potential the arriving packet flew at, in volts, from its kinetic energy.
    /// </summary>
    /// <remarks>
    /// Zero when nothing arrived, which leaves the space-charge fractions
    /// unreported rather than divided by zero.
    /// </remarks>
    private static double MeanArrivalPotential(IReadOnlyList<PhaseState> arrived, IonSpecies species)
    {
        if (arrived.Count == 0)
        {
            return 0.0;
        }

        var sum = 0.0;

        foreach (var state in arrived)
        {
            sum += state.Velocity.LengthSquared;
        }

        // Mean kinetic energy over charge, which is the potential an equivalent
        // beam source would have declared.
        return 0.5 * species.MassSi * (sum / arrived.Count) / Math.Abs(species.ChargeSi);
    }

    /// <summary>The flight-time budget ACC-1 sets, as a fraction.</summary>
    private const double AccuracyBudget = 1e-6;

    /// <summary>
    /// Says so when the packet is dense enough that ignoring its own charge
    /// changes the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine flies every ion through a field that does not know about the
    /// others. For a sparse beam that is exactly right; for a dense packet it is
    /// wrong, and wrong invisibly - the answer looks the same. Spec section 7 asks
    /// for the governing dimensionless number to be computed and a
    /// non-suppressible warning raised when the model is outside its validity,
    /// and this is that.
    /// </para>
    /// <para>
    /// Two tiers, because they mean different things. Past the accuracy budget the
    /// flight time is wrong by more than it claims to be right by, which is a
    /// validity violation. Past a tenth of it the number still stands but the
    /// headroom is nearly gone, and someone about to raise the ion count should
    /// hear that before they do rather than after.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ValidityWarning> SpaceChargeWarnings(
        SpaceChargeEstimate charge, double limit)
    {
        if (charge.IsPointLike)
        {
            return
            [
                new ValidityWarning(
                    "spacecharge.point-packet",
                    $"{charge.Population} ions are declared at a single point: the cloud has no spatial "
                    + "extent, so its self-field is unbounded and no estimate of space charge is possible. "
                    + "Give the cloud a transverse or longitudinal spread, or set a population of 1",
                    WarningSeverity.ValidityViolation),
            ];
        }

        if (charge.TimingFraction is not > 0.0)
        {
            return [];
        }

        if (charge.TimingFraction > AccuracyBudget)
        {
            return
            [
                new ValidityWarning(
                    "spacecharge.ignored",
                    $"a packet of {charge.Population} ions in {charge.EffectiveRadiusM * 1e3:F3} mm carries "
                    + $"{charge.PotentialVolts * 1e3:F1} mV across itself, which is a flight-time error of "
                    + $"{charge.TimingFraction / 1e-6:F1} ppm against the 1 ppm budget. The ions here do not "
                    + $"push on each other: space charge is not modelled. This packet holds about {limit:N0} "
                    + "ions within budget, and the estimate is an upper bound - an instrument at a "
                    + "first-order energy focus suppresses it further",
                    WarningSeverity.ValidityViolation),
            ];
        }

        if (charge.TimingFraction > 0.1 * AccuracyBudget)
        {
            return
            [
                new ValidityWarning(
                    "spacecharge.approaching",
                    $"a packet of {charge.Population} ions implies a flight-time error of "
                    + $"{charge.TimingFraction / 1e-6:F2} ppm from its own charge, which is not modelled. "
                    + $"Still inside the 1 ppm budget, but this packet reaches it at about {limit:N0} ions",
                    WarningSeverity.Qualified),
            ];
        }

        return [];
    }

    /// <summary>Attaches warnings to a result, since GRD-2 makes them travel with it.</summary>
    private static Measured Carry(Measured measured, IReadOnlyList<ValidityWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            measured = measured.WithWarning(warning);
        }

        return measured;
    }

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

        // A model that launches a cloud gets the Class S half of a result too. The
        // single-ion flight time stays: it is the centre of the peak and it is the
        // number with a convergence study behind it, so removing it would trade a
        // measured uncertainty for a sampled one.
        if (model.Cloud.IsCloud)
        {
            run = run with { Ensemble = Ensemble(model) };
        }

        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");
        File.WriteAllText(resultPath, CommandJson.Write(run));

        return (run with { Artifacts = [.. artifacts, Path.GetRelativePath(project.Root, resultPath)] }, validation);
    }
}
