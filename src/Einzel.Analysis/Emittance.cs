using Einzel.Core.Geometry;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Transport;

namespace Einzel.Analysis;

/// <summary>
/// The phase-space area a packet occupies along one transverse axis.
/// </summary>
/// <remarks>
/// <para>
/// The quantity that answers "will this packet get through the next aperture". A
/// beam is described by where its ions are and which way they are going, and
/// emittance is the area those two coordinates occupy together. It is the reason
/// a wide, parallel beam and a narrow, diverging one can be equally hard to use:
/// optics trade size against divergence, and cannot reduce the product.
/// </para>
/// <para>
/// That last sentence is Liouville's theorem, and it makes emittance a check as
/// well as a figure of merit. Conservative forces preserve phase-space area
/// exactly, so a drift or an ideal lens must leave it unchanged, and any growth is
/// aberration or something non-conservative. It tests the integrator along an axis
/// energy conservation says nothing about.
/// </para>
/// <para>
/// Spec section 12 asks for it twice: packet emittance for extraction traps and
/// orthogonal accelerators, which is Class T, and exit emittance for funnels and
/// guides, which is Class S. Same quantity, different devices.
/// </para>
/// </remarks>
public sealed record Emittance
{
    private Emittance(
        int ions,
        double rmsSize,
        double rmsDivergence,
        double correlation,
        double geometric,
        double normalised,
        double betaGamma)
    {
        Ions = ions;
        RmsSizeM = rmsSize;
        RmsDivergenceRad = rmsDivergence;
        Correlation = correlation;
        GeometricM = geometric;
        NormalisedM = normalised;
        BetaGamma = betaGamma;
    }

    /// <summary>How many ions the moments were taken over.</summary>
    public int Ions { get; }

    /// <summary>Root-mean-square beam size along this axis, in metres.</summary>
    public double RmsSizeM { get; }

    /// <summary>Root-mean-square divergence along this axis, in radians.</summary>
    public double RmsDivergenceRad { get; }

    /// <summary>
    /// The position-divergence correlation, in radians.
    /// </summary>
    /// <remarks>
    /// Negative for a converging beam, positive for a diverging one, zero at a
    /// waist. It is what distinguishes a packet that is about to be small from one
    /// that is about to be large, which a size alone cannot.
    /// </remarks>
    public double Correlation { get; }

    /// <summary>Geometric emittance, in metre-radians.</summary>
    /// <remarks>
    /// sqrt(&lt;x²&gt;&lt;x'²&gt; − &lt;xx'&gt;²), the root-mean-square emittance. Statistical
    /// rather than a bounding ellipse, because a bounding area is set by whichever
    /// ion strayed furthest and a real distribution has tails.
    /// </remarks>
    public double GeometricM { get; }

    /// <summary>
    /// Normalised emittance, in metre-radians: the same area measured against
    /// transverse momentum rather than against angle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The area in (y, gamma v_y / c) rather than in (y, y') times beta-gamma. The
    /// two agree to first order and differ by the paraxial term - a divergence
    /// angle relates transverse velocity to the <em>axial</em> speed, while
    /// beta-gamma is built from the <em>total</em> speed, and the two differ by a
    /// factor of one plus half the squared divergence.
    /// </para>
    /// <para>
    /// That term is small - around 8e-7 for a milliradian packet - but it does not
    /// cancel across an accelerating stage, because damping shrinks the divergence
    /// and shrinks the term with it. Written this way the invariance is exact
    /// rather than approximate, which is the only reason to have a normalised
    /// emittance at all: it is worth having precisely because it does not change.
    /// </para>
    /// <para>
    /// Newtonian: the momentum is m v, not gamma m v. That is deliberate and it
    /// matters. Transport integrates Newton's equations, so an axial force leaves
    /// transverse velocity exactly unchanged while it would <em>not</em> leave
    /// gamma times that velocity unchanged. Carrying a gamma here would measure the
    /// analysis in one mechanics and the trajectory in another, and the difference
    /// shows up as an emittance that grows out of nowhere under acceleration - it
    /// read 2.1e-8 across a 10 V to 2 kV stage, which is exactly gamma minus one at
    /// the exit speed.
    /// </para>
    /// <para>
    /// What is given up is bounded and remote: gamma minus one reaches 1 ppm at
    /// around 460 keV for m/z 500, against the few keV an ion-optical instrument
    /// runs at. If a relativistic transport mode is ever added this term has to
    /// come back, and it has to come back in both places at once.
    /// </para>
    /// </remarks>
    public double NormalisedM { get; }

    /// <summary>
    /// The mean relativistic factor of the packet, reported for scale.
    /// </summary>
    /// <remarks>
    /// Around 1e-5 for keV ions, which is why a normalised emittance reads so much
    /// smaller than the geometric one. Informational: the normalisation does not go
    /// through this number.
    /// </remarks>
    public double BetaGamma { get; }

