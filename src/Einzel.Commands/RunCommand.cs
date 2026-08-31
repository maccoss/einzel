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
    /// Fraction still inside at the end of the run - neither struck nor arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What a trap is measured by</b>, since a trapped ion by definition never arrives
    /// anywhere. Without it a working trap and one that lost every ion both report a
    /// transmission of zero, and the terminal shows the alarming number without the
    /// descriptive one - which is how `paul-trap-held`, an example that behaves exactly as
    /// designed, reads as a total failure.
    /// </para>
    /// <para>
    /// Counted from the flight the run already did rather than by calling the `confined`
    /// figure of merit, which re-flies the whole ensemble. Two implementations of one
    /// quantity is the defect that made `run` and `test` disagree twice here.
    /// </para>
    /// </remarks>
    public required MeasuredJson Confined { get; init; }

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
    public double? CentralWidthNs { get; init; }

    /// <summary>
    /// The Gaussian-equivalent full width at half maximum, in nanoseconds.
    /// </summary>
    /// <remarks>
    /// What the literature quotes, and what the turn-around closed form gives, so
    /// it is the one to compare against a published number. It exceeds the central
    /// width whenever the peak has a tail, which a second-order energy aberration
    /// always does.
    /// </remarks>
    public double? GaussianFwhmNs { get; init; }

    /// <summary>
    /// Asymmetry of the peak, which is why the two widths differ.
    /// </summary>
    /// <remarks>
    /// Zero for a symmetric peak. A mirror away from its focus produces a
    /// one-sided second-order tail, and the sign says which side.
    /// </remarks>
    public double? Skewness { get; init; }

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
    public MeasuredJson? ResolvingPower { get; init; }

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

/// <summary>One phase of a sequenced run, as it is reported.</summary>
/// <param name="Name">What the phase is for.</param>
/// <param name="Mode">The transport mode it ran in.</param>
/// <param name="EndsAtUs">When it ended, on the instrument's timeline.</param>
/// <param name="Population">How many real ions the packet held when it ended.</param>
/// <param name="Trajectories">
/// How many trajectories carried them, or zero in a diffusive phase where there are none.
/// </param>
/// <param name="CentroidMm">Where the packet was when the phase ended.</param>
/// <param name="Converted">Whether the packet was converted into this description.</param>
public sealed record SequencePhaseJson(
    string Name,
    string Mode,
    double EndsAtUs,
    double Population,
    int Trajectories,
    IReadOnlyList<double> CentroidMm,
    bool Converted);

