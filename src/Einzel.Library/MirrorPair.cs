using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Library;

/// <summary>
/// A field reflected through a plane normal to x.
/// </summary>
/// <remarks>
/// The second mirror of a pair is the first one turned around, so it is built by
/// reflection rather than solved again. That is not only cheaper: it makes the
/// two halves identical by construction, so a difference between the inbound and
/// outbound legs of a trajectory cannot come from the two mirrors having been
/// meshed differently.
/// </remarks>
public sealed class ReflectedField(IElectrostaticField inner, double planeX) : IElectrostaticField
{
    private readonly IElectrostaticField _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private Vec3 Reflect(in Vec3 position) => new((2.0 * planeX) - position.X, position.Y, position.Z);

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var mirrored = Reflect(in position);
        var field = _inner.ElectricFieldAt(in mirrored);

        // x flips with the coordinate; y and z do not.
        return new Vec3(-field.X, field.Y, field.Z);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var mirrored = Reflect(in position);
        return _inner.PotentialAt(in mirrored);
    }

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var mirrored = Reflect(in position);
        var mirroredDirection = new Vec3(-direction.X, direction.Y, direction.Z);
        return _inner.FieldFreeRunLength(in mirrored, in mirroredDirection);
    }

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var mirrored = Reflect(in position);
        return _inner.SignedDistanceToDiscontinuity(in mirrored);
    }

    /// <inheritdoc/>
    public double ResolutionLength => _inner.ResolutionLength;
}

/// <summary>The outcome of flying one ion through a mirror pair.</summary>
/// <param name="Arrived">Whether the ion reached the detector.</param>
/// <param name="FlightTimeSeconds">Elapsed flight time.</param>
/// <param name="DriftDistanceMetres">Drift the ion advanced, which sets the analyzer's length that way.</param>
/// <param name="Reflections">Reflections completed.</param>
/// <param name="EnergyDrift">Largest relative energy drift over the flight.</param>
/// <param name="Outcome">
/// How the flight ended. Reported rather than reduced to a boolean: an ion that
/// ran out of flight-time ceiling and one that hit a step-size floor are
/// different failures with different fixes, and a study that silently drops both
/// as "did not arrive" reports a transmission it has not understood.
/// </param>
public sealed record MirrorPairFlight(
    bool Arrived,
    double FlightTimeSeconds,
    double DriftDistanceMetres,
    int Reflections,
    double EnergyDrift,
    TrajectoryOutcome Outcome = TrajectoryOutcome.StopConditionMet);

/// <summary>
/// Two planar mirrors facing each other, with an ion oscillating between them
/// while drifting along the stripes.
/// </summary>
/// <remarks>
/// <para>
/// The companion memo's analyzer, in the form its section 6 asks to start from:
/// the long-focus-lens geometry, without per-oscillation dispersion control. The
/// ion oscillates between the mirrors and advances steadily in the drift
/// direction until it reaches the detector.
/// </para>
/// <para>
/// The drift is free, exactly. Because the stripes run along it the field has no
/// component that way, so the drift velocity is a constant of the motion and the
/// inclination angle sets it once at launch. That is why the memo can treat
/// inclination as a footprint parameter rather than an optical one — and why
/// pushing it from two degrees to six or eight, which its section 4 does to keep
/// the analyzer a shoebox, costs nothing in the oscillation plane.
/// </para>
/// <para>
/// What this does not model is the convergence that turns the drift around and
/// brings the ion back to the same end. That needs mirrors tilted in the drift
/// plane, which breaks the invariance the whole two-dimensional reduction rests
/// on. It belongs with the sequencer work, not here.
/// </para>
/// </remarks>
public sealed class MirrorPair
{
    private readonly IElectrostaticField _field;

