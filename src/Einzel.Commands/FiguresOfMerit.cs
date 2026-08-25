using Einzel.Analysis;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>One figure of merit a study may ask for by name.</summary>
/// <param name="Name">The name a study file uses.</param>
/// <param name="Unit">The unit it is reported in.</param>
/// <param name="Description">What it measures.</param>
/// <param name="LargerIsBetter">
/// Which way is better, so an optimisation need not restate it. A sign error in an
/// objective does not throw; it returns the worst design in the box and looks like
/// a result.
/// </param>
public sealed record FigureOfMeritInfo(string Name, string Unit, string Description, bool LargerIsBetter)
{
    /// <summary>
    /// The physical dimension, derived from the unit rather than stated beside it.
    /// </summary>
    /// <remarks>
    /// Two fields that must agree are one field too many. Deriving means a figure
    /// reported in microseconds cannot be declared dimensionless by an oversight,
    /// which is exactly the class of mistake GRD-1's insistence on units exists to
    /// catch.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public Core.Units.Dimension Dimension => Core.Units.Quantity.From(1.0, Unit).Dimension;
}

/// <summary>
/// The figures of merit a study or an optimisation can name in a file.
/// </summary>
/// <remarks>
/// <para>
/// A sweep driver takes a function from a validated model to a number, which is
/// what makes it device-agnostic. A study <em>file</em> cannot carry a function,
/// so it names one, and this is the registry it names into.
/// </para>
/// <para>
/// Four to begin with, chosen because they answer the questions the tolerance
/// machinery exists for. Section 12's Python objectives will register here too
/// when extensions land; the registry is the seam that keeps that from being a
/// change to the sweep drivers.
/// </para>
/// <para>
/// Everything here is Class T: deterministic trajectories through a static field,
/// no collisions, no space charge. An ensemble here is a spread of launch
/// energies, not a thermal distribution.
/// </para>
/// </remarks>
public static class FiguresOfMerit
{
    /// <summary>How wide an energy spread the ensemble figures sample, as a fraction.</summary>
    /// <remarks>
    /// Plus or minus three per cent, which is the acceptance the companion memo
    /// asks a mirror to hold. A study may override it.
    /// </remarks>
    public const double DefaultEnergySpread = 0.03;

    /// <summary>How many ions the ensemble figures launch.</summary>
    /// <remarks>
    /// Enough to resolve a peak width, few enough that a thousand-draw tolerance
    /// study is not a thousand ensembles too many. A study may override it.
    /// </remarks>
    public const int DefaultIons = 21;

    private static readonly FigureOfMeritInfo[] Catalogue =
    [
        new("flightTime", "us", "Arrival time at the detector, from a convergence study over three integrator tolerances.", false),
        new("energyDrift", "1", "Largest relative departure of total energy over the flight. The ACC-4 budget is 1e-6; this is a diagnostic, not a design target.", false),
        new("resolvingPower", "1", "Arrival-time resolving power across the energy spread, model-free at half maximum.", true),
        new("transmission", "1", "Fraction of launched ions that reach the detector.", true),
        new("arrivalSpread", "ns", "Full width at half maximum of the arrival-time peak, from the source cloud.", false),
        new("turnAroundTime", "ns", "The part of the arrival spread imposed before the ion leaves, by the thermal velocity of the source. What limits a pulsed extraction.", false),
        new("emittance", "um", "Geometric emittance of the arriving packet in its wider transverse plane. A micrometre is a millimetre-milliradian, so the number reads in the conventional unit. Smaller passes through a smaller aperture.", false),
        new("normalisedEmittance", "um", "The same area measured against transverse momentum, so it survives acceleration. The figure to compare a source by, since a geometric emittance can be improved by acceleration alone.", false),
    ];

    /// <summary>Every figure of merit that can be named, ordered by name.</summary>
    public static IReadOnlyList<FigureOfMeritInfo> All =>
        [.. Catalogue.OrderBy(f => f.Name, StringComparer.Ordinal)];

