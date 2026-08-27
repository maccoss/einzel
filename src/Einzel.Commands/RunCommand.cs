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
    /// <summary>Collisions the whole ensemble made. Zero in vacuum.</summary>
    public int Collisions { get; init; }

    /// <summary>
    /// How many ions collided at least once and still reached the detector or a
    /// surface.
    /// </summary>
    /// <remarks>
    /// COL-1 keeps a scattered ion that stays within acceptance rather than
    /// discarding it as a loss, so this count is the difference between a peak with
    /// a pedestal and one without - and neither is visible from a transmission
    /// figure, which counts both as arrivals.
    /// </remarks>
    public int ScatteredIons { get; init; }

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

/// <summary>
/// The governing dimensionless numbers of a run, on the wire.
/// </summary>
/// <remarks>
/// REG-2 requires these computed rather than assumed. Reported whether or not any
/// of them crosses a threshold, because a reader who can see a Knudsen number of
/// 40 knows something a reader who sees no warning does not: that the run was
/// checked and found comfortable, rather than not checked at all.
/// </remarks>
public sealed record RegimeJson
{
    /// <summary>Gas pressure, in millibar.</summary>
    public required double PressureMbar { get; init; }

    /// <summary>Which collision description was used.</summary>
    public required string CollisionModel { get; init; }

    /// <summary>Ion mean free path, in millimetres.</summary>
    public required double MeanFreePathMm { get; init; }

    /// <summary>The length the Knudsen number was taken against, in millimetres.</summary>
    public required double ApertureMm { get; init; }

    /// <summary>Mean free path over that length. Below 1 the gas is a continuum.</summary>
    public required double Knudsen { get; init; }

    /// <summary>Expected collisions over the flight.</summary>
    public required double CollisionsPerFlight { get; init; }

    /// <summary>Expected collisions per drive cycle, absent when undriven.</summary>
    public double? CollisionsPerRfCycle { get; init; }
}

/// <summary>What a diffusive run produced, on the wire.</summary>
/// <remarks>
/// TRN-2: this mode emits a time-resolved density rather than trajectories, so a
/// result has no flight time and no final position. What it has instead is where
/// the ions went and when they got there, which is what a Class S figure is made
/// of - and reporting an invented flight time here would be reporting a quantity
/// the model never computed.
/// </remarks>
public sealed record DiffusionJson
{
    /// <summary>Ions in the initial density.</summary>
    public required double Launched { get; init; }

    /// <summary>Ions that reached the collecting boundary.</summary>
    public required double Collected { get; init; }

    /// <summary>Ions still in the domain when the run ended.</summary>
    public required double Remaining { get; init; }

    /// <summary>Where the rest went, by boundary, largest first (ACC-5).</summary>
    public required IReadOnlyList<LossChannel> Losses { get; init; }

    /// <summary>Fraction collected, of those that left.</summary>
    public required double Transmission { get; init; }

    /// <summary>Mean time to reach the collector, in microseconds.</summary>
    public double? MeanTransitUs { get; init; }

    /// <summary>Spread of transit times, in microseconds.</summary>
    public double? TransitSpreadUs { get; init; }

    /// <summary>Mobility used, in square metres per volt-second.</summary>
    public required double MobilitySi { get; init; }

    /// <summary>Whether that mobility was derived rather than declared (TRN-1).</summary>
    public required bool MobilityDerived { get; init; }

    /// <summary>Grid the density was tracked on, columns then rows.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>Time steps taken.</summary>
    public required int Steps { get; init; }

    /// <summary>Simulated time, in microseconds.</summary>
    public required double ElapsedUs { get; init; }

    /// <summary>Final density spread, x then y, in millimetres.</summary>
    /// <remarks>
    /// What replaces a packet size when there are no particles to take a variance
    /// over. For a funnel the y figure is the radial compression the device exists
    /// to produce.
    /// </remarks>
    public required IReadOnlyList<double> SpreadMm { get; init; }
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

    /// <summary>
    /// The dimensionless numbers that say whether this mode applies, or null in
    /// vacuum where the question does not arise.
    /// </summary>
    public RegimeJson? Regime { get; init; }

    /// <summary>What a diffusive run produced, or null for a trajectory run.</summary>
    public DiffusionJson? Diffusion { get; init; }

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
    private static EnsembleOutcome? Ensemble(
        CompiledModel model, IReadOnlyList<ValidityWarning> fieldWarnings)
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

