using Einzel.Core.Geometry;
using Einzel.Fields;

namespace Einzel.Transport.Diffusion;

/// <summary>
/// A driven field presented as the time-averaged effective field an ion in a gas
/// actually drifts through.
/// </summary>
/// <remarks>
/// <para>
/// The drift-diffusion solve steps a density through one static field. A driven
/// structure has no static field, and sampling one at a chosen instant gives the RF
/// at that phase - a field that exists for no length of time. What a slow ion
/// experiences instead is the cycle average: the oscillating field pushes it back
/// and forth, and because the field is stronger at one end of that excursion than
/// the other, the round trip leaves a net force towards weaker field. That net
/// force is the gradient of an effective potential, and this presents the sum of it
/// and the DC potential as an ordinary field.
/// </para>
/// <para>
/// <b>The derivation, because the collisional form is not the one usually quoted.</b>
/// An ion at position x0 quivering by delta in a field E0(x) cos(Omega t), damped at
/// rate nu, obeys m(v' + nu v) = q E0 cos(Omega t). The steady quiver has
/// v = (q E0 / m)(nu cos + Omega sin)/(Omega^2 + nu^2), so the displacement is
/// delta = (q E0 / m)(nu sin - Omega cos)/(Omega (Omega^2 + nu^2)). The cycle-averaged
/// force is q (dE0/dx) times the average of delta cos(Omega t), which is
/// -q E0 / (2 m (Omega^2 + nu^2)). That is the gradient of
/// </para>
/// <para>
/// <c>Psi = q^2 E0^2 / (4 m (Omega^2 + nu^2))</c>
/// </para>
/// <para>
/// which reduces to Dehmelt's q^2 E0^2 / (4 m Omega^2) when collisions are rare and
/// is suppressed by Omega^2/(Omega^2 + nu^2) when they are not. That suppression is
/// the whole reason this class exists rather than a one-line addition of the
/// textbook pseudopotential: at the pressures an ion funnel runs at, the collision
/// rate is comparable to the drive frequency and the well is a fraction of what the
/// collisionless formula promises.
/// </para>
/// <para>
/// <b>The damping rate is the momentum-transfer rate, not the collision rate.</b>
/// It is taken from the mobility - nu = q/(m mu) - rather than from the number of
/// collisions per cycle, and the difference is not small. A heavy ion in a light gas
/// gives up only about the mass ratio of its momentum per collision, so for m/z 500
/// in nitrogen the collision count overstates the damping by roughly twenty times.
/// Taking it from the mobility also keeps it consistent with the drift the same
/// solve computes, which a second independent estimate would not be.
/// </para>
/// <para>
/// Written as a field wrapper so the solver needs no change at all: it asks for a
/// potential and a field at a point, and gets the effective ones. The same choice
/// <c>AxisymmetricField</c> makes.
/// </para>
/// </remarks>
public sealed class PonderomotiveField : IElectrostaticField
{
    /// <summary>
    /// Differencing step for a field with no resolution of its own, in metres.
    /// </summary>
    /// <remarks>
    /// A micrometre: below anything this engine meshes and far above the scale where
    /// cancellation in a double would start to matter.
    /// </remarks>
    private const double DefaultStepM = 1e-6;

    private readonly ITimeVaryingField _driven;
    private readonly double _massSi;
    private readonly Func<Vec3, double>? _rateAt;
    private readonly double _chargeSi;
    private readonly double _scale;
    private readonly double _step;
    private readonly int _samples;

