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
/// The flight-time error it implies for free flight, which is half the energy
/// fraction because time goes as the inverse square root of energy.
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
        IonCloudSettings settings, IonSpecies species, double accelerationPotentialVolts)
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

        return new SpaceChargeEstimate(population, radius, potential, energyFraction, 0.5 * energyFraction);
    }

    /// <summary>
    /// How many ions a packet of a given size can hold before space charge reaches
    /// a stated flight-time budget.
    /// </summary>
    /// <param name="radius">Packet radius.</param>
    /// <param name="species">The ion, for its charge.</param>
    /// <param name="accelerationPotentialVolts">The beam energy.</param>
    /// <param name="timingFraction">The flight-time error to solve for; ACC-1 is 1e-6.</param>
    /// <returns>The population at which the budget is reached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The radius or the budget is not positive.</exception>
    /// <remarks>
    /// The inverse of <see cref="Estimate"/>, provided because it is the more
    /// useful direction to be told: "this packet is over budget" invites the
    /// question "by how much can I load it", and an error message that answers its
    /// own follow-up is worth more than one that does not.
    /// </remarks>
    public static double PopulationLimit(
        Quantity radius, IonSpecies species, double accelerationPotentialVolts, double timingFraction)
    {
        var metres = radius.SiValue;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timingFraction);

        var potential = 2.0 * timingFraction * Math.Abs(accelerationPotentialVolts);

        return potential * 8.0 * Math.PI * PermittivitySi * metres / Math.Abs(species.ChargeSi);
    }
}