        // The flight time matters to the estimate, because the dominant mechanism
        // is the packet expanding under its own charge and that goes on for as long
        // as the flight does. The peak's own position is the packet's flight time;
        // a packet that never arrived has none, and the estimate falls back to its
        // asymptotic bound and says so.
        var flightTime = peak.Arrived > 0 ? peak.MeanSeconds : 0.0;

        var charge = SpaceCharge.Estimate(model.Cloud, species, flightPotential, flightTime);

        var limit = charge.EffectiveRadiusM > 0.0 && flightPotential != 0.0
            ? SpaceCharge.PopulationLimit(
                Quantity.Si(charge.EffectiveRadiusM, Quantity.From(1.0, "m").Dimension),
                species,
                flightPotential,
                AccuracyBudget,
                flightTime)
            : 0.0;

        var warnings = (IReadOnlyList<ValidityWarning>)[.. fieldWarnings, .. SpaceChargeWarnings(charge, limit)];

        return new EnsembleOutcome
        {
            Launched = peak.Launched,
            Arrived = peak.Arrived,
            Collisions = flight.Collisions,
            ScatteredIons = flight.ScatteredIons,
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
                    + $"push on each other: this run does not model space charge. This packet holds about "
                    + $"{Population(limit)} within budget, and the estimate is an upper bound - a mirror at a "
                    + "first-order energy focus measurably suppresses it, because the push correlates position "
                    + "with energy in the sign the mirror corrects",
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
                    + $"{charge.TimingFraction / 1e-6:F2} ppm from its own charge, which this run does not "
                    + $"model. Still inside the 1 ppm budget, but this packet reaches it at about "
                    + $"{Population(limit)}",
                    WarningSeverity.Qualified),
            ];
        }

        return [];
    }

    /// <summary>A population limit, rendered so a limit below one ion does not print as none.</summary>
    /// <remarks>
    /// The corrected estimate puts a dense half-millimetre packet's 1 ppm capacity
    /// in the single figures, and "holds about 0 ions" reads as a defect in the
    /// message rather than as the finding it is.
    /// </remarks>
    private static string Population(double limit) => limit switch
    {
        < 1.0 => $"{limit:F2} ions",
        < 10.0 => $"{limit:F1} ions",
        _ => $"{limit:N0} ions",
    };

    /// <summary>Runs a model in the diffusive mode and writes its result.</summary>
    private static RunOutcome Diffusive(
        CompiledModel model,
        Fields.IElectrostaticField field,
        IReadOnlyList<ValidityWarning> fieldWarnings,
        ValidateOutcome validation,
        ProjectLayout project,
        DateTimeOffset timestampUtc)
    {
        var outcome = DiffusionRun.Execute(model, field, fieldWarnings);
        var result = outcome.Result;

        var left = result.Collected + result.Lost.Values.Sum();

        // Transit time from the arrivals series, which is what a density has instead
        // of a list of arrival times. Weighted by how many ions arrived in each bin.
        double? mean = null;
        double? spread = null;

        if (result.Arrivals.Count > 0 && result.Collected > 0.0)
        {
            var weighted = result.Arrivals.Sum(a => a.TimeSeconds * a.Ions) / result.Collected;

            var variance = result.Arrivals.Sum(
                a => a.Ions * (a.TimeSeconds - weighted) * (a.TimeSeconds - weighted)) / result.Collected;

            mean = weighted * 1e6;
            spread = Math.Sqrt(Math.Max(0.0, variance)) * 1e6;
        }

        var (spreadX, spreadY) = result.Density.Spread();

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,
            SchemaVersion = ModelJson.Parse(File.ReadAllText(validation.ModelPath)).SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            TransportMode = model.TransportMode,
            ComputePath = EngineBuild.ComputePath,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);
        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");

        File.WriteAllText(manifestPath, manifest.ToJson());

        var gas = Transport.Collisions.BackgroundGas.FromModel(model.Gas);

        var regime = Transport.Collisions.RegimeDiagnostics.Measure(
            gas, IonSpecies.FromModel(model), 1.0, result.ElapsedSeconds, SmallestAperture(model));

        var run = new RunOutcome
        {
            Manifest = manifest,

            // No flight time: a density does not have one. The transit time is in the
            // diffusion block, where it is a distribution rather than a number.
            FlightTime = MeasuredJson.From(
                Carry(
                    new Measured(
                        Quantity.Si(double.NaN, Dimension.TimeDimension),
                        UncertaintyInterval.Symmetric(
                            Quantity.Si(double.NaN, Dimension.TimeDimension),
                            Quantity.Si(0.0, Dimension.TimeDimension),
                            1.0),
                        new Evidence.Convergence("diffusive transport", double.NaN, 0.0, double.NaN),
                        [
                            new ValidityWarning(
                                "transport.no-flight-time",
                                "this run computed a density, not trajectories, so there is no "
                                + "flight time. The transit-time distribution is reported under "
                                + "'diffusion' instead",
                                WarningSeverity.Provenance),
                        ]),
                    outcome.Warnings),
                "us"),

            Outcome = "DensityEvolved",
            FinalPositionMm = [],
            MaximumRelativeEnergyDrift = double.NaN,
            AcceptedSteps = result.Steps,
            AnalyticDriftDistanceM = 0.0,

            Regime = gas.IsPresent
                ? new RegimeJson
                {
                    PressureMbar = regime.PressureMbar,
                    CollisionModel = model.Gas.Model,
                    MeanFreePathMm = regime.MeanFreePathM * 1e3,
                    ApertureMm = regime.ApertureM * 1e3,
                    Knudsen = regime.Knudsen,
                    CollisionsPerFlight = regime.CollisionsPerFlight,
                    CollisionsPerRfCycle = regime.CollisionsPerRfCycle,
                }
                : null,

            Diffusion = new DiffusionJson
            {
                Launched = outcome.Launched,
                Collected = result.Collected,
                Remaining = result.Remaining,
                Losses =
                [
                    .. result.Lost
                        .OrderByDescending(p => p.Value)
                        .ThenBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => new LossChannel(p.Key, (int)Math.Round(p.Value))),
                ],
                Transmission = left > 0.0 ? result.Collected / left : 0.0,
                MeanTransitUs = mean,
                TransitSpreadUs = spread,
                MobilitySi = outcome.Mobility.ZeroFieldSi,
                MobilityDerived = outcome.Mobility.Derived,
                Nodes = [outcome.Grid.CountX, outcome.Grid.CountY],
                Steps = result.Steps,
                ElapsedUs = result.ElapsedSeconds * 1e6,
                SpreadMm = [spreadX * 1e3, spreadY * 1e3],
            },

            Artifacts = [Path.GetRelativePath(project.Root, manifestPath)],
        };

        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");
        File.WriteAllText(resultPath, CommandJson.Write(run));

        return run with
        {
            Artifacts = [.. run.Artifacts, Path.GetRelativePath(project.Root, resultPath)],
        };
    }

    /// <summary>
    /// The tightest constriction an ion must pass, in metres.
    /// </summary>
    /// <remarks>
    /// What the Knudsen number is taken against. The honest choice is the narrowest
    /// thing in the model rather than the size of the whole instrument: a gas is a
    /// continuum on the scale of an aperture long before it is one on the scale of a
    /// flight tube, and the aperture is where that matters.
    ///
    /// Falls back to the source-to-detector distance where no geometry declares a
    /// smaller feature, which is the largest defensible length and so the most
    /// conservative Knudsen number.
    /// </remarks>
    private static double SmallestAperture(CompiledModel model)
    {
        var smallest = double.PositiveInfinity;

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } flat)
            {
                smallest = Math.Min(smallest, Math.Min(flat.MaxX - flat.MinX, flat.MaxY - flat.MinY));
            }

            if (element.Solve3D is { } volume)
            {
                smallest = Math.Min(
                    smallest,
                    Math.Min(
                        volume.MaxX - volume.MinX,
                        Math.Min(volume.MaxY - volume.MinY, volume.MaxZ - volume.MinZ)));
            }
        }

        if (double.IsPositiveInfinity(smallest))
        {
            smallest = (model.DetectorPoint - model.SourcePosition).Length;
        }

        return smallest > 0.0 ? smallest : 1.0;
    }

    /// <summary>The drive frequency, or zero when the model is not driven.</summary>
    private static double DriveFrequency(CompiledModel model)
    {
        var highest = 0.0;

        foreach (var element in model.Fields)
        {
            if (element.Solve?.Drive is { } flat)
            {
                highest = Math.Max(highest, flat.FrequencyHz);
            }

            if (element.Solve3D?.Drive is { } volume)
            {
                highest = Math.Max(highest, volume.FrequencyHz);
            }
        }

        return highest;
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

        // Reported, not bare. A solve that missed its tolerance produces a field
        // indistinguishable from one that met it, so the evidence has to travel
        // alongside and land on every number computed through it (GRD-2).
        var (field, built) = FieldAssembly.BuildReported(model);
        var fieldWarnings = built;

        // REG-1's two modes are peers, so this is a fork rather than a special case
        // inside one path. A diffusive run has no flight time and no final position -
        // there are no trajectories in it - so it produces a different result rather
        // than the same result with fields left empty.
        if (model.TransportMode == "diffusion")
        {
            return (Diffusive(model, field, fieldWarnings, validation, project, timestampUtc), validation);
        }

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

        // REG-1's seam, on the execution path rather than beside it. The validator
        // refuses an unbuilt mode earlier with the same message; this is what stops
        // a model built in code - by a sweep, a study, or a test - from falling
        // through to whichever mode happened to be implemented first.
        _ = TransportModes.Resolve(model.TransportMode);

        var gas = Transport.Collisions.BackgroundGas.FromModel(model.Gas);

        // The reportable number comes from the convergence study, not from a
        // single integration: one run has no honest uncertainty to quote.
        //
        // In a gas the study still flies the gas - reporting a vacuum flight time
        // for a model that declares a pressure would be the same silent substitution
        // the validator refuses elsewhere - but the interval it produces measures
        // the integrator and not the stochastic spread, which is said below.
        var study = FlightTimeStudy.Run(
            launch, species, field, settings, detector,
            collisions: gas.IsPresent
                ? () => new Transport.Collisions.CollisionSampler(
                    gas, species.MassSi, species.ChargeSi, model.Gas.Seed)
                : null);

        var finest = study.Runs[^1];

        // REG-2: the governing dimensionless numbers, computed rather than assumed,
        // and a non-suppressible warning where the selected mode is outside its
        // validity. Measured at the launch speed and the flight this model actually
        // produced, so the numbers describe the run rather than a nominal case.

        var regime = Transport.Collisions.RegimeDiagnostics.Measure(
            gas,
            species,
            Math.Max(launch.Velocity.Length, 1.0),
            finest.FlightTimeSeconds,
            SmallestAperture(model),
            DriveFrequency(model));

        var regimeWarnings = Transport.Collisions.RegimeDiagnostics.ForTrajectoryMode(gas, regime);

        if (gas.IsPresent)
        {
            regimeWarnings =
            [
                .. regimeWarnings,
                new ValidityWarning(
                    "collisions.single-ion-interval",
                    "this flight time is one collisional history. The interval on it is the "
                    + "integrator's own convergence and not the spread of arrival times a packet "
                    + "would have: refining the tolerance does not average over the collisions, it "
                    + "reruns the same draws against a slightly different trajectory. Declare an "
                    + "ion cloud for a number whose interval means what it looks like",
                    WarningSeverity.Qualified),
            ];
        }

        fieldWarnings = [.. fieldWarnings, .. regimeWarnings];

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
            FlightTime = MeasuredJson.From(Carry(study.FlightTime, fieldWarnings), "us"),
            Outcome = finest.Outcome.ToString(),
            FinalPositionMm =
            [
                finest.FinalState.Position.X * 1e3,
                finest.FinalState.Position.Y * 1e3,
                finest.FinalState.Position.Z * 1e3,
            ],
            MaximumRelativeEnergyDrift = finest.MaximumRelativeEnergyDrift,
            Regime = gas.IsPresent
                ? new RegimeJson
                {
                    PressureMbar = regime.PressureMbar,
                    CollisionModel = model.Gas.Model,
                    MeanFreePathMm = regime.MeanFreePathM * 1e3,
                    ApertureMm = regime.ApertureM * 1e3,
                    Knudsen = regime.Knudsen,
                    CollisionsPerFlight = regime.CollisionsPerFlight,
                    CollisionsPerRfCycle = regime.CollisionsPerRfCycle,
                }
                : null,
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
            run = run with { Ensemble = Ensemble(model, fieldWarnings) };
        }

        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");
        File.WriteAllText(resultPath, CommandJson.Write(run));

        return (run with { Artifacts = [.. artifacts, Path.GetRelativePath(project.Root, resultPath)] }, validation);
    }
}
