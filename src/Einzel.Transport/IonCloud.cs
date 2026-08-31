using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Transport;

/// <summary>
/// Draws the starting states of an ion cloud.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic from its seed, and drawn in a fixed order, so the same study
/// gives the same answer twice. Spec section 8 requires run-to-run
/// reproducibility on one machine, and a statistical result that cannot be
/// compared against itself is not a result.
/// </para>
/// <para>
/// The thermal velocity is added to the directed velocity rather than replacing
/// it. That is the physically right composition - an ion in a source has its
/// thermal motion when the accelerating field arrives, and carries it through -
/// and it is what makes an ion moving backwards at the moment of extraction
/// possible at all.
/// </para>
/// </remarks>
public static class IonCloud
{
    /// <summary>The Boltzmann constant, in joules per kelvin.</summary>
    public const double BoltzmannSi = 1.380649e-23;

    /// <summary>Draws the starting states.</summary>
    /// <param name="nominal">The state a single ion would have started in.</param>
    /// <param name="species">The ion, whose mass sets the thermal velocity.</param>
    /// <param name="settings">How wide the cloud is.</param>
    /// <param name="axis">
    /// Which way is "along" when the nominal state is at rest. Ignored otherwise,
    /// since a moving ion says so itself.
    /// </param>
    /// <returns>One state per ion, in draw order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The ion count or a spread is negative.</exception>
    /// <remarks>
    /// The axis matters for a packet at rest, which is what a pulsed extraction
    /// trap holds. Longitudinal and transverse spread mean nothing without one, and
    /// they are not interchangeable: the spread along the extraction direction
    /// converts to an energy spread and then to arrival time, while the spread
    /// across it does not. Falling back to the x axis would silently swap the two
    /// for any instrument that extracts in another direction.
    /// </remarks>
    public static PhaseState[] Draw(
        in PhaseState nominal, IonSpecies species, IonCloudSettings settings, Vec3? axis = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Ions, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.TemperatureK);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.TransverseSpreadM);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.LongitudinalSpreadM);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.EnergyFractionSpread);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.DivergenceRadians);

        var states = new PhaseState[settings.Ions];
        var random = new Random(settings.Seed);

        var speed = nominal.Velocity.Length;

        var along = speed > 0.0
            ? nominal.Velocity * (1.0 / speed)
            : (axis is { } declared && declared.Length > 0.0 ? declared.Normalized() : new Vec3(1.0, 0.0, 0.0));
        var (acrossA, acrossB) = Perpendiculars(along);

        // Each velocity component of a Maxwell-Boltzmann distribution is Gaussian
        // of this width. Written per component rather than by sampling a speed and
        // then a direction, because the components are what compose with the
        // directed velocity.
        var thermal = settings.TemperatureK > 0.0
            ? Math.Sqrt(BoltzmannSi * settings.TemperatureK / species.MassSi)
            : 0.0;

        for (var k = 0; k < settings.Ions; k++)
        {
            var position = nominal.Position
                + (acrossA * (settings.TransverseSpreadM * Gaussian(random)))
                + (acrossB * (settings.TransverseSpreadM * Gaussian(random)))
                + (along * (settings.LongitudinalSpreadM * Gaussian(random)));

            // Energy scales as the square of speed, so a fractional energy offset
            // is a square root in velocity. Getting this wrong is a factor of two
            // in the linear term and four in the quadratic, and it has been got
            // wrong in this codebase before.
            var fraction = settings.EnergyFractionSpread > 0.0
                ? settings.EnergyFractionSpread * Gaussian(random)
                : 0.0;

            var scaled = speed * Math.Sqrt(Math.Max(0.0, 1.0 + fraction));

            // A tilt rather than an added transverse velocity, so the speed - and
            // therefore the energy - is exactly unchanged. That is the whole point of
            // having this beside a temperature: an aperture selects directions and
            // takes nothing out of the beam energy, while a thermal draw moves both
            // together in a fixed ratio.
            //
            // Uniform in solid angle inside the cone: cos(theta) uniform between the
            // axis and the cone edge, azimuth uniform. Sampling theta uniformly would
            // pile the rays onto the axis and understate the aberration a cone is
            // declared to probe.
            var directed = along * scaled;

            if (settings.DivergenceRadians > 0.0)
            {
                var cosMax = Math.Cos(settings.DivergenceRadians);
                var cosTheta = 1.0 - (random.NextDouble() * (1.0 - cosMax));
                var sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - (cosTheta * cosTheta)));
                var azimuth = 2.0 * Math.PI * random.NextDouble();

                directed = ((along * cosTheta)
                    + (acrossA * (sinTheta * Math.Cos(azimuth)))
                    + (acrossB * (sinTheta * Math.Sin(azimuth)))) * scaled;
            }

            var velocity = directed
                + (along * (thermal * Gaussian(random)))
                + (acrossA * (thermal * Gaussian(random)))
                + (acrossB * (thermal * Gaussian(random)));

            states[k] = new PhaseState(position, velocity);
        }

        return states;
    }

    /// <summary>
    /// The width of the arrival-time peak a thermal cloud produces in a uniform
    /// extraction field, from the closed form.
    /// </summary>
    /// <param name="species">The ion.</param>
    /// <param name="temperature">Source temperature.</param>
    /// <param name="fieldStrength">Extraction field.</param>
    /// <returns>Full width at half maximum.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The field strength is not positive.</exception>
    /// <remarks>
    /// <para>
    /// Turn-around time, which is what limits a pulsed extraction trap or an
    /// orthogonal accelerator. An ion moving away from the detector when the field
    /// arrives must be stopped and brought back, and it reaches the starting line
    /// later than an ion that was moving toward the detector by 2mv/qE.
    /// </para>
    /// <para>
    /// Over a thermal distribution the arrival times are Gaussian of width
    /// sqrt(mkT)/qE, so the peak's full width at half maximum is
    /// 2 sqrt(2 ln 2) sqrt(mkT) / qE. Nothing about the flight after extraction
    /// enters it: the spread is imposed before the ion leaves.
    /// </para>
    /// <para>
    /// Provided so the ensemble can be checked against something exact rather than
    /// against itself. For m/z 500 at 300 K in a field of 1 kV/mm it gives 0.86 ns,
    /// which is inside the 0.8 to 1.2 ns the Ion Processor paper reports across
    /// m/z 195 to 2722.
    /// </para>
    /// </remarks>
    public static Quantity TurnAroundFwhm(IonSpecies species, Quantity temperature, Quantity fieldStrength)
    {
        var field = fieldStrength.SiValue;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(field);

        var seconds = 2.0 * Math.Sqrt(2.0 * Math.Log(2.0))
            * Math.Sqrt(species.MassSi * BoltzmannSi * temperature.SiValue)
            / (Math.Abs(species.ChargeSi) * field);

        return Quantity.Si(seconds, Quantity.From(1.0, "s").Dimension);
    }

    /// <summary>Two unit vectors perpendicular to a direction, and to each other.</summary>
    /// <remarks>
    /// The seed vector is chosen away from the direction rather than fixed, so a
    /// beam travelling along x does not produce a degenerate cross product. A
    /// cloud silently collapsing to a line for one axis of travel and not another
    /// is the sort of thing that would present as a transmission difference.
    /// </remarks>
    private static (Vec3 A, Vec3 B) Perpendiculars(in Vec3 along)
    {
        var seed = Math.Abs(along.X) < 0.9 ? new Vec3(1.0, 0.0, 0.0) : new Vec3(0.0, 1.0, 0.0);

        var a = Vec3.Cross(along, seed);
        a *= 1.0 / a.Length;

        return (a, Vec3.Cross(along, a));
    }

    /// <summary>A standard normal deviate, by the polar Box-Muller method.</summary>
    private static double Gaussian(Random random)
    {
        double u, v, s;

        do
        {
            u = (2.0 * random.NextDouble()) - 1.0;
            v = (2.0 * random.NextDouble()) - 1.0;
            s = (u * u) + (v * v);
        }
        while (s is <= 0.0 or >= 1.0);

        return u * Math.Sqrt(-2.0 * Math.Log(s) / s);
    }
}