    /// <summary>Builds a pair from one solved mirror.</summary>
    /// <param name="mirror">The mirror, entered from positive x.</param>
    /// <param name="capToCap">Distance between the two mirror entrances, in metres.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The separation is not positive.</exception>
    public MirrorPair(PlanarMirror mirror, double capToCap)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capToCap);

        Mirror = mirror;
        CapToCap = capToCap;

        var first = mirror.Field();
        var second = new ReflectedField(first, capToCap / 2.0);

        _field = new Einzel.Fields.SuperposedField([first, second]);
    }

    /// <summary>The mirror both halves are built from.</summary>
    public PlanarMirror Mirror { get; }

    /// <summary>Distance between the mirror entrances, in metres.</summary>
    public double CapToCap { get; }

    /// <summary>The combined field.</summary>
    public IElectrostaticField Field => _field;

    /// <summary>
    /// The cap-to-cap distance that puts a mirror pair at its first-order energy
    /// focus, given a turning depth.
    /// </summary>
    /// <param name="turningDepth">Depth the ion reaches in each mirror, in metres.</param>
    /// <returns>The separation, in metres.</returns>
    /// <remarks>
    /// Four penetration depths, the same condition a single-stage reflectron
    /// obeys and for the same reason: the drift time falls with velocity while the
    /// time in the mirror rises with it, and the two rates cancel when the
    /// field-free path is four times the depth.
    /// </remarks>
    public static double FirstOrderFocusSeparation(double turningDepth) => 4.0 * turningDepth;

    /// <summary>Flies one ion for a fixed number of oscillations.</summary>
    /// <param name="species">The ion.</param>
    /// <param name="kineticEnergy">Kinetic energy at launch.</param>
    /// <param name="inclination">Angle between the velocity and the oscillation axis.</param>
    /// <param name="oscillations">Complete oscillations to fly.</param>
    /// <param name="settings">Integration settings; a per-half-period ceiling is supplied if absent.</param>
    /// <returns>The flight.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The oscillation count is not positive.</exception>
    /// <remarks>
    /// <para>
    /// A fixed oscillation count, not a fixed drift distance, and the difference
    /// decides whether the analyzer focuses at all. Stopping at a detector a set
    /// distance along the drift makes the arrival time equal that distance divided
    /// by the drift velocity, which depends only on energy and not at all on the
    /// mirrors — so the flight time varies as one over the square root of energy
    /// and there is no focusing to be had. A real multi-reflection analyzer fixes
    /// the number of oscillations instead: the converging mirrors bring every ion
    /// back after the same number of passes, and the flight time is that count
    /// times the oscillation period, which is where the four-penetration-depth
    /// condition does its work.
    /// </para>
    /// <para>
    /// The period is measured once and multiplied, rather than the whole flight
    /// being stitched together from individual crossings. In a static field with
    /// the drift decoupled the oscillation is strictly periodic, so this is exact
    /// — and it is more accurate than stitching, because every leg boundary is a
    /// root-find that has to be landed on and a hundred of them accumulate what
    /// two do not. It is also more robust: a leg that begins exactly on its own
    /// stopping surface is a delicate thing, and doing it twice instead of two
    /// dozen times leaves far less to go wrong.
    /// </para>
    /// <para>
    /// The two half periods are compared as a check. Both mirrors are the same
    /// object reflected, so the halves must agree; when they do not, something has
    /// gone wrong in the integration rather than in the optics, and the flight
    /// says so instead of returning a number.
    /// </para>
    /// </remarks>
    public MirrorPairFlight Fly(
        IonSpecies species,
        Quantity kineticEnergy,
        Quantity inclination,
        int oscillations,
        IntegrationSettings? settings = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(oscillations);

        var speed = Math.Sqrt(2.0 * kineticEnergy.In("J") / species.MassSi);
        var angle = inclination.In("rad");
        var midPlane = CapToCap / 2.0;

        // The oscillation plane is x-y; drift is along z, free and constant,
        // because the stripes run that way and the field has no component along
        // them. Inclination therefore sets the drift once at launch and never
        // enters the oscillation.
        var state = new PhaseState(
            new Vec3(midPlane, 0.0, 0.0),
            new Vec3(speed * Math.Cos(angle), 0.0, speed * Math.Sin(angle)));

        var effective = settings ?? new IntegrationSettings
        {
            MaximumFlightTime = 20.0 * CapToCap / (speed * Math.Cos(angle)),
        };

        TrajectoryStopFunction outward = (in PhaseState s) => s.Position.X - midPlane;
        TrajectoryStopFunction homeward = (in PhaseState s) => midPlane - s.Position.X;

        var first = TrajectoryIntegrator.Integrate(state, species, _field, effective, outward);

        if (first.Outcome != TrajectoryOutcome.StopConditionMet)
        {
            return new MirrorPairFlight(false, 0.0, 0.0, 0, 0.0, first.Outcome);
        }

        var second = TrajectoryIntegrator.Integrate(
            first.FinalState, species, _field, effective, homeward);

        if (second.Outcome != TrajectoryOutcome.StopConditionMet)
        {
            return new MirrorPairFlight(false, first.FlightTimeSeconds, 0.0, 0, 0.0, second.Outcome);
        }

        var period = first.FlightTimeSeconds + second.FlightTimeSeconds;
        var asymmetry = Math.Abs(first.FlightTimeSeconds - second.FlightTimeSeconds) / period;

        // A pair built by reflecting one mirror is symmetric by construction, so
        // unequal halves mean the integration went wrong, not the instrument.
        if (asymmetry > 1e-6)
        {
            return new MirrorPairFlight(
                false, period, 0.0, 0, Math.Max(first.MaximumRelativeEnergyDrift, second.MaximumRelativeEnergyDrift),
                TrajectoryOutcome.StepSizeUnderflow);
        }

        var total = period * oscillations;

        return new MirrorPairFlight(
            true,
            total,
            speed * Math.Sin(angle) * total,
            oscillations,
            Math.Max(first.MaximumRelativeEnergyDrift, second.MaximumRelativeEnergyDrift));
    }

    /// <summary>The oscillation period at a given energy, in seconds.</summary>
    /// <param name="species">The ion.</param>
    /// <param name="kineticEnergy">Kinetic energy.</param>
    /// <returns>The period, or NaN when the ion is not returned.</returns>
    public double Period(IonSpecies species, Quantity kineticEnergy)
    {
        var flight = Fly(species, kineticEnergy, Quantity.From(0.0, "deg"), oscillations: 1);
        return flight.Arrived ? flight.FlightTimeSeconds : double.NaN;
    }
}
