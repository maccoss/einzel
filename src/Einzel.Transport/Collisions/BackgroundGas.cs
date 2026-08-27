using Einzel.Core.Geometry;

namespace Einzel.Transport.Collisions;

/// <summary>Which collision description a run uses.</summary>
/// <remarks>
/// The regime map in spec figure 4 is explicit that these are not interchangeable
/// approximations of one another: above about 10^-2 mbar the collision frequency
/// vastly exceeds the RF frequency and integrating collision by collision is not
/// merely slow, it is the wrong description.
/// </remarks>
public enum CollisionModel
{
    /// <summary>No gas. The ion flies in vacuum.</summary>
    None,

    /// <summary>
    /// Hard-sphere elastic scattering off a Maxwellian gas, below about 10^-5 mbar.
    /// </summary>
    /// <remarks>
    /// Residual-gas scattering, and what puts the pedestal under an arrival-time
    /// peak. The cross section is a declared constant, which is what a measured
    /// collision cross section is.
    /// </remarks>
    HardSphere,

    /// <summary>
    /// Polarization-limited (Langevin) capture, between about 10^-5 and 10^-2 mbar.
    /// </summary>
    /// <remarks>
    /// Trap and guide damping, and thermalization. The ion polarizes the neutral
    /// and is drawn into it, and the resulting rate coefficient does not depend on
    /// how fast the ion is going - see <see cref="BackgroundGas.LangevinRateSi"/>.
    /// </remarks>
    Langevin,
}

/// <summary>
/// The neutral gas an ion flies through: what it is, how much of it there is, and
/// how fast it is moving.
/// </summary>
/// <remarks>
/// <para>
/// Two numbers describe the neutral for collision purposes - its mass and its
/// polarizability - plus a cross section where the hard-sphere description is
/// used. Nothing here knows what device the gas is in.
/// </para>
/// <para>
/// The gas is stationary by default, which spec figure 4 marks as adequate below
/// about 10^-2 mbar. A bulk velocity may be declared; a full neutral velocity
/// <em>field</em>, which the figure requires above that, is not modelled.
/// </para>
/// </remarks>
public sealed record BackgroundGas
{
    /// <summary>Boltzmann's constant, J/K.</summary>
    public const double BoltzmannSi = 1.380649e-23;

    /// <summary>Vacuum permittivity, F/m.</summary>
    public const double VacuumPermittivitySi = 8.8541878128e-12;

    /// <summary>No gas at all.</summary>
    public static BackgroundGas Vacuum { get; } = new() { Model = CollisionModel.None };

    /// <summary>Builds the engine gas from a validated model document.</summary>
    /// <param name="gas">The compiled gas.</param>
    /// <returns>The gas, or <see cref="Vacuum"/> when none is declared.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    public static BackgroundGas FromModel(Core.Model.CompiledGas gas)
    {
        ArgumentNullException.ThrowIfNull(gas);

        if (!gas.IsPresent)
        {
            return Vacuum;
        }

        return new BackgroundGas
        {
            Model = gas.Model switch
            {
                "hardSphere" => CollisionModel.HardSphere,
                "langevin" => CollisionModel.Langevin,
                _ => CollisionModel.None,
            },
            PressureSi = gas.PressureSi,
            TemperatureK = gas.TemperatureK,
            MassSi = gas.MassSi,
            CrossSectionSi = gas.CrossSectionSi,
            PolarizabilitySi = gas.PolarizabilitySi,
            DriftVelocitySi = gas.DriftVelocitySi,
        };
    }

    /// <summary>Which description to use.</summary>
    public CollisionModel Model { get; init; } = CollisionModel.None;

    /// <summary>Pressure, in pascals.</summary>
    public double PressureSi { get; init; }

    /// <summary>Temperature, in kelvin.</summary>
    public double TemperatureK { get; init; } = 300.0;

    /// <summary>Mass of one neutral, in kilograms.</summary>
    public double MassSi { get; init; }

    /// <summary>Collision cross section, in square metres. Hard sphere only.</summary>
    public double CrossSectionSi { get; init; }

    /// <summary>Polarizability volume, in cubic metres. Langevin only.</summary>
    public double PolarizabilitySi { get; init; }

    /// <summary>Bulk velocity of the neutral gas, in metres per second.</summary>
    public Vec3 DriftVelocitySi { get; init; }

    /// <summary>Whether this gas does anything at all.</summary>
    public bool IsPresent => Model != CollisionModel.None && PressureSi > 0.0;

    /// <summary>Neutral number density, in reciprocal cubic metres.</summary>
    /// <remarks>The ideal gas law: n = P / kT. Nothing here runs near a critical point.</remarks>
    public double NumberDensitySi => PressureSi / (BoltzmannSi * TemperatureK);

    /// <summary>
    /// The most probable speed of a neutral, in metres per second.
    /// </summary>
    /// <remarks>
    /// sqrt(2kT/m), the peak of the Maxwell-Boltzmann speed distribution, and the
    /// scale that says how much of the relative velocity between ion and neutral is
    /// the neutral's own motion rather than the ion's.
    /// </remarks>
    public double ThermalSpeedSi => MassSi > 0.0
        ? Math.Sqrt(2.0 * BoltzmannSi * TemperatureK / MassSi)
        : 0.0;

