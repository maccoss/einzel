using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Transport;
using Einzel.Transport.Fields;
using Einzel.Transport.Integration;

namespace Einzel.Transport.Tests;

/// <summary>
/// The closed-form single-stage reflectron used as a golden reference.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 19 lists "ideal single-stage reflectron focusing" in the analytic
/// test tier. The geometry is one dimensional along x: the ion starts at
/// <c>x = -driftLength</c> moving toward the mirror, drifts field-free to
/// <c>x = 0</c>, decelerates linearly, turns, and returns to the detector at the
/// starting plane.
/// </para>
/// <para>
/// Every quantity below has an exact expression, which is the point. An ion of
/// energy <c>U = qV</c> entering a gradient <c>G</c> penetrates to
/// <c>d = V / G</c> and spends <c>2 v / a</c> in the mirror, so the total flight
/// is
/// <c>T = 2 L / v + 2 v / a</c>. Setting <c>dT/dv = 0</c> gives the classic
/// first-order energy focusing condition: the total field-free path equals four
/// penetration depths. That condition is a statement about the physics, not about
/// the integrator, which makes it the sharpest available check that the two
/// agree.
/// </para>
/// <para>
/// This lives in the test project rather than the engine on purpose.
/// Architecture invariant 2 keeps device classes above Einzel.Library; a
/// reflectron is an arrangement of a half-space field, and the engine knows only
/// the field.
/// </para>
/// </remarks>
internal sealed class IdealSingleStageReflectron
{
    private IdealSingleStageReflectron(
        IonSpecies species,
        double accelerationVoltage,
        double penetrationDepth,
        double driftLength)
    {
        Species = species;
        AccelerationVoltage = accelerationVoltage;
        PenetrationDepth = penetrationDepth;
        DriftLength = driftLength;

        GradientSi = accelerationVoltage / penetrationDepth;
        Field = HalfSpaceUniformField.Create(
            Vec3.Zero, Vec3.UnitX, Quantity.Si(GradientSi, Dimension.ElectricField));
    }

    /// <summary>
    /// Builds a reflectron at the first-order energy focusing condition, where the
    /// one-way drift is twice the penetration depth.
    /// </summary>
    public static IdealSingleStageReflectron AtFirstOrderFocus(
        IonSpecies species, Quantity accelerationVoltage, Quantity penetrationDepth)
    {
        var depth = penetrationDepth.In("m");
        return new IdealSingleStageReflectron(species, accelerationVoltage.In("V"), depth, 2.0 * depth);
    }

    /// <summary>Builds a reflectron with the drift length detuned away from focus.</summary>
    public static IdealSingleStageReflectron Detuned(
        IonSpecies species, Quantity accelerationVoltage, Quantity penetrationDepth, double driftInDepths)
    {
        var depth = penetrationDepth.In("m");
        return new IdealSingleStageReflectron(
            species, accelerationVoltage.In("V"), depth, driftInDepths * depth);
    }

    public IonSpecies Species { get; }

    /// <summary>Nominal acceleration voltage, in volts.</summary>
    public double AccelerationVoltage { get; }

    /// <summary>Penetration depth at the nominal energy, in metres.</summary>
    public double PenetrationDepth { get; }

    /// <summary>One-way field-free path, in metres. The total is twice this.</summary>
    public double DriftLength { get; }

    /// <summary>Mirror potential gradient, in volts per metre.</summary>
    public double GradientSi { get; }

    public HalfSpaceUniformField Field { get; }

    /// <summary>Speed at the given fractional energy offset from nominal.</summary>
    public double SpeedAt(double energyFraction = 0.0)
    {
        var energy = Math.Abs(Species.ChargeSi) * AccelerationVoltage * (1.0 + energyFraction);
        return Math.Sqrt(2.0 * energy / Species.MassSi);
    }

    /// <summary>The ion's launch state, at the detector plane heading for the mirror.</summary>
    public PhaseState LaunchState(double energyFraction = 0.0) =>
        new(new Vec3(-DriftLength, 0.0, 0.0), new Vec3(SpeedAt(energyFraction), 0.0, 0.0));

    /// <summary>
    /// The stopping surface: the detector, back at the launch plane. Positive
    /// while the ion is on the mirror side of it.
    /// </summary>
    public TrajectoryStopFunction DetectorPlane()
    {
        var plane = -DriftLength;
        return (in PhaseState state) => state.Position.X - plane;
    }

    /// <summary>Deceleration inside the mirror, in metres per second squared.</summary>
    public double MirrorDeceleration => Math.Abs(Species.ChargeSi) * GradientSi / Species.MassSi;

    /// <summary>
    /// Exact total flight time, in seconds: field-free path plus time in the
    /// mirror.
    /// </summary>
    public double ExactFlightTime(double energyFraction = 0.0) =>
        ExactFlightTimeAtSpeed(SpeedAt(energyFraction));

    /// <summary>Exact total flight time for an ion of the given speed.</summary>
    public double ExactFlightTimeAtSpeed(double speed) =>
        (2.0 * DriftLength / speed) + (2.0 * speed / MirrorDeceleration);

    /// <summary>Launch state for an ion whose speed is offset by a fraction of nominal.</summary>
    public PhaseState LaunchAtSpeedFraction(double velocityFraction) =>
        new(new Vec3(-DriftLength, 0.0, 0.0), new Vec3(SpeedAt() * (1.0 + velocityFraction), 0.0, 0.0));

    /// <summary>
    /// Exact derivative of flight time with respect to fractional velocity
    /// offset, at nominal energy. Zero exactly at the first-order focus.
    /// </summary>
    public double ExactFlightTimeVelocityDerivative()
    {
        var speed = SpeedAt();
        var acceleration = Math.Abs(Species.ChargeSi) * GradientSi / Species.MassSi;

        return (-2.0 * DriftLength / speed) + (2.0 * speed / acceleration);
    }

    /// <summary>Settings tuned for this geometry, with a flight-time ceiling as a runaway guard.</summary>
    public IntegrationSettings Settings() => new()
    {
        MaximumFlightTime = 100.0 * ExactFlightTime(),
    };
}