    /// <summary>Looks one up.</summary>
    /// <param name="name">The name a study file used.</param>
    /// <returns>What it measures.</returns>
    /// <exception cref="EinzelException">No figure of merit by that name.</exception>
    public static FigureOfMeritInfo Describe(string name)
    {
        foreach (var candidate in Catalogue)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/figureOfMerit",
            Constraint = $"'{name}' is not a figure of merit this build computes",
            Suggestion = $"available: {string.Join(", ", All.Select(f => f.Name))}",
        });
    }

    /// <summary>Builds the evaluator a sweep or an optimiser drives.</summary>
    /// <param name="name">Which figure of merit.</param>
    /// <param name="energySpread">Fractional energy spread for the ensemble figures.</param>
    /// <param name="ions">How many ions the ensemble figures launch.</param>
    /// <returns>A function from a validated model to the figure, or null when it does not arrive.</returns>
    /// <exception cref="EinzelException">No figure of merit by that name.</exception>
    public static Func<CompiledModel, double?> Evaluator(
        string name, double energySpread = DefaultEnergySpread, int ions = DefaultIons)
    {
        var info = Describe(name);

        return info.Name switch
        {
            "flightTime" => model => Single(model)?.FlightTimeSeconds,
            "energyDrift" => model => Single(model)?.MaximumRelativeEnergyDrift,
            "resolvingPower" => model => Ensemble(model, energySpread, ions) is { Arrived: >= 3 } peak
                ? Magnitude(peak.ResolvingPower())
                : null,
            "transmission" => model => Ensemble(model, energySpread, ions) is { } peak
                ? Magnitude(peak.Transmission())
                : null,
            "arrivalSpread" => model => Ensemble(model, energySpread, ions) is { Arrived: >= 3 } peak
                ? peak.GaussianEquivalentFwhmSeconds
                : null,
            "turnAroundTime" => TurnAround,
            "emittance" => model => PacketEmittance(model)?.Wider.GeometricM,
            "normalisedEmittance" => model => PacketEmittance(model)?.Wider.NormalisedM,
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.InternalError,
                Path = "/figureOfMerit",
                Constraint = $"'{info.Name}' is catalogued but has no evaluator",
                Suggestion = "this is a defect in einzel; please report it",
            }),
        };
    }

    /// <summary>
    /// The magnitude out of a GRD-1 envelope, at the one place that is legitimate.
    /// </summary>
    /// <remarks>
    /// A sweep driver needs a scalar to rank draws by, and there is no ordering on
    /// an envelope. The discard is deliberate and visible, which is exactly the
    /// act GRD-1 is designed to make greppable rather than impossible - the
    /// uncertainty and the warnings are still reported alongside the study, and it
    /// is only the ranking that uses the bare number.
    /// </remarks>
    private static double Magnitude(Core.Results.Measured measured)
    {
        var (value, _, _, _) = measured;
        return value.SiValue;
    }

    /// <summary>One trajectory, with its convergence study.</summary>
    private static TrajectoryResult? Single(CompiledModel model)
    {
        var (launch, species, field, settings, detector) = Setup(model);
        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);

        // A draw whose ion never reaches the detector has no flight time. That is
        // a result about the geometry, not a failure of the study, and the sweep
        // records it as such.
        return result.Outcome == TrajectoryOutcome.StopConditionMet ? result : null;
    }

    /// <summary>
    /// The ensemble a figure of merit is computed over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A model that declares a source cloud is asking for that cloud, and gets it:
    /// a real spread in where the ions were, which way they were going, and how
    /// fast. That is what makes a resolving power a property of the instrument
    /// rather than of one ion.
    /// </para>
    /// <para>
    /// A model that declares none falls back to a deterministic sweep of launch
    /// energy, which is what these figures have always meant and what every
    /// existing study is calibrated against. Evenly spaced rather than random,
    /// because a tolerance study is already a Monte Carlo over geometry and
    /// sampling the energy randomly inside it would put noise on every draw and
    /// call it physics.
    /// </para>
    /// </remarks>
    private static ArrivalTimePeak Ensemble(CompiledModel model, double spread, int ions)
    {
        if (model.Cloud.IsCloud)
        {
            return FromCloud(model);
        }

        var arrivals = new List<double>(ions);

        for (var k = 0; k < ions; k++)
        {
            var fraction = ions == 1 ? 0.0 : (2.0 * k / (ions - 1.0)) - 1.0;
            var offset = spread * fraction;

            var (launch, species, field, settings, detector) = Setup(model, offset);
            var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrivals.Add(result.FlightTimeSeconds);
            }
        }

        return ArrivalTimePeak.FromArrivals(arrivals, ions);
    }

    /// <summary>Flies the cloud a model declares.</summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The arrival-time peak it forms.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two ions arrived.</exception>
    public static ArrivalTimePeak FromCloud(CompiledModel model) => FlyCloud(model).Peak;

    /// <summary>
    /// Flies the cloud a model declares, keeping both when the ions arrived and
    /// where they were going when they did.
    /// </summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The arrival-time peak and the arriving ions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two ions arrived.</exception>
    /// <remarks>
    /// The arrival times answer how a peak is shaped and the final states answer
    /// whether the packet would survive the next aperture. Both come out of one
    /// flight because flying twice for them would double the cost of every
    /// ensemble run to recompute something already in hand.
    /// </remarks>
    public static CloudFlight FlyCloud(CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var (nominal, species, field, settings, detector) = Setup(model);
        // The declared direction, so a packet at rest still knows which way is
        // downstream. For a moving ion it is redundant and ignored.
        var cloud = IonCloud.Draw(in nominal, species, model.Cloud, model.SourceDirection);

        var arrivals = new List<double>(cloud.Length);
        var arrived = new List<PhaseState>(cloud.Length);

        foreach (var start in cloud)
        {
            var result = TrajectoryIntegrator.Integrate(start, species, field, settings, detector);

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrivals.Add(result.FlightTimeSeconds);
                arrived.Add(result.FinalState);
            }
        }

        return new CloudFlight(
            ArrivalTimePeak.FromArrivals(arrivals, model.Cloud.Ions), [.. arrived]);
    }

    /// <summary>
    /// The emittance of the packet that arrives, or null when too few ions do.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception, because a geometry that loses its beam is a
    /// result a sweep records rather than a failure that stops it - the same
    /// convention the single-trajectory figures use for an ion that never lands.
    /// </remarks>
    private static (Emittance Wider, Emittance Narrower)? PacketEmittance(CompiledModel model)
    {
        if (!model.Cloud.IsCloud)
        {
            // A single deterministic ion has no spread to occupy an area with, and
            // a swept launch energy is not a packet either - the ions are not
            // simultaneous, they are one ion under different conditions.
            return null;
        }

        var flight = FlyCloud(model);

        return flight.Arrived.Count >= 3 ? Emittance.FromPacket(flight.Arrived) : null;
    }

    /// <summary>
    /// The arrival spread a source temperature alone would impose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured rather than derived, by flying the same cloud twice: once as
    /// declared, and once with everything except the temperature switched off. The
    /// difference is what the thermal velocity contributed, and it is the quantity
    /// a pulsed extraction is designed around.
    /// </para>
    /// <para>
    /// Two clouds rather than one closed form, because the closed form only holds
    /// for a uniform extraction field. Measuring it works in any geometry, and
    /// where the closed form does apply the two agree - which is what the
    /// transport tests check.
    /// </para>
    /// </remarks>
    private static double? TurnAround(CompiledModel model)
    {
        if (model.Cloud.TemperatureK <= 0.0)
        {
            // No temperature, no turn-around. Zero rather than null: it is a
            // measurement of something absent, not a failure to measure.
            return 0.0;
        }

        var thermalOnly = model with
        {
            Cloud = new IonCloudSettings
            {
                Ions = model.Cloud.Ions,
                Seed = model.Cloud.Seed,
                TemperatureK = model.Cloud.TemperatureK,
            },
        };

        try
        {
            return FromCloud(thermalOnly).GaussianEquivalentFwhmSeconds;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static (PhaseState Launch, IonSpecies Species, IElectrostaticField Field,
        IntegrationSettings Settings, TrajectoryStopFunction Detector) Setup(
        CompiledModel model, double energyOffset = 0.0)
    {
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        // Energy scales as the square of speed, so a fractional energy offset is
        // a square root in velocity. Treating it as a velocity fraction is a
        // factor of two in the linear term and four in the quadratic, and it has
        // been got wrong here before.
        var speed = model.LaunchSpeedSi() * Math.Sqrt(1.0 + energyOffset);
        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * speed);

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        return (launch, species, field, settings, detector);
    }
}