    /// <summary>Geometric emittance in the conventional millimetre-milliradian.</summary>
    /// <remarks>
    /// A radian is dimensionless, so an emittance has the dimension of length and
    /// is reported as one. The conventional unit is mm·mrad, which is 1e-6 m·rad,
    /// and it is offered here because every published figure uses it.
    /// </remarks>
    public double MillimetreMilliradian => GeometricM * 1e6;

    /// <summary>The Twiss beta, in metres: how the size relates to the area.</summary>
    public double TwissBetaM => GeometricM > 0.0 ? RmsSizeM * RmsSizeM / GeometricM : double.NaN;

    /// <summary>
    /// The Twiss alpha, dimensionless: positive while converging, zero at a waist,
    /// negative once past it.
    /// </summary>
    /// <remarks>
    /// Minus the correlation over the area, which is the sign convention every
    /// accelerator text uses and the opposite of what the raw correlation reads.
    /// Stated because getting it backwards inverts "about to focus" and "already
    /// focused", and both look plausible in isolation.
    /// </remarks>
    public double TwissAlpha => GeometricM > 0.0 ? -Correlation / GeometricM : double.NaN;

    /// <summary>
    /// Computes the emittance of a packet along one axis.
    /// </summary>
    /// <param name="states">The ions.</param>
    /// <param name="transverse">The axis to measure across; normalised internally.</param>
    /// <param name="axial">The direction of travel; normalised internally.</param>
    /// <returns>The emittance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two ions, or none is moving along the axis.</exception>
    /// <remarks>
    /// Divergence is velocity across over velocity along, which is the small-angle
    /// definition every published emittance uses. An ion with no axial velocity has
    /// no divergence to speak of and is excluded rather than divided by.
    /// </remarks>
    public static Emittance FromPacket(
        IReadOnlyList<PhaseState> states, in Vec3 transverse, in Vec3 axial)
    {
        ArgumentNullException.ThrowIfNull(states);

        var across = transverse.Normalized();
        var along = axial.Normalized();

        double sumX = 0.0, sumP = 0.0, sumBetaGamma = 0.0;
        var counted = 0;

        var positions = new double[states.Count];
        var divergences = new double[states.Count];
        var momenta = new double[states.Count];

        foreach (var state in states)
        {
            var axialSpeed = Vec3.Dot(state.Velocity, along);

            if (axialSpeed <= 0.0)
            {
                // Going backwards or standing still. Its divergence is not a
                // small angle and including it would be reporting a different
                // quantity under the same name.
                continue;
            }

            var position = state.Position;
            var x = Vec3.Dot(position, across);
            var crossVelocity = Vec3.Dot(state.Velocity, across);
            var xPrime = crossVelocity / axialSpeed;

            var beta = state.Velocity.Length / SpeedOfLightSi;
            var gamma = 1.0 / Math.Sqrt(Math.Max(1.0 - (beta * beta), double.Epsilon));

            positions[counted] = x;
            divergences[counted] = xPrime;

            // Transverse momentum over mc: the coordinate conjugate to position,
            // and the one an axial force leaves alone. Newtonian, with no gamma -
            // see the remarks on NormalisedM for why, and for how large the
            // omitted term is.
            momenta[counted] = crossVelocity / SpeedOfLightSi;
            counted++;

            sumX += x;
            sumP += xPrime;
            sumBetaGamma += beta * gamma;
        }

        if (counted < 2)
        {
            throw new ArgumentException(
                $"an emittance needs at least two ions moving along the axis; got {counted}", nameof(states));
        }

        var meanX = sumX / counted;
        var meanP = sumP / counted;
        var meanMomentum = Mean(momenta, counted);

        double varX = 0.0, varP = 0.0, covariance = 0.0;
        double varMomentum = 0.0, momentumCovariance = 0.0;

        for (var k = 0; k < counted; k++)
        {
            var dx = positions[k] - meanX;
            var dp = divergences[k] - meanP;
            var dm = momenta[k] - meanMomentum;

            varX += dx * dx;
            varP += dp * dp;
            covariance += dx * dp;

            varMomentum += dm * dm;
            momentumCovariance += dx * dm;
        }

        varX /= counted;
        varP /= counted;
        covariance /= counted;
        varMomentum /= counted;
        momentumCovariance /= counted;

        return new Emittance(
            counted,
            Math.Sqrt(varX),
            Math.Sqrt(varP),
            covariance,
            Area(varX, varP, covariance),
            Area(varX, varMomentum, momentumCovariance),
            sumBetaGamma / counted);
    }