    /// <summary>Wraps a driven field for an ion of a given species in a given gas.</summary>
    /// <param name="driven">The driven field.</param>
    /// <param name="chargeSi">The ion's charge, in coulombs.</param>
    /// <param name="massSi">The ion's mass, in kilograms.</param>
    /// <param name="collisionRateSi">
    /// Momentum-transfer rate, in inverse seconds. Zero for a collisionless ion,
    /// which gives the Dehmelt pseudopotential.
    /// </param>
    /// <param name="samplesPerCycle">
    /// How finely the drive cycle is sampled when averaging. Sixteen resolves a
    /// sinusoid to well under a per cent and a rectangular wave to its duty cycle.
    /// </param>
    /// <param name="collisionRateAt">
    /// The momentum-transfer rate at a point, or null where the gas is uniform. A
    /// pressure field grades the damping, because the rate goes as the density; where
    /// this is null the constant rate above is used everywhere and the arithmetic is
    /// bit-identical to what it was before a graded gas could be declared.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="driven"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A non-positive mass or sample count, a negative collision rate, or a drive
    /// with no period.
    /// </exception>
    public PonderomotiveField(
        ITimeVaryingField driven,
        double chargeSi,
        double massSi,
        double collisionRateSi,
        int samplesPerCycle = 16,
        Func<Vec3, double>? collisionRateAt = null)
    {
        ArgumentNullException.ThrowIfNull(driven);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massSi);
        ArgumentOutOfRangeException.ThrowIfNegative(collisionRateSi);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerCycle, 4);

        var period = driven.ShortestPeriodSeconds;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);

        _driven = driven;
        _chargeSi = chargeSi;
        _massSi = massSi;
        _samples = samplesPerCycle;
        _rateAt = collisionRateAt;

        PeriodSeconds = period;
        AngularFrequencySi = 2.0 * Math.PI / period;
        CollisionRateSi = collisionRateSi;

        var damped = (AngularFrequencySi * AngularFrequencySi) + (collisionRateSi * collisionRateSi);

        // Psi = q^2 E0^2 / (4 m (Omega^2 + nu^2)), and the mean square of the
        // oscillating field over a cycle is E0^2 / 2 for a linear polarisation - so
        // in terms of that mean square the coefficient is q^2 / (2 m (...)), which
        // is also the right generalisation when the field traces an ellipse.
        _scale = chargeSi * chargeSi / (2.0 * massSi * damped);

        Suppression = AngularFrequencySi * AngularFrequencySi / damped;

        // Differencing the effective potential over half the resolution of whatever
        // is underneath: fine enough to follow it, coarse enough not to be
        // differencing interpolation noise.
        //
        // Finite, not merely positive. An analytic field reports a resolution of
        // positive infinity - meaning it has no resolution limit, not that it has an
        // enormous one - and reading that as a length gave a step of infinity, a
        // difference of infinity minus infinity, and a field of NaN. The potential
        // was correct throughout; only its gradient was not a number.
        var resolution = driven.ResolutionLength;

        _step = double.IsFinite(resolution) && resolution > 0.0
            ? 0.5 * resolution
            : DefaultStepM;
    }

    /// <summary>The drive period, in seconds.</summary>
    public double PeriodSeconds { get; }

    /// <summary>Angular drive frequency, in radians per second.</summary>
    public double AngularFrequencySi { get; }

    /// <summary>Momentum-transfer rate, in inverse seconds.</summary>
    /// <remarks>
    /// At the model's declared density. Where a pressure field grades the gas the
    /// rate varies with it - the momentum-transfer rate goes as the density, since
    /// mobility goes as its reciprocal - and this is the value at the reference,
    /// reported so a reader has one number to hold the range against.
    /// </remarks>
    public double CollisionRateSi { get; }

    /// <summary>Whether the damping varies from place to place.</summary>
    public bool IsGraded => _rateAt is not null;

    /// <summary>How much collisions weaken the well at a point.</summary>
    /// <param name="position">Where, in metres.</param>
    /// <returns>Omega^2 / (Omega^2 + nu(x)^2), between zero and one.</returns>
    /// <remarks>
    /// One where the drive is far faster than the collisions and the textbook
    /// Dehmelt well stands; falling toward zero as the quiver is damped out of
    /// existence. A graded gas grades this, because the momentum-transfer rate goes
    /// as the density.
    /// </remarks>
    public double SuppressionAt(in Vec3 position)
    {
        if (_rateAt is null)
        {
            return Suppression;
        }

        var rate = _rateAt(position);
        var omega = AngularFrequencySi * AngularFrequencySi;

        return omega / (omega + (rate * rate));
    }

    /// <summary>
    /// The coefficient turning a mean-square field into an effective energy, at a point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A constant where the gas is uniform, and returned as the precomputed one so
    /// that an ungraded model is bit-identical to what it was.
    /// </para>
    /// <para>
    /// <b>The gradient of this is a real force.</b> Where the damping varies the
    /// effective well varies with it even at constant field amplitude, and
    /// differencing the potential picks that up - which is right: an ion in a funnel
    /// whose gas thins toward the exit sees a well that deepens toward the exit, and
    /// a version holding the damping at one declared value would report the well as
    /// flat where it is not. It holds to the same order the cycle average itself
    /// does, which is that the gas changes little over one quiver amplitude.
    /// </para>
    /// </remarks>
    private double ScaleAt(in Vec3 position)
    {
        if (_rateAt is null)
        {
            return _scale;
        }

        var rate = _rateAt(position);
        var damped = (AngularFrequencySi * AngularFrequencySi) + (rate * rate);

        return _chargeSi * _chargeSi / (2.0 * _massSi * damped);
    }

    /// <summary>
    /// How much collisions weaken the effective well, as
    /// Omega^2 / (Omega^2 + nu^2).
    /// </summary>
    /// <remarks>
    /// One when collisions are rare, and the factor the textbook pseudopotential
    /// overstates the well by when they are not. Reported whether or not it crosses
    /// a threshold, per REG-2: a reader who sees 0.9 knows the question was asked.
    /// </remarks>
    public double Suppression { get; }

    /// <inheritdoc/>
    public double ResolutionLength => _driven.ResolutionLength;

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _driven.SignedDistanceToDiscontinuity(in position);

    /// <summary>The effective potential: the DC part plus the ponderomotive well.</summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The potential, in volts.</returns>
    public double PotentialAt(in Vec3 position)
    {
        var direct = 0.0;
        var meanSquare = 0.0;
        var mean = default(Vec3);

        // One pass for the cycle mean of the field, which is the DC part, and one
        // for the mean square of what is left. Two passes rather than one because
        // the oscillating part is defined relative to the mean, and a structure with
        // an asymmetric duty cycle has a mean that is not zero - which is a real DC
        // offset and belongs in the direct term, not in the well.
        for (var s = 0; s < _samples; s++)
        {
            var time = PeriodSeconds * s / _samples;

            direct += _driven.PotentialAt(in position, time);
            mean += _driven.ElectricFieldAt(in position, time);
        }

        direct /= _samples;
        mean *= 1.0 / _samples;

        for (var s = 0; s < _samples; s++)
        {
            var time = PeriodSeconds * s / _samples;
            var oscillating = _driven.ElectricFieldAt(in position, time) - mean;

            meanSquare += oscillating.LengthSquared;
        }

        meanSquare /= _samples;

        // Psi is an energy; divided by the charge it is a potential, which is what
        // the drift-diffusion solve wants. The sign is such that a strong-field
        // region is uphill for either polarity, because the ponderomotive force
        // always pushes towards weaker field - so the charge divides out in
        // magnitude but not in sign, and dividing by the signed charge here is what
        // makes an anion feel the same well as a cation.
        return direct + (ScaleAt(in position) * meanSquare / _chargeSi);
    }

    /// <summary>The effective field: minus the gradient of the effective potential.</summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The field vector, in volts per metre.</returns>
    /// <remarks>
    /// By central differences rather than analytically, because the ponderomotive
    /// term is a cycle average of a superposed interpolant and its closed-form
    /// gradient would be a second implementation of the same quantity - the kind of
    /// pair that agrees until one of them is changed.
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var h = _step;

        var x = (PotentialAt(position + new Vec3(h, 0.0, 0.0))
            - PotentialAt(position - new Vec3(h, 0.0, 0.0))) / (2.0 * h);

        var y = (PotentialAt(position + new Vec3(0.0, h, 0.0))
            - PotentialAt(position - new Vec3(0.0, h, 0.0))) / (2.0 * h);

        var z = (PotentialAt(position + new Vec3(0.0, 0.0, h))
            - PotentialAt(position - new Vec3(0.0, 0.0, h))) / (2.0 * h);

        return new Vec3(-x, -y, -z);
    }

    /// <summary>
    /// How far the ion is swept back and forth by the drive at a point, in metres.
    /// </summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The quiver amplitude.</returns>
    /// <remarks>
    /// The effective potential is an average over an excursion, so it only describes
    /// anything if the field is roughly linear across that excursion. When the
    /// quiver approaches the scale the field varies on, the averaging has nothing
    /// left to average and the ion's real motion is not a small wobble about a drift
    /// - it is the whole story.
    /// </remarks>
    public double QuiverAmplitude(in Vec3 position)
    {
        var mean = default(Vec3);
        var meanSquare = 0.0;

        for (var s = 0; s < _samples; s++)
        {
            mean += _driven.ElectricFieldAt(in position, PeriodSeconds * s / _samples);
        }

        mean *= 1.0 / _samples;

        for (var s = 0; s < _samples; s++)
        {
            var oscillating =
                _driven.ElectricFieldAt(in position, PeriodSeconds * s / _samples) - mean;

            meanSquare += oscillating.LengthSquared;
        }

        meanSquare /= _samples;

        // Amplitude of a linear polarisation carrying this mean square.
        var amplitude = Math.Sqrt(2.0 * meanSquare);

        // The rate here, not the rate at the reference density: the quiver is damped
        // by the gas the ion is actually in, and rf.quiver-exceeds-mesh is checked
        // against this. Identical to CollisionRateSi where the gas is uniform, so an
        // ungraded model is bit-identical.
        var rate = _rateAt is null ? CollisionRateSi : _rateAt(position);

        var damped = Math.Sqrt((AngularFrequencySi * AngularFrequencySi) + (rate * rate));

        // delta = q E0 / (m Omega sqrt(Omega^2 + nu^2)), and q^2/(2 m (...)) is
        // already in the scale, so q/m is 2 scale (Omega^2 + nu^2) / q.
        var chargeToMass = 2.0 * ScaleAt(in position) * damped * damped / _chargeSi;

        return chargeToMass * amplitude / (AngularFrequencySi * damped);
    }

    /// <summary>
    /// The momentum-transfer rate implied by a mobility, in inverse seconds.
    /// </summary>
    /// <param name="chargeSi">The ion's charge, in coulombs.</param>
    /// <param name="massSi">The ion's mass, in kilograms.</param>
    /// <param name="mobilitySi">Mobility, in square metres per volt second.</param>
    /// <returns>The rate, or zero for an infinite mobility.</returns>
    /// <remarks>
    /// Drude: a steady field gives a drift q E / (m nu), so mu = q/(m nu). Using the
    /// mobility the solve already has keeps the damping in the well consistent with
    /// the drift beside it, which a separately estimated collision frequency would
    /// not be - and it is the momentum-transfer rate, which is the one the equation
    /// of motion wants.
    /// </remarks>
    public static double CollisionRateFromMobility(double chargeSi, double massSi, double mobilitySi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massSi);

        return mobilitySi > 0.0 ? Math.Abs(chargeSi) / (massSi * mobilitySi) : 0.0;
    }
}
