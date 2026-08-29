using Einzel.Core.Geometry;

namespace Einzel.Transport.Collisions;

/// <summary>
/// Decides when an ion collides and what the collision does to it.
/// </summary>
/// <remarks>
/// <para>
/// A collision is an <em>instant</em> in this description: the ion flies
/// collisionlessly, then its velocity changes discontinuously, then it flies on.
/// That makes it the same kind of thing as a sequencer switch - an event at a
/// known time - so the integrator lands on it exactly by cutting the step, with no
/// root-find and no new machinery.
/// </para>
/// <para>
/// One sampler per ion, carrying its own random stream, so a run is reproducible
/// from its seed (PRJ-3) and adding an ion to an ensemble does not change the
/// flight of any ion before it.
/// </para>
/// </remarks>
public sealed class CollisionSampler
{
    private readonly BackgroundGas _gas;
    private readonly double _ionMass;
    private readonly double _charge;
    private readonly Random _random;

    /// <summary>How many thermal speeds of headroom the null-collision bound carries.</summary>
    /// <remarks>
    /// The Maxwellian has no upper speed, so no finite bound is certain. Five most
    /// probable speeds puts the probability of an over-run around 1e-11 per draw,
    /// and <see cref="BoundExceeded"/> reports it rather than hiding it if one
    /// happens - an unreported over-run silently biases the collision rate low.
    /// </remarks>
    private const double ThermalHeadroom = 5.0;

    /// <summary>Creates a sampler for one ion.</summary>
    /// <param name="gas">The gas.</param>
    /// <param name="ionMassSi">Ion mass, in kilograms.</param>
    /// <param name="chargeSi">Ion charge, in coulombs.</param>
    /// <param name="seed">The random seed for this ion.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    /// <remarks>
    /// A gas flow that varies with position used to be refused here, because a
    /// collision was drawn from a time and a velocity with no place to evaluate the
    /// flow at. Refusing was right at the time - the alternative would have been a
    /// run that used the uniform drift and said nothing, flying an ion through a
    /// declared jet as though the gas were standing still, which is exactly the
    /// mistake GAS-1 exists to prevent. The position is now carried into the draw,
    /// so the sampler sees the flow where the ion actually is.
    /// </remarks>
    public CollisionSampler(BackgroundGas gas, double ionMassSi, double chargeSi, int seed)
    {
        ArgumentNullException.ThrowIfNull(gas);

        _gas = gas;
        _ionMass = ionMassSi;
        _charge = chargeSi;
        _random = new Random(seed);
    }

    /// <summary>The gas this sampler draws from.</summary>
    public BackgroundGas Gas => _gas;

    /// <summary>How many collisions have actually happened.</summary>
    public int Collisions { get; private set; }

    /// <summary>How many scheduled events turned out to be null collisions.</summary>
    /// <remarks>
    /// Reported because it is the cost of the method: a high null fraction means
    /// the bound is far above the true rate and the scheduler is doing many times
    /// more work than the physics needs.
    /// </remarks>
    public int NullEvents { get; private set; }

    /// <summary>
    /// Whether a sampled relative speed ever exceeded the null-collision bound.
    /// </summary>
    /// <remarks>
    /// True means the collision rate was underestimated for at least one event and
    /// the result is biased. Non-suppressible where it is reported, because a
    /// silently biased rate is indistinguishable from a correct one.
    /// </remarks>
    public bool BoundExceeded { get; private set; }

    /// <summary>
    /// Whether a collision was ever drawn at a point outside the imported flow
    /// field's extent.
    /// </summary>
    /// <remarks>
    /// A sampled flow clamps to its edge value outside its box, which is a choice and
    /// not a measurement: the gas beyond the imported volume is whatever the last
    /// plane of it said. True here means at least one collision used that
    /// extrapolation, and it is worth reporting for the same reason the diffusive
    /// mode reports its own fraction - an ion that spends its flight outside the data
    /// was flown through a gas nobody computed.
    /// </remarks>
    public bool SampledOutsideFlow { get; private set; }

