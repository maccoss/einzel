using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Analysis;

/// <summary>
/// The slow motion of an ion in a device where a fast oscillation and a slow one
/// separate, described by the effective potential the slow motion feels.
/// </summary>
/// <param name="TurningPoint">
/// Where the slow motion reverses, in metres along the slow coordinate, measured
/// from the launch point.
/// </param>
/// <param name="HalfPeriod">
/// Time to reach the turning point, in seconds. Half the slow period, by the
/// symmetry of a conservative one-dimensional motion.
/// </param>
/// <param name="Bracketed">
/// False when the slow energy exceeds the effective potential everywhere in the
/// range searched, so there is no turning point and the ion escapes. The other
/// fields are then meaningless.
/// </param>
public sealed record SlowMotion(double TurningPoint, double HalfPeriod, bool Bracketed);

/// <summary>
/// Separates a fast oscillation from a slow drift and describes the slow motion
/// through the effective potential it feels.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> Several devices in spec section 1's table work by a
/// timescale hierarchy: an ion crosses a structure many times while advancing
/// slowly along it. In an asymmetric-track multi-reflection analyzer the ion
/// reflects between two mirrors thousands of times while drifting along their
/// length; in an ion funnel or a travelling-wave guide it quivers at the drive
/// frequency while drifting axially. In each case the slow motion does not feel
/// the field at a point, it feels the field <b>averaged over the fast orbit</b>,
/// and the useful description is a one-dimensional conservative motion in that
/// average.
/// </para>
/// <para>
/// <b>Why it belongs in the engine rather than in a study.</b> The alternative is
/// to fly the ion, and flying is between three and five orders of magnitude
/// dearer: a drift period of four hundred microseconds takes seconds of wall
/// clock per evaluation, where the quadrature here takes under a millisecond. For
/// the multi-reflection analyzer that difference decides whether the drift
/// electrode's shape can be optimised at all. It also decides what can be
/// asserted: measured against the published design of the Thermo Astral
/// analyzer, the quadrature reproduces an isochronicity of one part in a million
/// where the paper claims two, and a flight of the same configuration agrees with
/// the quadrature rather than with the paper - which located the model's error in
/// the drift electrode's discretisation rather than in the mesh or the
/// integrator. See <c>docs/astral-log.md</c> sections 47 to 50.
/// </para>
/// <para>
/// <b>The approximation, stated.</b> The separation assumes the fast orbit is
/// unchanged as the ion advances, which is exact only where the structure is
/// invariant along the slow coordinate and approximate where it is not. The
/// action of the fast motion is then an adiabatic invariant, conserved to the
/// extent the slow motion is slow. Nothing here measures that assumption, and a
/// caller who needs it checked should compare against a flight - which is
/// precisely the comparison this class makes affordable.
/// </para>
/// <para>
/// <b>The square-root singularity is removed rather than integrated through.</b>
/// The time to the turning point is the integral of dz over the slow speed, and
/// the slow speed vanishes there, so the integrand diverges as one over the
/// square root of the distance remaining. Substituting z = z_r (1 - u^2) makes the
/// Jacobian vanish at the same rate and the integrand finite, which is why a
/// midpoint rule converges here instead of losing half its digits at the endpoint.
/// </para>
/// </remarks>
public static class AdiabaticDrift
{
    /// <summary>
    /// The effective potential a slow motion feels: the applied potential averaged
    /// over one period of the fast orbit, sampled along the slow coordinate.
    /// </summary>
    /// <param name="potentialAt">
    /// Potential in volts at a point, given a fast-orbit sample and a position
    /// along the slow coordinate in metres.
    /// </param>
    /// <param name="orbit">
    /// One period of the fast orbit as weights summing to one. The weight of a
    /// sample is the fraction of the fast period spent there, so an orbit sampled
    /// at uniform time carries equal weights and one sampled at uniform arc length
    /// does not.
    /// </param>
    /// <param name="slowPositions">Positions along the slow coordinate, in metres.</param>
    /// <returns>Effective potential in volts, one per slow position.</returns>
    /// <exception cref="ArgumentException">
    /// The orbit is empty, or its weights do not sum to one within a part in a
    /// thousand. A weight sum that is not one is a fast average that is not an
    /// average, and it scales the whole effective potential silently.
    /// </exception>
    public static double[] EffectivePotential<TSample>(
        Func<TSample, double, double> potentialAt,
        IReadOnlyList<(TSample Sample, double Weight)> orbit,
        IReadOnlyList<double> slowPositions)
    {
        ArgumentNullException.ThrowIfNull(potentialAt);
        ArgumentNullException.ThrowIfNull(orbit);
        ArgumentNullException.ThrowIfNull(slowPositions);
        if (orbit.Count == 0)
        {
            throw new ArgumentException("the fast orbit has no samples", nameof(orbit));
        }

        var total = 0.0;
        foreach (var (_, w) in orbit)
        {
            total += w;
        }

        if (Math.Abs(total - 1.0) > 1e-3)
        {
            throw new ArgumentException(
                $"the fast-orbit weights sum to {total:R} rather than 1. They are the "
                + "fraction of the fast period spent at each sample, so a sum that is not "
                + "one scales the effective potential by that factor.",
                nameof(orbit));
        }

        var profile = new double[slowPositions.Count];
        for (var i = 0; i < slowPositions.Count; i++)
        {
            var z = slowPositions[i];
            var sum = 0.0;
            foreach (var (sample, weight) in orbit)
            {
                sum += weight * potentialAt(sample, z);
            }

            profile[i] = sum;
        }

        return profile;
    }