    /// <summary>The reduced mass of an ion and a neutral, in kilograms.</summary>
    /// <param name="ionMassSi">Ion mass, in kilograms.</param>
    /// <returns>The reduced mass.</returns>
    public double ReducedMassSi(double ionMassSi) =>
        MassSi > 0.0 ? ionMassSi * MassSi / (ionMassSi + MassSi) : ionMassSi;

    /// <summary>
    /// The Langevin rate coefficient for an ion, in cubic metres per second.
    /// </summary>
    /// <param name="ionMassSi">Ion mass, in kilograms.</param>
    /// <param name="chargeSi">Ion charge, in coulombs.</param>
    /// <returns>The rate coefficient.</returns>
    /// <remarks>
    /// <para>
    /// k = q sqrt(pi a / (eps0 mu)), with the polarizability as a volume. The
    /// property that matters computationally is that <em>it does not contain the
    /// speed</em>: the capture cross section goes as 1/v and the rate is the
    /// product, so a Langevin collision is a Poisson process with a constant rate
    /// and the time to the next one is a plain exponential draw. The hard-sphere
    /// rate is not, and needs the null-collision machinery.
    /// </para>
    /// <para>
    /// That is physics rather than convenience: it is why mobility in the
    /// polarization limit is independent of temperature, and it is the reason the
    /// Langevin limit is a useful reference at all.
    /// </para>
    /// </remarks>
    public double LangevinRateSi(double ionMassSi, double chargeSi)
    {
        var reduced = ReducedMassSi(ionMassSi);

        if (reduced <= 0.0 || PolarizabilitySi <= 0.0)
        {
            return 0.0;
        }

        return Math.Abs(chargeSi)
            * Math.Sqrt(Math.PI * PolarizabilitySi / (VacuumPermittivitySi * reduced));
    }

    /// <summary>
    /// The mean free path of an ion moving much faster than the gas, in metres.
    /// </summary>
    /// <param name="ionMassSi">Ion mass, in kilograms.</param>
    /// <param name="chargeSi">Ion charge, in coulombs.</param>
    /// <param name="speedSi">Ion speed, in metres per second.</param>
    /// <returns>The mean free path.</returns>
    /// <remarks>
    /// The distance an ion covers per collision on average, which is what the
    /// Knudsen number is built from. For hard spheres this is 1/(n sigma) in the
    /// fast-ion limit; for Langevin it is v/(n k), which does depend on speed
    /// because the <em>rate</em> does not.
    /// </remarks>
    public double MeanFreePathSi(double ionMassSi, double chargeSi, double speedSi)
    {
        var rate = Model switch
        {
            CollisionModel.HardSphere => NumberDensitySi * CrossSectionSi * speedSi,
            CollisionModel.Langevin => NumberDensitySi * LangevinRateSi(ionMassSi, chargeSi),
            _ => 0.0,
        };

        return rate > 0.0 ? speedSi / rate : double.PositiveInfinity;
    }

    /// <summary>
    /// The collision rate for an ion at a given speed, in reciprocal seconds.
    /// </summary>
    /// <param name="ionMassSi">Ion mass, in kilograms.</param>
    /// <param name="chargeSi">Ion charge, in coulombs.</param>
    /// <param name="speedSi">Ion speed relative to the bulk gas, in metres per second.</param>
    /// <returns>The rate.</returns>
    public double CollisionRateSi(double ionMassSi, double chargeSi, double speedSi) => Model switch
    {
        CollisionModel.HardSphere => NumberDensitySi * CrossSectionSi * speedSi,
        CollisionModel.Langevin => NumberDensitySi * LangevinRateSi(ionMassSi, chargeSi),
        _ => 0.0,
    };

    /// <summary>
    /// Low-field mobility from Mason-Schamp, in square metres per volt-second.
    /// </summary>
    /// <param name="ionMassSi">Ion mass, in kilograms.</param>
    /// <param name="chargeSi">Ion charge, in coulombs.</param>
    /// <returns>The mobility, or zero when the gas has no cross section.</returns>
    /// <remarks>
    /// <para>
    /// K = (3q / 16N) sqrt(2 pi / (mu k T)) / Omega, the first-order Chapman-Enskog
    /// result. This is <em>not</em> used to move an ion - the event-driven models
    /// derive their own drift by colliding - which is exactly what makes it useful:
    /// it is an independent closed form to check the collision kinematics against,
    /// and a measured drift velocity that disagrees with it means the scattering is
    /// wrong rather than that the estimate is.
    /// </para>
    /// <para>
    /// TRN-1 makes mobility an explicit input with stated field dependence for the
    /// diffusive transport mode. Here it is an output.
    /// </para>
    /// </remarks>
    public double LowFieldMobilitySi(double ionMassSi, double chargeSi)
    {
        var omega = CrossSectionSi;
        var reduced = ReducedMassSi(ionMassSi);

        if (omega <= 0.0 || reduced <= 0.0 || NumberDensitySi <= 0.0)
        {
            return 0.0;
        }

        return 3.0 * Math.Abs(chargeSi) / (16.0 * NumberDensitySi * omega)
            * Math.Sqrt(2.0 * Math.PI / (reduced * BoltzmannSi * TemperatureK));
    }
}
