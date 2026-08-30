using Einzel.Transport.Collisions;

namespace Einzel.Transport.Diffusion;

/// <summary>
/// How fast an ion drifts per unit field, and how that changes with the field.
/// </summary>
/// <remarks>
/// <para>
/// TRN-1: mobility is an explicit input with stated field dependence. Explicit
/// because it is the one number a diffusive transport calculation rests on entirely
/// - the drift velocity, the diffusion coefficient through Einstein, and therefore
/// the residence time and the spread all come from it - and a platform that guessed
/// it from a cross section would be presenting a guess with the authority of a
/// solve.
/// </para>
/// <para>
/// The field dependence is stated rather than assumed constant because it is not.
/// Above a few tens of townsend an ion is heated by the field, its collision rate
/// rises, and the mobility falls; treating it as constant there overestimates the
/// drift by tens of per cent. The two-term form here is the standard low-field
/// expansion and it says where it stops applying.
/// </para>
/// </remarks>
/// <param name="ZeroFieldSi">
/// Mobility as the field goes to zero, in square metres per volt-second, at the
/// gas density it was measured at.
/// </param>
/// <param name="Alpha">
/// The quadratic coefficient of the field expansion: K(E/N) = K0 (1 + a (E/N)^2),
/// with E/N in townsend. Zero for a field-independent mobility.
/// </param>
/// <param name="ValidToTownsend">
/// The reduced field this expansion was fitted to. Past it the value is an
/// extrapolation and says so.
/// </param>
public readonly record struct Mobility(
    double ZeroFieldSi,
    double Alpha = 0.0,
    double ValidToTownsend = 50.0)
{
    /// <summary>One townsend, in volt square metres.</summary>
    public const double Townsend = 1e-21;

    /// <summary>
    /// Mobility derived from a collision cross section, by Mason-Schamp.
    /// </summary>
    /// <param name="gas">The gas.</param>
    /// <param name="species">The ion.</param>
    /// <returns>The mobility, field-independent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    /// <remarks>
    /// A convenience, not the primary path. TRN-1 wants mobility declared, and a
    /// value derived from a cross section carries the cross section's uncertainty
    /// plus the first-order Chapman-Enskog approximation on top. It is here because
    /// a model that already declares a cross section for the event-driven mode
    /// should not have to declare a second, independent number to run the diffusive
    /// one - and because the two modes then describe the same gas, which is what
    /// REG-3's comparison requires to mean anything.
    /// </remarks>
    public static Mobility FromCrossSection(BackgroundGas gas, IonSpecies species)
    {
        ArgumentNullException.ThrowIfNull(gas);

        return new Mobility(gas.LowFieldMobilitySi(species.MassSi, species.ChargeSi));
    }

    /// <summary>Mobility at a given field strength and gas density.</summary>
    /// <param name="fieldSi">Field magnitude, in volts per metre.</param>
    /// <param name="numberDensitySi">Gas number density, in reciprocal cubic metres.</param>
    /// <returns>The mobility, in square metres per volt-second.</returns>
    public double At(double fieldSi, double numberDensitySi)
    {
        if (Alpha == 0.0 || numberDensitySi <= 0.0)
        {
            return ZeroFieldSi;
        }

        var reduced = fieldSi / (numberDensitySi * Townsend);

        // Clamped at zero rather than allowed to go negative. A fitted expansion
        // driven past its range is an extrapolation, and an extrapolation that
        // changes the sign of a drift velocity is not one worth honouring.
        return Math.Max(0.0, ZeroFieldSi * (1.0 + (Alpha * reduced * reduced)));
    }

    /// <summary>The mobility at a field, in a gas denser or thinner than the declared one.</summary>
    /// <param name="fieldSi">Field magnitude, in volts per metre.</param>
    /// <param name="numberDensitySi">Gas number density here, in reciprocal cubic metres.</param>
    /// <param name="referenceNumberDensitySi">
    /// The density this mobility was declared or derived at.
    /// </param>
    /// <returns>The mobility, in square metres per volt-second.</returns>
    /// <remarks>
    /// <para>
    /// <b>Mobility goes as the reciprocal of density, and nothing here did that
    /// before.</b> An ion drifts further between collisions in a thinner gas, so
    /// mu N is the constant - that is what makes <em>reduced</em> mobility the
    /// quantity tabulated in the literature rather than mobility itself. Reading a
    /// single declared mobility at every point of a graded gas would put the ion's
    /// drift at the wrong speed everywhere except where the pressure happens to
    /// equal the declared one.
    /// </para>
    /// <para>
    /// Two separate density dependences, and they are not the same one. This factor
    /// is how <em>much</em> gas; the field expansion below is E/N, how hard the ion
    /// is being pushed <em>between</em> collisions. A graded gas moves both, and a
    /// version that scaled only the second would leave the drift speed flat across a
    /// pressure gradient while reporting a changing field dependence - which reads
    /// as the mobility being handled.
    /// </para>
    /// <para>
    /// Bit-identical to the two-argument form where the two densities are equal:
    /// the ratio is exactly 1.0 and multiplying by it changes nothing. That is the
    /// control which says a model with no pressure field is untouched.
    /// </para>
    /// </remarks>
    public double At(double fieldSi, double numberDensitySi, double referenceNumberDensitySi)
    {
        if (numberDensitySi <= 0.0 || referenceNumberDensitySi <= 0.0)
        {
            return At(fieldSi, numberDensitySi);
        }

        var scaled = ZeroFieldSi * (referenceNumberDensitySi / numberDensitySi);

        if (Alpha == 0.0)
        {
            return scaled;
        }

        var reduced = fieldSi / (numberDensitySi * Townsend);

        return Math.Max(0.0, scaled * (1.0 + (Alpha * reduced * reduced)));
    }

    /// <summary>
    /// The diffusion coefficient that goes with a mobility, in square metres per second.
    /// </summary>
    /// <param name="temperatureK">Gas temperature, in kelvin.</param>
    /// <param name="chargeSi">Ion charge, in coulombs.</param>
    /// <param name="mobilitySi">The mobility at the local field.</param>
    /// <returns>The diffusion coefficient.</returns>
    /// <remarks>
    /// The Einstein relation, D = mu k T / q. It holds while the ion is in thermal
    /// equilibrium with the gas, which is the same low-field limit the mobility
    /// expansion above is fitted in - so the two assumptions fail together rather
    /// than one quietly outliving the other.
    /// </remarks>
    public static double DiffusionSi(double temperatureK, double chargeSi, double mobilitySi) =>
        mobilitySi * BackgroundGas.BoltzmannSi * temperatureK / Math.Abs(chargeSi);

    /// <summary>Whether a field is inside the range this mobility was fitted to.</summary>
    /// <param name="fieldSi">Field magnitude, in volts per metre.</param>
    /// <param name="numberDensitySi">Gas number density, in reciprocal cubic metres.</param>
    /// <returns><see langword="true"/> when the value is a fit rather than an extrapolation.</returns>
    public bool IsWithinFit(double fieldSi, double numberDensitySi) =>
        numberDensitySi <= 0.0
        || fieldSi / (numberDensitySi * Townsend) <= ValidToTownsend;
}
