using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A static electric field the transport engine can integrate through.
/// </summary>
/// <remarks>
/// <para>
/// Stage 1 implementations are analytic: closed-form fields whose exact
/// trajectories are known, so the integrator can be held to ACC-1 before any
/// solver exists to share the blame. The solved-field implementations that
/// arrive with Einzel.Fields sit behind this same interface, which is what makes
/// "the same number by two independent routes" a runnable comparison rather than
/// an aspiration.
/// </para>
/// <para>
/// No device class appears here or below (architecture invariant 2). This is a
/// field, not a reflectron.
/// </para>
/// </remarks>
public interface IElectrostaticField
{
    /// <summary>The electric field vector at a point, in volts per metre.</summary>
    /// <param name="position">The point, in metres.</param>
    /// <returns>The field vector.</returns>
    Vec3 ElectricFieldAt(in Vec3 position);

    /// <summary>
    /// The electric potential at a point, in volts. Required rather than
    /// optional: it is what makes the ACC-4 energy-drift diagnostic computable,
    /// and a field that cannot state its own potential cannot be checked for
    /// conservation.
    /// </summary>
    /// <param name="position">The point, in metres.</param>
    /// <returns>The potential.</returns>
    double PotentialAt(in Vec3 position);

    /// <summary>
    /// The distance from <paramref name="position"/> along
    /// <paramref name="direction"/> over which the field is exactly zero.
    /// </summary>
    /// <param name="position">The starting point, in metres.</param>
    /// <param name="direction">A unit vector along the direction of travel.</param>
    /// <returns>
    /// The guaranteed field-free run length in metres, or zero when the field is
    /// non-zero at <paramref name="position"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Spec section 11: "Field-free drift is advanced analytically." That is worth
    /// real effort in a multi-reflection analyzer, where the drift region is most
    /// of the path — the memo's design point B is 7.55 m of flight of which only
    /// the mirror penetration is under field. Integrating a straight line
    /// numerically accumulates error for no physics.
    /// </para>
    /// <para>
    /// The contract is a guarantee, not a hint: returning a non-zero length
    /// asserts the field is identically zero over that whole run, and the
    /// integrator will skip it exactly. Returning zero is always safe and costs
    /// only speed, which is why it is the default.
    /// </para>
    /// </remarks>
    double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <summary>
    /// A signed measure that changes sign across a discontinuity in the field,
    /// and is zero on it. Infinite when the field is smooth everywhere.
    /// </summary>
    /// <param name="position">The point, in metres.</param>
    /// <returns>The signed distance, in metres, or positive infinity.</returns>
    /// <remarks>
    /// <para>
    /// Runge-Kutta assumes the derivative is smooth across the step. A step that
    /// straddles a field discontinuity violates that: every stage lands on
    /// whichever side its own sample point falls, and the result is a systematic
    /// error that does not shrink the way truncation error does. Left unhandled
    /// on an ideal single-stage mirror it dominates the flight-time budget — the
    /// error stops responding to tolerance and sits on a floor set by the step
    /// size at the crossing.
    /// </para>
    /// <para>
    /// Declaring the surface lets the integrator land on it exactly and restart
    /// on the far side, so each step sees one smooth field. Fields that are
    /// genuinely smooth — every solved and interpolated field — leave this at the
    /// default and pay nothing.
    /// </para>
    /// </remarks>
    double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;
}
