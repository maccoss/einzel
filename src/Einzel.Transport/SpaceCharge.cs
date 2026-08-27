using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Transport;

/// <summary>
/// How much the ions in a packet push on each other, and whether ignoring it
/// matters.
/// </summary>
/// <param name="Population">Ions in the physical packet.</param>
/// <param name="EffectiveRadiusM">The radius of the uniform sphere the packet was modelled as.</param>
/// <param name="PotentialVolts">Centre-to-surface potential difference across the packet.</param>
/// <param name="EnergyFraction">That potential as a fraction of the beam energy.</param>
/// <param name="TimingFraction">
/// The flight-time error it implies, as a fraction of the flight time.
/// </param>
public sealed record SpaceChargeEstimate(
    int Population,
    double EffectiveRadiusM,
    double PotentialVolts,
    double? EnergyFraction,
    double? TimingFraction)
{
    /// <summary>
    /// Whether the fractions could be computed at all.
    /// </summary>
    /// <remarks>
    /// They are fractions of the beam energy, so a packet with no beam energy has
    /// none - a packet at rest, before the instrument has accelerated it. The
    /// self-potential and the effective radius are still real and still reported;
    /// it is only the conversion to a fractional error that has no denominator.
    /// </remarks>
    public bool IsScaled => TimingFraction is not null;

    /// <summary>Whether the packet has no spatial extent to spread its charge over.</summary>
    /// <remarks>
    /// More than one ion at a single point is not a small error, it is an infinite
    /// one, and it is easy to write by declaring a population without any spread.
    /// </remarks>
    public bool IsPointLike => EffectiveRadiusM <= 0.0 && Population > 1;
}

/// <summary>
/// A screening estimate of space charge, for deciding whether ignoring it is safe.
/// </summary>
/// <remarks>
/// <para>
/// The engine flies every ion through a field that does not know about the other
/// ions. That is exactly right for a sparse beam and wrong for a dense packet, and
/// the difference is invisible in the answer - which is the case this exists to
/// make visible. Spec section 7 asks the engine to compute the governing
/// dimensionless numbers and raise a non-suppressible warning when the model
/// selected is outside its validity; this is that number for space charge.
/// </para>
/// <para>
/// Deliberately an estimate rather than a simulation. It costs no trajectories, it
/// is arithmetic on the declared packet, and it is only ever used to say "this
/// matters, and the engine is not modelling it". A real treatment advances every
/// ion together and recomputes their shared field each step, which is Phase 3 and
/// a different program.
/// </para>
/// <para>
/// It is also deliberately <em>conservative</em>. The energy spread is turned into
/// a flight-time error as though the ion were in free flight, where time goes as
/// the inverse square root of energy. An instrument at a first-order energy focus
/// suppresses that to second order - measured on the shipped reflectron, where a
/// sixteenfold temperature gave a sixteenfold width rather than a fourfold one -
/// so the real error is smaller than this says. Over-warning is the right
/// direction for a screen whose whole purpose is to stop a silent one.
/// </para>
/// </remarks>
public static class SpaceCharge
{
    /// <summary>Vacuum permittivity, in farads per metre.</summary>
    public const double PermittivitySi = 8.8541878128e-12;

