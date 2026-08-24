using Einzel.Core.Units;

namespace Einzel.Transport;

/// <summary>
/// The mass and charge of the ion being tracked, held in SI.
/// </summary>
/// <remarks>
/// Constructed through <see cref="Quantity"/> at the boundary, then held as raw
/// SI doubles because this sits in the integrator's inner loop. Spec section 9's
/// rule is that units are explicit at every boundary, not that every arithmetic
/// step re-derives them.
/// </remarks>
public readonly record struct IonSpecies
{
    private IonSpecies(double massSi, double chargeSi)
    {
        MassSi = massSi;
        ChargeSi = chargeSi;
    }

    /// <summary>Mass, in kilograms.</summary>
    public double MassSi { get; }

    /// <summary>Charge, in coulombs. Signed.</summary>
    public double ChargeSi { get; }

    /// <summary>Charge divided by mass, in coulombs per kilogram. Precomputed for the inner loop.</summary>
    public double ChargeToMassSi => ChargeSi / MassSi;

    /// <summary>Creates a species from explicit mass and charge quantities.</summary>
    /// <param name="mass">The mass; must be positive and of mass dimension.</param>
    /// <param name="charge">The charge; must be of charge dimension.</param>
    /// <returns>The species.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mass is not positive.</exception>
    /// <exception cref="Core.Errors.EinzelException">A quantity has the wrong dimension.</exception>
    public static IonSpecies Create(Quantity mass, Quantity charge)
    {
        var massKg = mass.In("kg");
        var chargeC = charge.In("C");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massKg);

        return new IonSpecies(massKg, chargeC);
    }

    /// <summary>
    /// Creates a species from a mass-to-charge ratio and a charge number, the
    /// form mass spectrometry actually quotes.
    /// </summary>
    /// <param name="massToCharge">
    /// The m/z value, in daltons per elementary charge.
    /// </param>
    /// <param name="chargeNumber">
    /// The charge number z; positive for cations, negative for anions, and never
    /// zero.
    /// </param>
    /// <returns>The species.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The m/z is not positive, or the charge number is zero.
    /// </exception>
    /// <remarks>
    /// The ion mass is taken as m/z multiplied by the absolute charge number. The
    /// mass of the transferred electrons is not subtracted: at m/z 500 that is
    /// about 1 ppm of the mass and so roughly 0.5 ppm of the flight time, which
    /// is inside the ACC-1 budget but not negligible against it. Callers needing
    /// that accuracy should supply an explicit mass through <see cref="Create"/>.
    /// </remarks>
    public static IonSpecies FromMassToCharge(double massToCharge, int chargeNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massToCharge);

        if (chargeNumber == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chargeNumber), chargeNumber, "an ion cannot have zero charge");
        }

        var mass = Quantity.From(massToCharge * Math.Abs(chargeNumber), "u");
        var charge = Quantity.From(chargeNumber, "e");

        return new IonSpecies(mass.SiValue, charge.SiValue);
    }

    /// <summary>The mass, as a quantity.</summary>
    /// <returns>The mass.</returns>
    public Quantity Mass() => Quantity.Si(MassSi, Dimension.MassDimension);

    /// <summary>The charge, as a quantity.</summary>
    /// <returns>The charge.</returns>
    public Quantity Charge() => Quantity.Si(ChargeSi, Dimension.Charge);

    /// <summary>
    /// The speed this ion reaches when accelerated from rest through a potential
    /// difference, ignoring relativistic correction.
    /// </summary>
    /// <param name="potentialDifference">The accelerating potential.</param>
    /// <returns>The resulting speed.</returns>
    /// <remarks>
    /// At the memo's design points — 1 to 4 keV — the relativistic correction to
    /// the speed of an m/z 500 ion is below 1e-12, far inside ACC-1. It becomes
    /// worth revisiting only for light ions at high energy.
    /// </remarks>
    public Quantity SpeedAfterAcceleration(Quantity potentialDifference)
    {
        var volts = potentialDifference.In("V");
        var speed = Math.Sqrt(2.0 * Math.Abs(ChargeSi) * Math.Abs(volts) / MassSi);

        return Quantity.Si(speed, Dimension.Velocity);
    }
}
