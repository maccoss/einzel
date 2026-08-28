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
    /// <exception cref="Core.Errors.EinzelException">
    /// The gas carries a flow field, which this sampler has no position to evaluate.
    /// </exception>
    public CollisionSampler(BackgroundGas gas, double ionMassSi, double chargeSi, int seed)
    {
        ArgumentNullException.ThrowIfNull(gas);

        // A flow field is a velocity at a place, and a collision here is scheduled
        // and drawn without one - Collide takes a time and a velocity. Refused
        // rather than evaluated at some convenient point, because the failure would
        // otherwise be a run that used the uniform drift and said nothing: the ion
        // would fly through a declared jet as though the gas were standing still,
        // which is the exact mistake GAS-1 exists to prevent.
        if (gas.Flow is not null)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.RegimeInvalid,
                Path = "/transport/gas/flow",
                Constraint = "the event-driven collision models sample a neutral velocity without "
                    + "a position, so they cannot see a gas flow that varies with position",
                Suggestion = "declare a uniform 'driftVelocity' instead, or use the diffusive "
                    + "transport mode, which samples the flow on its own grid",
            });
        }

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
    /// <param name="velocity">The ion velocity, replaced if the collision is real.</param>
    /// <returns><see langword="true"/> if the ion actually scattered.</returns>
    /// <remarks>
    /// The null-collision method: events are scheduled at a rate that bounds the
    /// true one from above, and each is then accepted with the ratio of the two.
    /// The alternative - integrating the speed-dependent rate along the path to
    /// invert it - is exact in principle and needs the trajectory before it can
    /// tell you where the trajectory bends.
    /// </remarks>
    public bool Collide(double nowSeconds, ref Vec3 velocity)
    {
        var scattered = false;

        if (_gas.Model == CollisionModel.Langevin)
        {
            // No rejection step at all: the Langevin rate does not contain the
            // speed, so every scheduled event is a real one.
            Scatter(ref velocity);
            Collisions++;
            scattered = true;
        }
        else if (_gas.Model == CollisionModel.HardSphere)
        {
            var neutral = DrawNeutral();
            var relative = (velocity - neutral).Length;

            var bound = Bound(velocity.Length);
            var trueRate = _gas.NumberDensitySi * _gas.CrossSectionSi * relative;

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
            CollisionModel.Langevin => _gas.NumberDensitySi * _gas.LangevinRateSi(_ionMass, _charge),
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
    private double Bound(double speedSi) =>
        _gas.NumberDensitySi * _gas.CrossSectionSi
        * (speedSi + (ThermalHeadroom * _gas.ThermalSpeedSi));

    /// <summary>Draws one neutral velocity from the Maxwellian, plus any bulk drift.</summary>
    private Vec3 DrawNeutral()
    {
        var sigma = _gas.MassSi > 0.0
            ? Math.Sqrt(BackgroundGas.BoltzmannSi * _gas.TemperatureK / _gas.MassSi)
            : 0.0;

        return new Vec3(Normal() * sigma, Normal() * sigma, Normal() * sigma) + _gas.DriftVelocitySi;
    }

    private void Scatter(ref Vec3 velocity) => Deflect(ref velocity, DrawNeutral());

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