    /// <summary>
    /// Estimates the self-field effect of a declared packet.
    /// </summary>
    /// <param name="settings">The cloud, whose population and spreads describe the packet.</param>
    /// <param name="species">The ion, for its charge.</param>
    /// <param name="accelerationPotentialVolts">The beam energy, as the potential it was accelerated through.</param>
    /// <param name="flightTimeSeconds">
    /// How long the packet flies. The dominant mechanism is expansion under the
    /// packet's own charge, which goes on for as long as the flight does; zero
    /// means it is not known, and the estimate falls back to a much looser bound.
    /// </param>
    /// <returns>The estimate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The packet is modelled as a uniform sphere carrying the whole charge, whose
    /// centre sits above its surface by Nq/(8 pi eps0 R). That is an
    /// order-of-magnitude screen and is meant to be: a real packet is neither
    /// uniform nor spherical, and getting the shape factor right would be
    /// precision spent on a number whose only job is to cross a threshold.
    /// </para>
    /// <para>
    /// The radius comes from the declared spreads by matching root-mean-square
    /// radii: a Gaussian cloud of widths sigma has r_rms = sqrt(2 sigma_t^2 +
    /// sigma_l^2), and a uniform sphere of radius R has r_rms = sqrt(3/5) R.
    /// Matching them keeps the estimate tied to the geometry the model actually
    /// declared rather than to a diameter someone chose.
    /// </para>
    /// </remarks>
    public static SpaceChargeEstimate Estimate(
        IonCloudSettings settings,
        IonSpecies species,
        double accelerationPotentialVolts,
        double flightTimeSeconds = 0.0)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var population = settings.Population ?? settings.Ions;

        var rootMeanSquare = Math.Sqrt(
            (2.0 * settings.TransverseSpreadM * settings.TransverseSpreadM)
            + (settings.LongitudinalSpreadM * settings.LongitudinalSpreadM));

        var radius = Math.Sqrt(5.0 / 3.0) * rootMeanSquare;

        if (population <= 1 || radius <= 0.0)
        {
            return new SpaceChargeEstimate(population, radius, 0.0, 0.0, 0.0);
        }

        var potential = population * Math.Abs(species.ChargeSi)
            / (8.0 * Math.PI * PermittivitySi * radius);

        // Absent rather than infinite when there is no energy to be a fraction of.
        // An infinity is not a large number here, it is a missing one - and it
        // reaches a JSON serialiser that cannot write either, which is how this
        // class of bug has surfaced three times in this codebase now.
        if (Math.Abs(accelerationPotentialVolts) <= 0.0)
        {
            return new SpaceChargeEstimate(population, radius, potential, null, null);
        }

        var energyFraction = potential / Math.Abs(accelerationPotentialVolts);
        var beamSpeed = Math.Sqrt(
            2.0 * Math.Abs(species.ChargeSi) * Math.Abs(accelerationPotentialVolts) / species.MassSi);

