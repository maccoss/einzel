using Einzel.Core.Results;

namespace Einzel.Transport.Collisions;

/// <summary>
/// The dimensionless numbers that say whether the chosen description applies.
/// </summary>
/// <param name="PressureMbar">Gas pressure, in millibar.</param>
/// <param name="MeanFreePathM">Ion mean free path, in metres.</param>
/// <param name="ApertureM">The length the Knudsen number is taken against, in metres.</param>
/// <param name="Knudsen">Mean free path over that length.</param>
/// <param name="CollisionsPerFlight">Expected collisions over the whole flight.</param>
/// <param name="CollisionsPerRfCycle">Expected collisions per drive cycle, or NaN when undriven.</param>
public readonly record struct RegimeNumbers(
    double PressureMbar,
    double MeanFreePathM,
    double ApertureM,
    double Knudsen,
    double CollisionsPerFlight,
    double CollisionsPerRfCycle);

/// <summary>
/// Computes regime validity, and refuses to let a run look valid outside it.
/// </summary>
/// <remarks>
/// <para>
/// REG-2: the engine computes the governing dimensionless numbers along every path
/// and raises a <em>non-suppressible</em> warning when the selected mode is outside
/// validity. The point is that this is engine behaviour rather than documentation -
/// an agent producing fifty transmission numbers in an afternoon, three of them
/// computed outside the validity of the model used, is the defining risk of the
/// whole thesis.
/// </para>
/// <para>
/// The boundaries come from spec figure 4, whose caption is the argument: above
/// about 10^-2 mbar the collision frequency vastly exceeds the RF frequency and
/// residence times are of order a millisecond, so integrating collision by
/// collision is not merely slow, it is the wrong description. Below about 10^-5
/// mbar an ion may not collide at all and every nanosecond matters. Between them
/// is the band the figure marks dangerous: both descriptions run, neither is
/// obviously right, and the engine must run both and report the disagreement
/// rather than silently choosing.
/// </para>
/// </remarks>
public static class RegimeDiagnostics
{
    /// <summary>Above this, trajectory integration is the wrong description entirely.</summary>
    public const double DiffusiveMbar = 1e-2;

    /// <summary>Above this, both descriptions run and neither is obviously right.</summary>
    public const double OverlapMbar = 1e-3;

    /// <summary>Above this, polarization capture dominates hard-sphere scattering.</summary>
    public const double LangevinMbar = 1e-5;