    /// <summary>
    /// The slow motion in an effective potential: where it turns and how long it
    /// takes to get there.
    /// </summary>
    /// <param name="effectivePotentialVolts">
    /// Effective potential in volts at a position along the slow coordinate in
    /// metres. Only differences from the launch point matter.
    /// </param>
    /// <param name="slowEnergyVolts">
    /// Kinetic energy of the slow motion at launch, in volts. Positive.
    /// </param>
    /// <param name="chargeToMass">Charge to mass ratio in coulombs per kilogram.</param>
    /// <param name="searchTo">
    /// Far end of the range searched for a turning point, in metres. A slow motion
    /// that does not turn inside it is reported as unbracketed rather than
    /// extrapolated.
    /// </param>
    /// <param name="intervals">
    /// Quadrature intervals. The default is ample: the substitution removes the
    /// endpoint singularity, so the rule converges at its nominal order.
    /// </param>
    public static SlowMotion Motion(
        Func<double, double> effectivePotentialVolts,
        double slowEnergyVolts,
        double chargeToMass,
        double searchTo,
        int intervals = 20_000)
    {
        ArgumentNullException.ThrowIfNull(effectivePotentialVolts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slowEnergyVolts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chargeToMass);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(searchTo);
        ArgumentOutOfRangeException.ThrowIfLessThan(intervals, 16);

        var reference = effectivePotentialVolts(0.0);

        // remaining slow kinetic energy, in volts, at a position
        double Remaining(double z) => slowEnergyVolts - (effectivePotentialVolts(z) - reference);

        if (Remaining(0.0) <= 0.0)
        {
            return new SlowMotion(0.0, 0.0, Bracketed: false);
        }

        if (Remaining(searchTo) > 0.0)
        {
            return new SlowMotion(searchTo, 0.0, Bracketed: false);
        }

        var lo = 0.0;
        var hi = searchTo;
        for (var i = 0; i < 200; i++)
        {
            var mid = 0.5 * (lo + hi);
            if (Remaining(mid) > 0.0)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var turning = lo;

        // t = integral dz / v(z), v = sqrt(2 q/m (E - phi)). The substitution
        // z = turning (1 - u^2) gives dz = -2 turning u du, and u vanishes where the
        // speed does, so the integrand is finite at the endpoint.
        var time = 0.0;
        for (var i = 0; i < intervals; i++)
        {
            var u = (i + 0.5) / intervals;
            var v = Remaining(turning * (1.0 - (u * u)));
            if (v > 0.0)
            {
                time += 2.0 * turning * u / Math.Sqrt(2.0 * chargeToMass * v) / intervals;
            }
        }

        return new SlowMotion(turning, time, Bracketed: true);
    }

    /// <summary>
    /// The arrival-time error a slow drift accumulates, which is not the same
    /// quantity as the slow period and can differ from it by six orders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists and why the period is the wrong thing to ask for.</b> In a
    /// multi-reflection analyzer the ion makes a whole number of fast oscillations and lands;
    /// the flight time is that count times the nominal oscillation period, plus the small
    /// perturbation the slow motion accumulates along the way. The <i>slow period</i> decides
    /// how many oscillations happen. The <i>accumulated perturbation</i> decides when the ion
    /// arrives. Measured on the Thermo Astral's published design the two differ by a factor
    /// of 1.7 million over the same range - the slow period varies by a per cent while the
    /// arrival time is flat to parts per billion. See <c>docs/astral-log.md</c>
    /// section 53, and note that four earlier sections of that document measured the first
    /// and reported it as the second.
    /// </para>
    /// <para>
    /// <b>The load-bearing detail is that the two contributions enter with opposite signs.</b>
    /// Where two mechanisms both retard the slow motion - in that instrument, a biased
    /// electrode the ion crosses and a convergence of the mirrors - the slow potential is
    /// their <b>sum</b>, but the timing integrand is their <b>difference</b>: a retarding
    /// electrode lengthens each oscillation while a shortened path shortens it. That
    /// opposition is what allows a cancellation, and a caller who passes the sum for both
    /// gets a number that looks reasonable and is meaningless.
    /// </para>
    /// </remarks>
    /// <param name="retardingVolts">
    /// The part of the effective potential that <i>lengthens</i> the fast oscillation, in
    /// volts at a position along the slow coordinate in metres.
    /// </param>
    /// <param name="hasteningVolts">
    /// The part that <i>shortens</i> it. The slow motion feels the sum of the two; the
    /// arrival time feels the difference.
    /// </param>
    /// <param name="slowEnergyVolts">Slow kinetic energy at launch, in volts.</param>
    /// <param name="chargeToMass">Charge to mass ratio in coulombs per kilogram.</param>
    /// <param name="searchTo">Far end of the turning-point search, in metres.</param>
    /// <param name="intervals">Quadrature intervals.</param>
    /// <param name="accelerationVolts">
    /// The potential the ion was accelerated through. It enters because the perturbation to
    /// the fast period is the energy derivative of the action perturbation, and where the ion
    /// is field-free the action goes as the inverse square root of energy - so that derivative
    /// is the perturbation itself over the beam energy. A device whose slow-motion electrode
    /// sits somewhere the ion is <i>not</i> field-free needs a different factor, and this
    /// routine would then be wrong rather than approximate.
    /// </param>
    /// <returns>
    /// The arrival-time error in seconds, or null when the slow motion does not turn inside
    /// the range. What is almost always wanted is how much this <i>changes</i> across a range
    /// of slow energy - see <see cref="ArrivalSpread"/>.
    /// </returns>
    public static double? ArrivalTimeError(
        Func<double, double> retardingVolts,
        Func<double, double> hasteningVolts,
        double slowEnergyVolts,
        double chargeToMass,
        double accelerationVolts,
        double searchTo,
        int intervals = 20_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accelerationVolts);
        ArgumentNullException.ThrowIfNull(retardingVolts);
        ArgumentNullException.ThrowIfNull(hasteningVolts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slowEnergyVolts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(searchTo);
        ArgumentOutOfRangeException.ThrowIfLessThan(intervals, 16);

        double Total(double z) => retardingVolts(z) + hasteningVolts(z);

        var reference = Total(0.0);
        double Remaining(double z) => slowEnergyVolts - (Total(z) - reference);

        if (Remaining(0.0) <= 0.0 || Remaining(searchTo) > 0.0)
        {
            return null;
        }

        var lo = 0.0;
        var hi = searchTo;
        for (var i = 0; i < 200; i++)
        {
            var mid = 0.5 * (lo + hi);
            if (Remaining(mid) > 0.0)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var turning = lo;
        var total = 0.0;
        for (var i = 0; i < intervals; i++)
        {
            var u = (i + 0.5) / intervals;
            var z = turning * (1.0 - (u * u));
            var v = Remaining(z);
            if (v > 0.0)
            {
                var weight = 2.0 * turning * u / Math.Sqrt(2.0 * chargeToMass * v) / intervals;
                total += weight * (retardingVolts(z) - hasteningVolts(z));
            }
        }

        return total / accelerationVolts;
    }

    /// <summary>
    /// How much the arrival time moves across a range of slow amplitude, in seconds.
    /// </summary>
    /// <remarks>
    /// Absolute rather than relative, and deliberately: the useful comparison is against the
    /// <i>flight time</i>, not against the arrival-time error itself. Dividing by the latter
    /// gives a number three to four orders larger that means nothing - which is the mistake
    /// the first version of this API made.
    /// Null when any sample fails to turn inside the range, for the same reason
    /// <see cref="Isochronicity"/> is: a spread over the subset that turned is a spread over
    /// a different population.
    /// </remarks>
    /// <param name="retardingVolts">As for <see cref="ArrivalTimeError"/>.</param>
    /// <param name="hasteningVolts">As for <see cref="ArrivalTimeError"/>.</param>
    /// <param name="slowEnergyVolts">Slow kinetic energy at nominal, in volts.</param>
    /// <param name="chargeToMass">Charge to mass ratio in coulombs per kilogram.</param>
    /// <param name="searchTo">Far end of the turning-point search, in metres.</param>
    /// <param name="energyFraction">Half-range of slow energy scanned, as a fraction.</param>
    /// <param name="samples">Odd number of samples, at least three.</param>
    /// <param name="accelerationVolts">As for <see cref="ArrivalTimeError"/>.</param>
    public static double? ArrivalSpread(
        Func<double, double> retardingVolts,
        Func<double, double> hasteningVolts,
        double slowEnergyVolts,
        double chargeToMass,
        double accelerationVolts,
        double searchTo,
        double energyFraction = 0.10,
        int samples = 9)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(energyFraction);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 3);
        if (samples % 2 == 0)
        {
            throw new ArgumentException(
                "an even number of samples has no point at nominal", nameof(samples));
        }

        var lowest = double.MaxValue;
        var highest = double.MinValue;
        var nominal = 0.0;
        for (var i = 0; i < samples; i++)
        {
            var f = 1.0 + (energyFraction * ((2.0 * i / (samples - 1)) - 1.0));
            var v = ArrivalTimeError(
                retardingVolts, hasteningVolts, slowEnergyVolts * f, chargeToMass,
                accelerationVolts, searchTo);
            if (v is null)
            {
                return null;
            }

            lowest = Math.Min(lowest, v.Value);
            highest = Math.Max(highest, v.Value);
            if (i == (samples - 1) / 2)
            {
                nominal = v.Value;
            }
        }

        _ = nominal;
        return highest - lowest;
    }