        return new SpaceChargeEstimate(
            population, radius, potential, energyFraction,
            ExplosionTimingFraction(potential, radius, species, beamSpeed, flightTimeSeconds));
    }

    /// <summary>
    /// The fractional flight-time error a packet's own charge implies, by the
    /// mechanism that actually dominates.
    /// </summary>
    /// <param name="potentialVolts">The packet's centre-to-surface self-potential.</param>
    /// <param name="radiusM">The packet's effective radius.</param>
    /// <param name="species">The ion, for its charge and mass.</param>
    /// <param name="beamSpeedSi">How fast the packet is travelling.</param>
    /// <param name="flightTimeSeconds">How long it flies, or zero if not known.</param>
    /// <returns>The fraction of the flight time.</returns>
    /// <remarks>
    /// <para>
    /// <b>This used to be half the energy fraction, and that was wrong by more than
    /// two orders of magnitude.</b> The reasoning was that the self-potential is an
    /// energy spread and time goes as the inverse square root of energy, so the
    /// timing error is half of it. That describes a real mechanism — ions extracted
    /// from different depths of the self-potential well leave with different
    /// energies — and it is not the one that dominates in flight.
    /// </para>
    /// <para>
    /// What dominates is that <b>the packet expands</b>. The self-field keeps
    /// pushing for the whole drift, and the relative speed it imparts is set by
    /// converting the self-potential into <em>relative</em> kinetic energy —
    /// sqrt(2 q phi / m) — not by perturbing a beam energy thousands of times
    /// larger. For a 40,000-ion packet of 0.5 mm at 4 kV the two differ by 527
    /// times, and the direct pairwise sum agrees with the larger one. Found by
    /// building the direct sum SC-1 asks for and comparing, which is exactly what
    /// that requirement is for.
    /// </para>
    /// <para>
    /// Two regimes, and the smaller wins. Over a short drift the packet has not had
    /// time to expand, so the relative speed is the surface acceleration times the
    /// flight time. Over a long one it saturates at the escape value. Taking the
    /// minimum keeps a sparse packet on a short flight from being reported as
    /// catastrophic, which the escape value alone would do: two ions half a
    /// millimetre apart reach 1 m/s of relative speed <em>eventually</em>, and
    /// eventually is 200 times the flight.
    /// </para>
    /// <para>
    /// With no flight time known there is nothing to be short compared to, so the
    /// escape value is used. It is a true upper bound and a very loose one, and the
    /// callers that matter — a run, a study — all know their flight time.
    /// </para>
    /// </remarks>
    public static double ExplosionTimingFraction(
        double potentialVolts,
        double radiusM,
        IonSpecies species,
        double beamSpeedSi,
        double flightTimeSeconds = 0.0)
    {
        if (potentialVolts <= 0.0 || radiusM <= 0.0 || beamSpeedSi <= 0.0)
        {
            return 0.0;
        }

        var charge = Math.Abs(species.ChargeSi);

        // The field at the surface of a uniformly charged sphere is twice the
        // centre-to-surface potential over the radius.
        var acceleration = 2.0 * charge * potentialVolts / (species.MassSi * radiusM);

        var escape = Math.Sqrt(2.0 * charge * potentialVolts / species.MassSi);

        var relative = flightTimeSeconds > 0.0
            ? Math.Min(acceleration * flightTimeSeconds, escape)
            : escape;

        return relative / beamSpeedSi;
    }

    /// <summary>
    /// How many ions a packet of a given size can hold before space charge reaches
    /// a stated flight-time budget.
    /// </summary>
    /// <param name="radius">Packet radius.</param>
    /// <param name="species">The ion, for its charge.</param>
    /// <param name="accelerationPotentialVolts">The beam energy.</param>
    /// <param name="timingFraction">The flight-time error to solve for; ACC-1 is 1e-6.</param>
    /// <param name="flightTimeSeconds">How long the packet flies, or zero if not known.</param>
    /// <returns>The population at which the budget is reached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The radius or the budget is not positive.</exception>
    /// <remarks>
    /// The inverse of <see cref="Estimate"/>, provided because it is the more
    /// useful direction to be told: "this packet is over budget" invites the
    /// question "by how much can I load it", and an error message that answers its
    /// own follow-up is worth more than one that does not.
    /// </remarks>
    public static double PopulationLimit(
        Quantity radius,
        IonSpecies species,
        double accelerationPotentialVolts,
        double timingFraction,
        double flightTimeSeconds = 0.0)
    {
        var metres = radius.SiValue;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timingFraction);

        var charge = Math.Abs(species.ChargeSi);
        var beamSpeed = Math.Sqrt(
            2.0 * charge * Math.Abs(accelerationPotentialVolts) / species.MassSi);

        if (beamSpeed <= 0.0)
        {
            return 0.0;
        }

        var relative = timingFraction * beamSpeed;

        // Escape branch: relative = sqrt(2 q phi / m).
        var fromEscape = relative * relative * species.MassSi / (2.0 * charge);

        // Linear branch: relative = 2 q phi T / (m r).
        var fromLinear = relative * species.MassSi * metres / (2.0 * charge * flightTimeSeconds);

        // The larger, not the smaller. The forward estimate takes the minimum of
        // the two mechanisms, and min(a, b) is within budget as soon as *either*
        // is - so the population that satisfies it is the larger of the two
        // inversions. Taking the minimum here looked symmetric with the forward
        // direction and reported a limit of three thousandths of an ion.
        var potential = flightTimeSeconds > 0.0
            ? Math.Max(fromEscape, fromLinear)
            : fromEscape;

        return potential * 8.0 * Math.PI * PermittivitySi * metres / charge;
    }
}