    /// <summary>Whether any collision was drawn outside the imported density field.</summary>
    /// <remarks>
    /// The same statement the flow makes, about the other imported quantity. Outside
    /// the box the edge density continues, which for a differentially pumped
    /// instrument is the one place it is most likely to be wrong - the gradient is
    /// steepest at the ends of a pumped region, and continuing the last plane says
    /// the gradient stopped there.
    /// </remarks>
    public bool SampledOutsideDensity { get; private set; }

    /// <summary>Time of the next scheduled collision event, in seconds.</summary>
    public double NextEventSeconds { get; private set; } = double.PositiveInfinity;

    /// <summary>Schedules the first event.</summary>
    /// <param name="nowSeconds">The current flight time.</param>
    /// <param name="speedSi">The ion's current speed.</param>
    public void Start(double nowSeconds, double speedSi) => Schedule(nowSeconds, speedSi);

    /// <summary>
    /// Applies the event due now, and schedules the next one.
    /// </summary>
    /// <param name="nowSeconds">The current flight time.</param>
    /// <param name="position">
    /// Where the ion is, so the neutral is drawn from the gas <em>there</em>. A gas
    /// that flows carries its neutrals with it, and an ion colliding in a jet meets
    /// molecules moving at the jet's speed.
    /// </param>
    /// <param name="velocity">The ion velocity, replaced if the collision is real.</param>
    /// <returns><see langword="true"/> if the ion actually scattered.</returns>
    /// <remarks>
    /// The null-collision method: events are scheduled at a rate that bounds the
    /// true one from above, and each is then accepted with the ratio of the two.
    /// The alternative - integrating the speed-dependent rate along the path to
    /// invert it - is exact in principle and needs the trajectory before it can
    /// tell you where the trajectory bends.
    /// </remarks>
    public bool Collide(double nowSeconds, in Vec3 position, ref Vec3 velocity)
    {
        var scattered = false;

        if (_gas.Flow is { } flow && !flow.Covers(in position))
        {
            SampledOutsideFlow = true;
        }

        if (_gas.Density is { } graded && !graded.Covers(in position))
        {
            SampledOutsideDensity = true;
        }

        if (_gas.Model == CollisionModel.Langevin)
        {
            // The Langevin rate does not contain the speed, so in a uniform gas
            // every scheduled event is a real one and there is no rejection step at
            // all. A graded gas makes the rate position-dependent instead, and the
            // null-collision method is what turns a varying rate into a constant
            // scheduled one plus a thinning - the same mechanism the hard-sphere
            // branch below already runs for a speed-dependent rate, reached a second
            // way.
            //
            // Short-circuited on IsGraded rather than written unconditionally, and
            // that is load-bearing: with a uniform density the thinning would accept
            // with probability exactly one and still consume a random draw, moving
            // every subsequent number in the stream. A seeded run has to be
            // bit-identical to what it was before a pressure field could be declared.
            if (!_gas.IsGraded
                || _random.NextDouble() * _gas.HighestNumberDensitySi
                    <= _gas.NumberDensityAt(in position))
            {
                Scatter(in position, ref velocity);
                Collisions++;
                scattered = true;
            }
            else
            {
                NullEvents++;
            }
        }
        else if (_gas.Model == CollisionModel.HardSphere)
        {
            var neutral = DrawNeutral(in position);
            var relative = (velocity - neutral).Length;

            var bound = Bound(velocity.Length);
            // At the ion, not at the model. Identical to the declared density
            // wherever no field is imported, and the bound below already covers the
            // densest region, so the rejection thins correctly to the local rate.
            var trueRate = _gas.NumberDensityAt(in position) * _gas.CrossSectionSi * relative;

            if (trueRate > bound)
            {
                BoundExceeded = true;
            }

            if (_random.NextDouble() * bound <= trueRate)
            {
                Deflect(ref velocity, neutral);
                Collisions++;
                scattered = true;
            }
            else
            {
                NullEvents++;
            }
        }

        Schedule(nowSeconds, velocity.Length);

        return scattered;
    }