    /// <summary>
    /// How nearly the slow period is independent of the slow amplitude, which is
    /// what an isochronous drift means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported as the spread in half period over the fractional range asked for,
    /// relative to the half period at nominal. The envelope carries that spread as
    /// its uncertainty, per GRD-1, because the quantity being asked about <b>is</b>
    /// a spread: a single number for it would be the nominal period, which is not
    /// the answer to this question.
    /// </para>
    /// <para>
    /// A harmonic effective potential is exactly isochronous, so it returns zero to
    /// rounding whatever the amplitude range - which makes it the sharp test of this
    /// routine rather than of any device.
    /// </para>
    /// </remarks>
    /// <param name="effectivePotentialVolts">As for <see cref="Motion"/>.</param>
    /// <param name="slowEnergyVolts">Slow kinetic energy at nominal, in volts.</param>
    /// <param name="chargeToMass">Charge to mass ratio in coulombs per kilogram.</param>
    /// <param name="searchTo">Far end of the turning-point search, in metres.</param>
    /// <param name="energyFraction">
    /// Half-range of slow energy scanned, as a fraction of nominal. The drift
    /// energy goes as the square of the injection angle, so a fractional range in
    /// angle is twice this.
    /// </param>
    /// <param name="samples">Odd number of slow energies, at least three.</param>
    /// <returns>
    /// The half period at nominal in seconds, with the spread over the range as its
    /// uncertainty. Null when any sample fails to turn inside the range, since a
    /// spread over the subset that turned is a spread over a different population.
    /// </returns>
    public static Measured? Isochronicity(
        Func<double, double> effectivePotentialVolts,
        double slowEnergyVolts,
        double chargeToMass,
        double searchTo,
        double energyFraction = 0.02,
        int samples = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(energyFraction);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 3);
        if (samples % 2 == 0)
        {
            throw new ArgumentException(
                "an even number of samples has no point at nominal, and the spread is "
                + "reported relative to it",
                nameof(samples));
        }

        var lowest = double.MaxValue;
        var highest = double.MinValue;
        var nominal = 0.0;
        for (var i = 0; i < samples; i++)
        {
            var f = 1.0 + (energyFraction * ((2.0 * i / (samples - 1)) - 1.0));
            var m = Motion(effectivePotentialVolts, slowEnergyVolts * f, chargeToMass, searchTo);
            if (!m.Bracketed)
            {
                return null;
            }

            lowest = Math.Min(lowest, m.HalfPeriod);
            highest = Math.Max(highest, m.HalfPeriod);
            if (i == (samples - 1) / 2)
            {
                nominal = m.HalfPeriod;
            }
        }

        var quantity = Quantity.Si(nominal, Dimension.TimeDimension);
        return new Measured(
            quantity,
            UncertaintyInterval.Symmetric(
                quantity,
                Quantity.Si(0.5 * (highest - lowest), Dimension.TimeDimension),
                confidenceLevel: 1.0),
            new Evidence.Search(samples, Converged: true, SpreadSi: highest - lowest));
    }
}