    /// <summary>Computes the numbers for one flight.</summary>
    /// <param name="gas">The gas.</param>
    /// <param name="species">The ion.</param>
    /// <param name="speedSi">A representative ion speed, in metres per second.</param>
    /// <param name="flightSeconds">The flight duration, in seconds.</param>
    /// <param name="apertureM">
    /// The smallest length the ion has to pass through, in metres. The Knudsen
    /// number is meaningless without one, and the honest choice is the tightest
    /// constriction rather than the size of the whole instrument.
    /// </param>
    /// <param name="driveHz">The drive frequency, or zero when undriven.</param>
    /// <returns>The numbers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    public static RegimeNumbers Measure(
        BackgroundGas gas,
        IonSpecies species,
        double speedSi,
        double flightSeconds,
        double apertureM,
        double driveHz = 0.0)
    {
        ArgumentNullException.ThrowIfNull(gas);

        var rate = gas.CollisionRateSi(species.MassSi, species.ChargeSi, speedSi);
        var path = gas.MeanFreePathSi(species.MassSi, species.ChargeSi, speedSi);

        return new RegimeNumbers(
            PressureMbar: gas.PressureSi / 1e2,
            MeanFreePathM: path,
            ApertureM: apertureM,
            Knudsen: apertureM > 0.0 ? path / apertureM : double.PositiveInfinity,
            CollisionsPerFlight: rate * flightSeconds,
            CollisionsPerRfCycle: driveHz > 0.0 ? rate / driveHz : double.NaN);
    }

    /// <summary>
    /// Warnings a trajectory-integrated run in this gas must carry.
    /// </summary>
    /// <param name="gas">The gas.</param>
    /// <param name="numbers">The measured regime numbers.</param>
    /// <returns>The warnings, in descending severity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    /// <remarks>
    /// Every one of these is a validity violation or a qualification, never an
    /// advisory, because GRD-3 makes only advisories suppressible and none of these
    /// is advice.
    /// </remarks>
    public static IReadOnlyList<ValidityWarning> ForTrajectoryMode(
        BackgroundGas gas, RegimeNumbers numbers)
    {
        ArgumentNullException.ThrowIfNull(gas);

        var warnings = new List<ValidityWarning>();

        if (!gas.IsPresent)
        {
            return warnings;
        }

        if (numbers.PressureMbar > DiffusiveMbar)
        {
            warnings.Add(new ValidityWarning(
                "regime.trajectory-above-validity",
                $"at {numbers.PressureMbar:G3} mbar an ion makes about "
                + $"{numbers.CollisionsPerFlight:G3} collisions over this flight, and above "
                + $"{DiffusiveMbar:G1} mbar trajectory integration is not the description of this "
                + "physics - a diffusive region has no trajectories, it has a density field. "
                + "Statistical diffusion is the mode for this pressure and it is not built, so "
                + "this result is outside the validity of every mode this engine has",
                WarningSeverity.ValidityViolation));
        }
        else if (numbers.PressureMbar > OverlapMbar)
        {
            warnings.Add(new ValidityWarning(
                "regime.overlap-band",
                $"{numbers.PressureMbar:G3} mbar is inside the band where both transport "
                + "descriptions run and neither is obviously right. REG-3 makes running both and "
                + "reporting the disagreement a supported operation; statistical diffusion is not "
                + "built, so the comparison that would settle it cannot be made",
                WarningSeverity.Qualified));
        }

        if (numbers.Knudsen < 1.0 && double.IsFinite(numbers.Knudsen))
        {
            warnings.Add(new ValidityWarning(
                "regime.knudsen-continuum",
                $"the mean free path is {numbers.MeanFreePathM * 1e3:G3} mm against a "
                + $"{numbers.ApertureM * 1e3:G3} mm aperture, a Knudsen number of "
                + $"{numbers.Knudsen:G3}. Below 1 the gas is a continuum on the scale of the "
                + "geometry and the neutral velocity field matters; this run treats the gas as "
                + "stationary and uniform",
                WarningSeverity.Qualified));
        }

        if (double.IsFinite(numbers.CollisionsPerRfCycle) && numbers.CollisionsPerRfCycle > 1.0)
        {
            warnings.Add(new ValidityWarning(
                "regime.collisions-outrun-rf",
                $"about {numbers.CollisionsPerRfCycle:G3} collisions happen per drive cycle, so "
                + "the ion does not complete an oscillation between them and the pseudopotential "
                + "picture the drive is designed around does not apply",
                WarningSeverity.ValidityViolation));
        }

        if (gas.Model == CollisionModel.HardSphere && numbers.PressureMbar > LangevinMbar)
        {
            warnings.Add(new ValidityWarning(
                "regime.model-below-validity",
                $"hard-sphere scattering is declared at {numbers.PressureMbar:G3} mbar, above the "
                + $"{LangevinMbar:G1} mbar where polarization capture takes over. Hard spheres "
                + "will under-damp: a Langevin collision is a capture rather than a glance, and "
                + "at this pressure most of them are",
                WarningSeverity.Qualified));
        }

        if (gas.Model == CollisionModel.Langevin && numbers.PressureMbar < LangevinMbar)
        {
            warnings.Add(new ValidityWarning(
                "regime.model-above-validity",
                $"polarization capture is declared at {numbers.PressureMbar:G3} mbar, below the "
                + $"{LangevinMbar:G1} mbar where residual-gas scattering is the mechanism. "
                + "Langevin capture will over-damp and will not produce the arrival-time pedestal "
                + "that hard-sphere scattering does",
                WarningSeverity.Qualified));
        }

        return warnings;
    }
}