    private void Schedule(double nowSeconds, double speedSi)
    {
        var rate = _gas.Model switch
        {
            CollisionModel.Langevin =>
                _gas.HighestNumberDensitySi * _gas.LangevinRateSi(_ionMass, _charge),
            CollisionModel.HardSphere => Bound(speedSi),
            _ => 0.0,
        };

        if (!(rate > 0.0))
        {
            NextEventSeconds = double.PositiveInfinity;
            return;
        }

        // Inverse transform on the exponential. NextDouble is in [0,1), so the
        // argument of the logarithm is in (0,1] and never zero.
        NextEventSeconds = nowSeconds - (Math.Log(1.0 - _random.NextDouble()) / rate);
    }

    /// <summary>The rate that bounds the true one from above, for the null method.</summary>
    /// <remarks>
    /// Taken at the densest gas anywhere, because an event is scheduled before it is
    /// known where the ion will be when it lands. A rate bounding the densest region
    /// bounds every region; one taken at the declared density would be exceeded
    /// wherever the field is denser than declared, and an exceeded bound biases the
    /// rate low. Equal to the declared density wherever no field is imported.
    /// </remarks>
    private double Bound(double speedSi) =>
        _gas.HighestNumberDensitySi * _gas.CrossSectionSi
        * (speedSi + (ThermalHeadroom * _gas.ThermalSpeedSi));

    /// <summary>Draws one neutral velocity from the Maxwellian, plus the bulk flow there.</summary>
    /// <remarks>
    /// The bulk term is evaluated at the ion's own position rather than taken from a
    /// single declared drift, which is what makes a spatially varying flow visible to
    /// the event-driven models at all. Where the gas declares no flow field this is
    /// the uniform drift and the draw is bit-identical to what it was.
    /// </remarks>
    private Vec3 DrawNeutral(in Vec3 position)
    {
        var sigma = _gas.MassSi > 0.0
            ? Math.Sqrt(BackgroundGas.BoltzmannSi * _gas.TemperatureK / _gas.MassSi)
            : 0.0;

        return new Vec3(Normal() * sigma, Normal() * sigma, Normal() * sigma)
            + _gas.VelocityAt(in position);
    }

    private void Scatter(in Vec3 position, ref Vec3 velocity) =>
        Deflect(ref velocity, DrawNeutral(in position));

    /// <summary>
    /// Elastic scattering off one neutral: isotropic in the centre-of-mass frame.
    /// </summary>
    /// <remarks>
    /// Exact kinematics rather than a drag coefficient. The relative speed is
    /// unchanged - that is what elastic means - and the ion's share of the
    /// centre-of-mass motion is what damps it. Isotropic in the centre of mass is
    /// right for hard spheres by construction, and is the standard treatment of a
    /// Langevin capture, which deflects strongly enough that the incoming direction
    /// is forgotten.
    /// </remarks>
    private void Deflect(ref Vec3 velocity, Vec3 neutral)
    {
        var total = _ionMass + _gas.MassSi;

        if (!(total > 0.0))
        {
            return;
        }

        var centreOfMass = ((velocity * _ionMass) + (neutral * _gas.MassSi)) / total;
        var relative = (velocity - neutral).Length;

        velocity = centreOfMass + (IsotropicDirection() * (relative * _gas.MassSi / total));
    }

    /// <summary>A direction drawn uniformly over the sphere.</summary>
    /// <remarks>
    /// The cosine of the polar angle is uniform, not the angle itself. Drawing the
    /// angle uniformly instead is the classic mistake and concentrates directions at
    /// the poles, which for a damping calculation shows up as too little
    /// randomisation of the transverse motion.
    /// </remarks>
    private Vec3 IsotropicDirection()
    {
        var cosine = (2.0 * _random.NextDouble()) - 1.0;
        var sine = Math.Sqrt(Math.Max(0.0, 1.0 - (cosine * cosine)));
        var azimuth = 2.0 * Math.PI * _random.NextDouble();

        return new Vec3(sine * Math.Cos(azimuth), sine * Math.Sin(azimuth), cosine);
    }

    /// <summary>One standard normal deviate, by Box-Muller.</summary>
    private double Normal()
    {
        var u = 1.0 - _random.NextDouble();
        var v = _random.NextDouble();

        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
    }
}