    /// <summary>
    /// Computes the emittance of a packet in both transverse planes, about its own
    /// direction of travel.
    /// </summary>
    /// <param name="states">The ions.</param>
    /// <returns>The two transverse emittances, wider one first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two ions, or the packet has no net motion.</exception>
    /// <remarks>
    /// <para>
    /// The axis is the packet's mean velocity rather than a geometric one, so this
    /// works for any device without being told which way is downstream. A mirror
    /// turns the beam around and a deflector bends it; taking the axis from the
    /// ions themselves means the answer follows.
    /// </para>
    /// <para>
    /// Two planes rather than one because a real packet is rarely round. A
    /// quadrupole focuses in one and defocuses in the other by construction, and a
    /// slit-shaped source stays slit-shaped, so a single number would average away
    /// the axis that is actually about to clip.
    /// </para>
    /// </remarks>
    public static (Emittance Wider, Emittance Narrower) FromPacket(IReadOnlyList<PhaseState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var mean = Vec3.Zero;

        foreach (var state in states)
        {
            mean += state.Velocity;
        }

        if (mean.Length <= 0.0)
        {
            throw new ArgumentException(
                "the packet has no net direction of travel, so there is no axis to measure across",
                nameof(states));
        }

        var along = mean.Normalized();
        var (first, second) = Perpendiculars(along);

        var a = FromPacket(states, first, along);
        var b = FromPacket(states, second, along);

        return a.GeometricM >= b.GeometricM ? (a, b) : (b, a);
    }

    /// <summary>Two unit vectors perpendicular to a direction, and to each other.</summary>
    /// <remarks>
    /// Seeded from whichever axis the direction leans on least, so the cross
    /// product is never taken against something nearly parallel to it.
    /// </remarks>
    private static (Vec3 First, Vec3 Second) Perpendiculars(in Vec3 along)
    {
        var seed = Math.Abs(along.X) < 0.9 ? new Vec3(1.0, 0.0, 0.0) : new Vec3(0.0, 1.0, 0.0);

        var first = Vec3.Cross(along, seed).Normalized();
        var second = Vec3.Cross(along, first).Normalized();

        return (first, second);
    }

    /// <summary>Geometric emittance, as the GRD-1 envelope.</summary>
    /// <returns>The emittance, with its sampling interval and evidence.</returns>
    /// <remarks>
    /// The interval is the sampling uncertainty of a second moment, which falls as
    /// the square root of the ion count. An emittance quoted from fifty ions
    /// carries a visibly wider interval than the same number from five thousand,
    /// which is the point of quoting it at all.
    /// </remarks>
    public Measured Geometric() => Envelope(GeometricM);

    /// <summary>
    /// Normalised emittance, as the GRD-1 envelope.
    /// </summary>
    /// <returns>The normalised emittance.</returns>
    /// <remarks>
    /// <para>
    /// The reason to bother is that this one survives acceleration. Speeding a beam
    /// up along its axis shrinks the divergence angle without touching the
    /// transverse velocity, so the geometric emittance falls as one over the speed
    /// - adiabatic damping, and entirely real. It makes a beam look better without
    /// anything having improved.
    /// </para>
    /// <para>
    /// Measuring against transverse momentum removes exactly that factor, so this
    /// is the quantity to compare across an accelerating stage and the one a source
    /// is fairly judged by. See <see cref="NormalisedM"/> for why it is not simply
    /// the geometric emittance times beta-gamma.
    /// </para>
    /// </remarks>
    public Measured Normalised() => Envelope(NormalisedM);

    private const double SpeedOfLightSi = 299792458.0;

    private static double Mean(double[] values, int count)
    {
        var sum = 0.0;

        for (var k = 0; k < count; k++)
        {
            sum += values[k];
        }

        return sum / count;
    }

    /// <summary>The root determinant of a two-by-two covariance.</summary>
    /// <remarks>
    /// Clamped at zero: the determinant is non-negative in exact arithmetic, and a
    /// perfectly correlated packet - every ion on one line in phase space, which a
    /// deterministic test cloud can be - rounds to a hair below it.
    /// </remarks>
    private static double Area(double varA, double varB, double covariance) =>
        Math.Sqrt(Math.Max(0.0, (varA * varB) - (covariance * covariance)));

    private Measured Envelope(double value)
    {
        var quantity = Quantity.Si(value, Dimension.LengthDimension);

        // A second moment's relative sampling error goes as one over the square
        // root of twice the count.
        var relative = 1.0 / Math.Sqrt(2.0 * Ions);

        return new Measured(
            quantity,
            UncertaintyInterval.Symmetric(
                quantity, Quantity.Si(value * relative, Dimension.LengthDimension), confidenceLevel: 0.68),
            new Evidence.Ensemble(Ions, Converged: Ions >= 100));
    }
}