/// <summary>What a run across a changing transport mode did (SEQ-1).</summary>
/// <param name="Phases">Each phase, in order.</param>
/// <param name="Conversions">How many boundaries the packet was converted across.</param>
/// <param name="ArrivedIons">Real ions that reached the detector, over every phase.</param>
/// <param name="Losses">Every other way ions left, by named surface, in real ions.</param>
/// <remarks>
/// In real ions rather than trajectory counts, because a conversion re-samples: on the
/// far side of one the trajectory count is a numerical choice while the population is
/// what carries across.
/// </remarks>
public sealed record SequenceJson(
    IReadOnlyList<SequencePhaseJson> Phases,
    int Conversions,
    double ArrivedIons,
    IReadOnlyList<WeightedLoss> Losses);

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

    /// <summary>
    /// Bulk speed of the neutral gas, in metres per second, or null where it is
    /// standing still.
    /// </summary>
    /// <remarks>
    /// GAS-1. Reported as a number as well as in a warning because a study over
    /// pressure or flow needs to read it back, and absent rather than zero because
    /// zero is a real answer - a stationary gas - and a reader cannot tell that from
    /// a field that was never consulted.
    /// </remarks>
    public double? GasSpeedSi { get; init; }

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

    /// <summary>Whether the engine computed what it was asked to.</summary>
    /// <remarks>
    /// <para>
    /// <b>Not whether the instrument performed.</b> An ion that struck an electrode, or
    /// that was still held when the declared flight time elapsed, is a result; the
    /// transmission, the itemised losses and <c>confined</c> say what became of it. Only an
    /// integrator that gave up - a step-size underflow, an exhausted step budget - leaves
    /// numbers that stop part way with no bound on how wrong they are.
    /// </para>
    /// <para>
    /// Carried here rather than derived from <see cref="Outcome"/> at the surface, because
    /// deriving it there is what produced a list of the outcome names known when the line
    /// was written. That list had to be widened twice, and still called six of the
    /// thirty-seven shipped examples failures.
    /// </para>
    /// </remarks>
    public required bool Completed { get; init; }

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

    /// <summary>
    /// The per-phase result, when the run crossed a transport-mode boundary (SEQ-1).
    /// </summary>
    public SequenceJson? Sequence { get; init; }

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
        var flight = FiguresOfMerit.FlyCloud(model);

        // Null when fewer than two ions arrived: an arrival peak needs two points to
        // have a width. The flight is still a result, and this used to return null
        // outright - so a packet that lost everything reported no ensemble at all,
        // and its itemised losses went with it. That is backwards. ACC-5's whole
        // subject is transmission by named surface, and the reading most worth
        // having is the one where the transmission is zero.
        var peak = flight.Peak;

        var launched = model.Cloud.Ions;
        var arrived = flight.Arrived.Count;

        var transmission = new Measured(
            Quantity.Si(launched > 0 ? (double)arrived / launched : 0.0, default),
            UncertaintyInterval.Symmetric(
                Quantity.Si(launched > 0 ? (double)arrived / launched : 0.0, default),
                Quantity.Si(0.0, default),
                1.0),
            new Evidence.Ensemble(launched, true),
            []);

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
        var flightTime = peak is { Arrived: > 0 } ? peak.MeanSeconds : 0.0;

        var charge = SpaceCharge.Estimate(model.Cloud, species, flightPotential, flightTime);

        var limit = charge.EffectiveRadiusM > 0.0 && flightPotential != 0.0
            ? SpaceCharge.PopulationLimit(
                Quantity.Si(charge.EffectiveRadiusM, Quantity.From(1.0, "m").Dimension),
                species,
                flightPotential,
                AccuracyBudget,
                flightTime)
            : 0.0;

        var warnings = (IReadOnlyList<ValidityWarning>)
            [
                .. fieldWarnings,
                .. SpaceChargeWarnings(
                    charge, limit, model,
                    (double)charge.Population / Math.Max(1, model.Cloud.Ions)),
            ];

        var held = flight.Remaining.Count;

        var confinedFraction = launched > 0 ? (double)held / launched : 0.0;
        var confinedQuantity = Quantity.Number(confinedFraction);

        var confined = new Measured(
            confinedQuantity,
            UncertaintyInterval.Symmetric(
                confinedQuantity,
                Quantity.Number(
                    Math.Max(
                        Math.Sqrt(confinedFraction * (1.0 - confinedFraction) / Math.Max(1, launched)),
                        1.0 / Math.Max(1, launched))),
                0.68),
            new Evidence.Ensemble(launched, launched >= 100),
            []);

        return new EnsembleOutcome
        {
            Launched = launched,
            Arrived = arrived,
            Confined = MeasuredJson.From(confined, "1"),
            Collisions = flight.Collisions,
            ScatteredIons = flight.ScatteredIons,

            // Counted, not read off the peak, so a transmission of zero is a
            // measurement rather than a missing peak.
            Transmission = MeasuredJson.From(
                Carry(peak is null ? transmission : peak.Transmission(), warnings), "1"),

            Losses = flight.Losses,

            // Absent rather than zero where there is no peak to measure. Zero is a
            // real width and a reader cannot tell the two apart if both print as
            // zero, which is the policy the rest of this surface already follows.
            CentralWidthNs = peak is null ? null : peak.CentralWidthSeconds(0.5) * 1e9,
            GaussianFwhmNs = peak is null ? null : peak.GaussianEquivalentFwhmSeconds * 1e9,
            Skewness = peak?.Skewness,
            TurnAroundFwhmNs = turnAround * 1e9,
            ResolvingPower = peak is null
                ? null
                : MeasuredJson.From(Carry(peak.ResolvingPower(), warnings), "1"),
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
    /// <summary>
    /// Reports the grid's cell against the mean spacing between macroparticles, and
    /// warns where that ratio puts the answer outside the band it was measured in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REG-2's rule, applied to a different quantity: reported whether or not it
    /// crosses a threshold, because a reader who sees the ratio knows the run was
    /// checked and one who sees nothing cannot tell that from its not having been.
    /// </para>
    /// <para>
    /// <b>The accuracy here has an optimum rather than a floor</b>, which is the part
    /// that needs saying out loud. Against the direct sum taken to its own point
    /// limit: 3.68 cells per spacing gives -15.1%, 1.84 gives -4.2%, 0.92 gives
    /// +0.08%, 0.46 gives +4.4%. So the intuitive move - raise the node count for a
    /// better answer - makes it worse past the match, and does so silently. Confirmed
    /// as a sampling artefact rather than a resolution one by holding the cell fixed
    /// and raising the macroparticle count: at 128 nodes the error falls 4.42% to
    /// 1.55% to 0.93% as macroparticles per cell go 0.012 to 0.049 to 0.195.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ValidityWarning> GridResolutionWarnings(
        CompiledSpaceChargeGrid? grid, int macroparticles)
    {
        if (grid is null || macroparticles < 2)
        {
            return [];
        }

        var matched = 2.0 * grid.Padding * Math.Cbrt(macroparticles);
        var ratio = matched / grid.Nodes;

        var advice = (int)Math.Pow(2.0, Math.Max(3.0, Math.Ceiling(Math.Log2(matched))));

        var band = ratio is >= 0.7 and <= 2.0
            ? "which is the band this method was measured in"
            : ratio < 0.7
                ? $"which is finer than the packet has structure. Below about 0.7 the cells hold "
                    + "less than one macroparticle each, so the deposit resolves lumps rather than "
                    + $"a density and the mutual force comes out too strong. Try {advice} nodes, or "
                    + "raise \"ions\""
                : $"which over-smooths the packet: the gathered force is too weak and the packet "
                    + $"comes out narrow. Try {advice} nodes";

        return
        [
            new ValidityWarning(
                "spacecharge.grid-resolution",
                $"the grid's cell is {ratio:F2} of the mean spacing between macroparticles, {band}. "
                + "Accuracy here has an optimum at about one rather than improving with refinement, "
                + "so this number is reported whether or not it is a problem",
                ratio is >= 0.7 and <= 2.0
                    ? WarningSeverity.Provenance
                    : WarningSeverity.ValidityViolation),
        ];
    }

    private static IReadOnlyList<ValidityWarning> SpaceChargeWarnings(
        SpaceChargeEstimate charge, double limit, CompiledModel model, double weight)
    {
        if (model.ModelsSpaceCharge)
        {
            // The estimate exists to say "this matters and the engine is not doing
            // it". Here the engine is doing it, so repeating the warning would be
            // false - and staying silent would leave a reader unable to tell a run
            // that modelled the mutual force from one that found it negligible.
            //
            // What replaces it is provenance: the method used, and the fact that
            // the trajectories are macroparticles rather than ions wherever the
            // population exceeds them. A reader who does not know that is reading a
            // sampled packet as a real one.
            //
            // The two methods approximate different things and each says which,
            // because "space charge was modelled" is not enough to read a number by:
            // the direct sum softens at short range, and the grid method smooths at
            // the cell and stands the packet in an earthed box.
            var grid = model.SpaceChargeGrid;

            var method = grid is null
                ? "by direct summation over every pair"
                : $"on a grid of {grid.Nodes} nodes across a box {grid.Padding:F1} RMS radii wide, "
                    + $"re-solved when the packet's RMS radius moves {grid.RefreshTolerance:P0}";

            var approximation = grid is null
                ? "the force between two of them closer together than the mean macroparticle "
                    + "spacing is softened rather than Coulombic"
                : "the mutual force is smoothed at the scale of one cell, and the packet is solved "
                    + "in an earthed box centred on itself rather than in free space";

            return
            [
                new ValidityWarning(
                    "spacecharge.modelled",
                    $"the ions in this packet push on each other, {method}. "
                    + $"The screening estimate for the same packet is "
                    + $"{charge.TimingFraction / 1e-6:F2} ppm and is reported alongside as a cross-check, "
                    + $"not as the answer. Each trajectory carries the charge and the mass of "
                    + $"{weight:F1} real ions, and {approximation}",
                    WarningSeverity.Provenance),

                .. GridResolutionWarnings(grid, model.Cloud.Ions),
            ];
        }

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
    /// <summary>A run whose phases are not all in one transport description (SEQ-1).</summary>
    /// <remarks>
    /// <para>
    /// A third case beside the two modes rather than a variation of either, for the same
    /// reason the diffusive fork exists: what comes out is not the same result with
    /// fields left empty. A sequenced run ends when its <em>sequence</em> ends, not when
    /// an ion arrives, so there is no single flight time to report - what it has instead
    /// is a per-phase account and, across the whole run, how many ions reached the
    /// detector.
    /// </para>
    /// <para>
    /// The conversions' warnings ride out on the result, which is GRD-2 doing its work at
    /// the seam that matters most here: a reader who takes a number from after a boundary
    /// without knowing the velocities were invented there has been misled by the
    /// platform.
    /// </para>
    /// </remarks>
    private static RunOutcome Sequenced(
        CompiledModel model,
        Fields.IElectrostaticField field,
        IReadOnlyList<ValidityWarning> fieldWarnings,
        ValidateOutcome validation,
        ProjectLayout project,
        DateTimeOffset timestampUtc)
    {
        // The one place that knows where the model file is, so the one place that can
        // resolve a declared gas field.
        var resolved = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(validation.ModelPath) ?? ".");

        var outcome = SequencedRun.Execute(model, field, resolved);

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,

            // Which model, as distinct from which content. Without it verify has to find
            // the model by searching for one that still hashes to the recorded value, and
            // editing the model that was actually run then makes its drift disappear -
            // the result re-attaches to whatever else still matches.
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, validation.ModelPath)),
            SchemaVersion = ModelJson.Parse(File.ReadAllText(validation.ModelPath)).SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,

            // Every mode the run used, in the order the phases used them. One mode
            // recorded here would make a manifest claim to determine a run it does not
            // describe, which is PRJ-3's whole subject.
            TransportMode = string.Join(
                " -> ", outcome.Phases.Select(p => p.Mode).Distinct(StringComparer.Ordinal)),
            ComputePath = EngineBuild.ComputePath,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);
        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");

        File.WriteAllText(manifestPath, manifest.ToJson());

        var last = outcome.Phases[^1];

        return new RunOutcome
        {
            Manifest = manifest,

            FlightTime = MeasuredJson.From(
                Carry(
                    new Measured(
                        Quantity.Si(double.NaN, Dimension.TimeDimension),
                        UncertaintyInterval.Symmetric(
                            Quantity.Si(double.NaN, Dimension.TimeDimension),
                            Quantity.Si(0.0, Dimension.TimeDimension),
                            1.0),
                        new Evidence.Convergence("sequenced transport", double.NaN, 0.0, double.NaN),
                        [
                            new ValidityWarning(
                                "transport.sequenced-no-flight-time",
                                "this run ends when its sequence ends, not when an ion "
                                + "arrives, so there is no single flight time. What each "
                                + "phase did is reported under 'sequence', and the ions that "
                                + "reached the detector are counted there",
                                WarningSeverity.Provenance),
                        ]),
                    [.. fieldWarnings, .. outcome.Warnings]),
                "us"),

            Outcome = "SequenceCompleted",
            Completed = true,

            // The packet's centre where the sequence left it. Not a single ion's final
            // position - there is no single ion - which is why this is a centroid and is
            // labelled as one in the sequence block.
            FinalPositionMm = last.CentroidMm,

            // A driven or switched field does work deliberately, so energy drift stops
            // being a diagnostic; a sequenced run is switched by construction.
            MaximumRelativeEnergyDrift = double.NaN,
            AcceptedSteps = 0,
            AnalyticDriftDistanceM = 0.0,
            Artifacts = [manifestPath],

            Sequence = new SequenceJson(
                [.. outcome.Phases.Select(phase => new SequencePhaseJson(
                    phase.Name,
                    phase.Mode,
                    phase.EndsAtSeconds * 1e6,
                    phase.Population,
                    phase.Trajectories,
                    phase.CentroidMm,
                    phase.Converted))],
                outcome.Conversions,
                outcome.Arrived,
                outcome.Losses),
        };
    }

    private static RunOutcome Diffusive(
        CompiledModel model,
        Fields.IElectrostaticField field,
        IReadOnlyList<ValidityWarning> fieldWarnings,
        ValidateOutcome validation,
        ProjectLayout project,
        DateTimeOffset timestampUtc,
        bool exportVtu)
    {
        // The one place that knows where the model file is, so the one place that can
        // resolve a declared velocity field.
        var resolved = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(validation.ModelPath) ?? ".");

        var outcome = DiffusionRun.Execute(model, field, fieldWarnings, resolved);
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

            // Which model, as distinct from which content. Without it verify has to find
            // the model by searching for one that still hashes to the recorded value, and
            // editing the model that was actually run then makes its drift disappear -
            // the result re-attaches to whatever else still matches.
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, validation.ModelPath)),
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

        // The resolved gas, not a fresh one built from the document. Rebuilding here
        // would report a model with an imported velocity field as standing still,
        // which is the same silent drop the resolution exists to prevent - one line
        // further down the pipe.
        var gas = resolved;

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
            Completed = true,
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
                GasSpeedSi = gas.IsFlowing ? gas.FastestBulkSpeedSi : null,
                Nodes = [outcome.Grid.CountX, outcome.Grid.CountY],
                Steps = result.Steps,
                ElapsedUs = result.ElapsedSeconds * 1e6,
                SpreadMm = [spreadX * 1e3, spreadY * 1e3],
            },

            Artifacts = [Path.GetRelativePath(project.Root, manifestPath)],
        };

        var artifacts = new List<string> { Path.GetRelativePath(project.Root, manifestPath) };

        // What --vtu means for this mode. A diffusive run has no trajectory to write
        // and RND-8 forbids inventing one, so the file it writes is the thing it
        // actually computed: the density at the end of the run, on the grid it was
        // tracked on. Before this the flag was accepted and silently did nothing,
        // which is the worst of the three options - the result of the mode could not
        // be looked at in any form, and nothing said so.
        if (exportVtu)
        {
            Directory.CreateDirectory(project.Scratch);

            var densityPath = Path.Combine(project.Scratch, $"{stem}.density.vti");

            // GRD-2: the warnings travel with the file. A figure or a volume is the
            // artifact most likely to be looked at by someone who never saw the
            // result envelope it came from.
            var provenance = new List<string>
            {
                $"engine: {EngineBuild.Version}",
                $"model: {validation.ModelHash}",
                $"transport: diffusion, {result.Steps} steps over {result.ElapsedSeconds:G6} s",
                $"ions: {outcome.Launched:G6} launched, {result.Collected:G6} collected, "
                    + $"{result.Remaining:G6} still in the domain",
                "units: ions per cubic metre, at grid nodes",
            };

            provenance.AddRange(outcome.Warnings.Select(w => $"{w.Severity}: {w.Code}: {w.Message}"));

            File.WriteAllText(
                densityPath,
                VtuWriter.WriteDensityField(result.Density, "density_per_m3", provenance));

            artifacts.Add(Path.GetRelativePath(project.Root, densityPath));
        }

        run = run with { Artifacts = artifacts };

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
    /// <summary>
    /// The tightest constriction the ion has to pass through, in metres.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because the regime inspector needs the same length:
    /// a Knudsen number is meaningless without one, and two implementations of "the
    /// tightest constriction" would let a run and an inspection of that run disagree
    /// about whether the gas is a continuum.
    /// </remarks>
    internal static double SmallestAperture(CompiledModel model)
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
    internal static double DriveFrequency(CompiledModel model)
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

        var validation = ModelValidator.Validate(
            document, null, Path.GetDirectoryName(full));

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
        var model = ModelValidator.Validate(
            document, null, Path.GetDirectoryName(validation.ModelPath)).Model!;

        // Reported, not bare. A solve that missed its tolerance produces a field
        // indistinguishable from one that met it, so the evidence has to travel
        // alongside and land on every number computed through it (GRD-2).
        var (field, built) = FieldAssembly.BuildReported(model);
        var fieldWarnings = built;

        // A run whose phases are not all in one description is a third case, and it
        // comes first: a model may declare "diffusion" as its own mode and still have a
        // sequence that leaves it, and the sequence is the more specific statement.
        if (model.ChangesTransportMode)
        {
            return (
                Sequenced(model, field, fieldWarnings, validation, project, timestampUtc),
                validation);
        }

        // REG-1's two modes are peers, so this is a fork rather than a special case
        // inside one path. A diffusive run has no flight time and no final position -
        // there are no trajectories in it - so it produces a different result rather
        // than the same result with fields left empty.
        if (model.TransportMode == "diffusion")
        {
            return (
                Diffusive(model, field, fieldWarnings, validation, project, timestampUtc, exportVtu),
                validation);
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

        // Resolved rather than merely built from the model, so a declared velocity
        // field reaches the event-driven models too. FromModel alone would produce a
        // gas with no flow in it and no complaint - which is the failure the sampler
        // used to refuse a flow outright to prevent, and removing that refusal
        // without this would have reintroduced it exactly.
        var gas = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(validation.ModelPath) ?? ".");

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

            // Which model, as distinct from which content. Without it verify has to find
            // the model by searching for one that still hashes to the recorded value, and
            // editing the model that was actually run then makes its drift disappear -
            // the result re-attaches to whatever else still matches.
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, validation.ModelPath)),
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

        // Assembled here rather than at the result, because the exported file must carry
        // the same set (GRD-2) and two expressions for "the warnings on this run" is how
        // they come to differ. This is the seventh place in this engine where evidence
        // about a computation was dropped at a seam, and the sixth was the same writer.
        var flightTime = Carry(study.FlightTime, fieldWarnings);

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

                // GRD-2: the warnings travel with the file. A .vtu is the artifact that
                // travels furthest - opened in ParaView, months later, by someone who never
                // saw the result envelope it came from - so it is the layer where a dropped
                // warning does the most damage. The density path beside this one has always
                // carried them; the trajectory path did not, through the same writer and the
                // same optional parameter.
                var provenance = new List<string>
                {
                    $"engine: {EngineBuild.Version}",
                    $"model: {validation.ModelHash}",
                    $"samples: {recorder.Samples.Count} at {model.SampleIntervalSi:G6} s nominal interval",
                    recorder.Truncated
                        ? "TRUNCATED: sample capacity reached; the tail of this trajectory is missing"
                        : "complete",
                };

                provenance.AddRange(
                    flightTime.Warnings.Select(w => $"{w.Severity}: {w.Code}: {w.Message}"));

                File.WriteAllText(
                    vtuPath, VtuWriter.WriteTrajectory(recorder.Samples, provenance));

                artifacts.Add(Path.GetRelativePath(project.Root, vtuPath));
            }
        }

        var run = new RunOutcome
        {
            Manifest = manifest,
            FlightTime = MeasuredJson.From(flightTime, "us"),
            Outcome = finest.Outcome.ToString(),
            Completed = finest.Outcome.Completed(),
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
