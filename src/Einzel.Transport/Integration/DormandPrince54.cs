using Einzel.Core.Geometry;
using Einzel.Fields;

namespace Einzel.Transport.Integration;

/// <summary>
/// The Dormand-Prince 5(4) embedded Runge-Kutta pair.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 11 makes adaptive-step Runge-Kutta with per-step error control
/// the default integrator. Dormand-Prince 5(4) is the standard choice: seven
/// stages, a fifth-order solution with an embedded fourth-order estimate for
/// error control, and first-same-as-last, so an accepted step costs six field
/// evaluations rather than seven.
/// </para>
/// <para>
/// A note on where this sits in the error budget. Spec section 8 is explicit that
/// the instinct to reach for a higher-order integrator is "usually the wrong
/// lever", because in a solved field the dominant error is the interpolant's
/// discontinuous derivatives at cell boundaries, not the integrator's truncation.
/// Stage 1 has no interpolant, which is exactly why the integrator can be
/// characterised honestly here: whatever error shows up against a closed form is
/// the integrator's own.
/// </para>
/// <para>
/// All coefficients are written as exact rational quotients rather than decimal
/// literals, so the compiler rounds each once, correctly, instead of inheriting
/// whatever a transcription rounded to.
/// </para>
/// </remarks>
internal static class DormandPrince54
{
    /// <summary>The order of the solution that is propagated.</summary>
    public const int Order = 5;

    // Stage times, as fractions of the step. Unused while the field is static -
    // every stage then samples the same field - and load-bearing the moment it is
    // not, because a stage evaluated at the wrong instant of an RF cycle is a
    // wrong force applied with full fifth-order confidence.
    internal const double C2 = 1.0 / 5.0;
    internal const double C3 = 3.0 / 10.0;
    internal const double C4 = 4.0 / 5.0;
    internal const double C5 = 8.0 / 9.0;
    internal const double C6 = 1.0;
    internal const double C7 = 1.0;

    // Stage coefficients.
    internal const double A21 = 1.0 / 5.0;

    internal const double A31 = 3.0 / 40.0;
    internal const double A32 = 9.0 / 40.0;

    internal const double A41 = 44.0 / 45.0;
    internal const double A42 = -56.0 / 15.0;
    internal const double A43 = 32.0 / 9.0;

    internal const double A51 = 19372.0 / 6561.0;
    internal const double A52 = -25360.0 / 2187.0;
    internal const double A53 = 64448.0 / 6561.0;
    internal const double A54 = -212.0 / 729.0;

    internal const double A61 = 9017.0 / 3168.0;
    internal const double A62 = -355.0 / 33.0;
    internal const double A63 = 46732.0 / 5247.0;
    internal const double A64 = 49.0 / 176.0;
    internal const double A65 = -5103.0 / 18656.0;

    // Fifth-order weights. Also the seventh stage's coefficients, which is what
    // makes the method first-same-as-last.
    internal const double B1 = 35.0 / 384.0;
    internal const double B3 = 500.0 / 1113.0;
    internal const double B4 = 125.0 / 192.0;
    internal const double B5 = -2187.0 / 6784.0;
    internal const double B6 = 11.0 / 84.0;

    // Fourth-order weights, for the embedded error estimate.
    internal const double E1 = 5179.0 / 57600.0;
    internal const double E3 = 7571.0 / 16695.0;
    internal const double E4 = 393.0 / 640.0;
    internal const double E5 = -92097.0 / 339200.0;
    internal const double E6 = 187.0 / 2100.0;
    internal const double E7 = 1.0 / 40.0;

    /// <summary>How many times <see cref="Step"/> evaluates the field, for the integrator's count.</summary>
    /// <param name="field">The field stepped through.</param>
    /// <param name="endsOnSwitch">Whether the step ends on a switch.</param>
    /// <returns>
    /// Six stages after the first, and one more where the derivative handed on is evaluated apart
    /// from the last stage: across a magnet face, or on a switch.
    /// </returns>
    public static int Evaluations(IElectrostaticField field, bool endsOnSwitch = false) =>
        field is Einzel.Fields.Magnetic.IMagneticField || endsOnSwitch ? 7 : 6;

    /// <summary>
    /// Takes one step, producing the fifth-order state, the difference between
    /// the fifth- and fourth-order solutions, and the derivative at the new state
    /// for reuse as the next step's first stage.
    /// </summary>
    /// <param name="state">The state at the start of the step.</param>
    /// <param name="k1">The derivative at <paramref name="state"/>.</param>
    /// <param name="stepSize">The step size, in seconds.</param>
    /// <param name="field">The field being integrated through.</param>
    /// <param name="chargeToMass">Charge divided by mass, in coulombs per kilogram.</param>
    /// <param name="result">The fifth-order state at the end of the step.</param>
    /// <param name="errorPosition">Position difference between the two solutions, in metres.</param>
    /// <param name="errorVelocity">Velocity difference between the two solutions, in metres per second.</param>
    /// <param name="derivativeAtResult">The derivative at <paramref name="result"/>.</param>
    /// <param name="timeSeconds">
    /// The instant the step starts at. Ignored by a static field; every stage of a
    /// driven one samples a different point in the cycle.
    /// </param>
    /// <param name="switchSeconds">
    /// The switch the step was cut to end on, exactly as the field reported it, or NaN for a step
    /// that does not end on one. The stages at the end then ask the field for the phase ending
    /// there (<see cref="ITimeVaryingField.ElectricFieldBefore"/>), to which the whole step
    /// belongs, at the switch itself rather than at <c>t + h</c>, which is a sum that need not
    /// equal it; and the derivative handed on is the starting phase's, at the switch.
    /// </param>
    public static void Step(
        in PhaseState state,
        in PhaseDerivative k1,
        double stepSize,
        IElectrostaticField field,
        double chargeToMass,
        out PhaseState result,
        out Vec3 errorPosition,
        out Vec3 errorVelocity,
        out PhaseDerivative derivativeAtResult,
        double timeSeconds = 0.0,
        double switchSeconds = double.NaN)
    {
        var h = stepSize;
        var endsOnSwitch = !double.IsNaN(switchSeconds);

        // Every stage takes B from the side of each magnet face the step STARTED on (see
        // IMagneticField.MagneticFieldAt(position, side)), so a step straddling a hard edge
        // integrates one smooth field and the integrator lands on the face as a declared
        // discontinuity. Without a magnet the side is never read.
        var side = SideOf(in state, field);

        var k2 = Derivative(Offset(state, h, (A21, k1)), field, chargeToMass, timeSeconds + (C2 * h), in side);
        var k3 = Derivative(
            Offset(state, h, (A31, k1), (A32, k2)), field, chargeToMass, timeSeconds + (C3 * h), in side);
        var k4 = Derivative(
            Offset(state, h, (A41, k1), (A42, k2), (A43, k3)), field, chargeToMass, timeSeconds + (C4 * h), in side);
        var k5 = Derivative(
            Offset(state, h, (A51, k1), (A52, k2), (A53, k3), (A54, k4)),
            field, chargeToMass, timeSeconds + (C5 * h), in side);
        // C6 and C7 are one: these two stages sit at the step's end, which on a switch is the
        // next phase's first instant. The step belongs to the phase ending, so there they ask it.
        var k6 = Derivative(
            Offset(state, h, (A61, k1), (A62, k2), (A63, k3), (A64, k4), (A65, k5)),
            field, chargeToMass, endsOnSwitch ? switchSeconds : timeSeconds + (C6 * h), in side, endsOnSwitch);

        result = Offset(state, h, (B1, k1), (B3, k3), (B4, k4), (B5, k5), (B6, k6));

        var k7 = Derivative(
            result, field, chargeToMass, endsOnSwitch ? switchSeconds : timeSeconds + (C7 * h), in side, endsOnSwitch);

        // The error estimate uses the start's side, but the next step starts at the result
        // and must see the field there: identical unless the step ended across a magnet face,
        // which the integrator lands on and recomputes anyway - or unless two faces were
        // crossed at once, which the sign product cannot see.
        //
        // And a step that ended on a switch hands on the derivative of the phase now starting,
        // where the next step begins: the k7 above is the ending phase's, for the estimate.
        derivativeAtResult = field is Einzel.Fields.Magnetic.IMagneticField || endsOnSwitch
            ? Derivative(in result, field, chargeToMass, endsOnSwitch ? switchSeconds : timeSeconds + (C7 * h))
            : k7;

        var fourth = Offset(state, h, (E1, k1), (E3, k3), (E4, k4), (E5, k5), (E6, k6), (E7, k7));

        errorPosition = result.Position - fourth.Position;
        errorVelocity = result.Velocity - fourth.Velocity;
    }

    /// <summary>The equation of motion: velocity, and acceleration from the field.</summary>
    /// <param name="state">The state to evaluate at.</param>
    /// <param name="field">The field.</param>
    /// <param name="chargeToMass">Charge divided by mass, in coulombs per kilogram.</param>
    /// <param name="timeSeconds">The instant to sample a driven field at.</param>
    /// <returns>The derivative.</returns>
    public static PhaseDerivative Derivative(
        in PhaseState state, IElectrostaticField field, double chargeToMass, double timeSeconds = 0.0)
    {
        var side = SideOf(in state, field);
        return Derivative(in state, field, chargeToMass, timeSeconds, in side);
    }

    /// <summary>
    /// The point whose side of each magnet face a step from <paramref name="state"/> takes its
    /// B from: a hair ahead of the ion along its velocity.
    /// </summary>
    /// <remarks>
    /// Ahead rather than at the ion because the ion is ON a face exactly when the integrator
    /// has just landed there, and a point on a face belongs to the box (a box contains its
    /// boundary) whichever way the ion is going: leaving a magnet, the next step would carry
    /// the magnet's B out into the field-free space beyond - measured as a 2 mm magnet kicking
    /// an ion 44 per cent too hard. A millionth of a millionth of the position's scale ahead
    /// resolves which side the ion is moving into, and is far too small to put an ion that is
    /// anywhere else on the wrong side of anything. Without a magnet nothing reads it.
    /// </remarks>
    private static Vec3 SideOf(in PhaseState state, IElectrostaticField field)
    {
        var position = state.Position;

        if (field is not Einzel.Fields.Magnetic.IMagneticField)
        {
            return position;
        }

        var speed = state.Speed;

        if (!(speed > 0.0))
        {
            return position;
        }

        var scale = Math.Max(
            Math.Max(Math.Abs(position.X), Math.Abs(position.Y)), Math.Max(Math.Abs(position.Z), 1e-6));

        return position + (state.Velocity * (1e-12 * scale / speed));
    }

    /// <summary>
    /// The equation of motion, with B taken from the side of each magnet face that
    /// <paramref name="magneticSide"/> is on.
    /// </summary>
    /// <param name="state">The state to evaluate at.</param>
    /// <param name="field">The field.</param>
    /// <param name="chargeToMass">Charge divided by mass, in coulombs per kilogram.</param>
    /// <param name="timeSeconds">The instant to sample a driven field at.</param>
    /// <param name="magneticSide">The point whose side of each magnetic jump applies.</param>
    /// <returns>The derivative.</returns>
    public static PhaseDerivative Derivative(
        in PhaseState state, IElectrostaticField field, double chargeToMass, double timeSeconds, in Vec3 magneticSide) =>
        Derivative(in state, field, chargeToMass, timeSeconds, in magneticSide, before: false);

    /// <summary>The derivative, with a driven field asked at an instant or as it is approached from before.</summary>
    /// <param name="state">The state to evaluate at.</param>
    /// <param name="field">The field.</param>
    /// <param name="chargeToMass">Charge divided by mass, in coulombs per kilogram.</param>
    /// <param name="timeSeconds">The instant to sample a driven field at.</param>
    /// <param name="before">
    /// Whether a driven field is asked for the phase ending at <paramref name="timeSeconds"/>, a
    /// switch it reported, rather than the one starting there.
    /// </param>
    /// <returns>The derivative.</returns>
    /// <remarks>For an integrator that evaluates its stages itself, as the packet integrator does.</remarks>
    internal static PhaseDerivative Derivative(
        in PhaseState state, IElectrostaticField field, double chargeToMass, double timeSeconds, bool before)
    {
        var side = SideOf(in state, field);
        return Derivative(in state, field, chargeToMass, timeSeconds, in side, before);
    }

    /// <summary>The derivative, with a driven field sampled at or just before an instant.</summary>
    /// <param name="state">The state to evaluate at.</param>
    /// <param name="field">The field.</param>
    /// <param name="chargeToMass">Charge divided by mass, in coulombs per kilogram.</param>
    /// <param name="timeSeconds">The instant to sample a driven field at.</param>
    /// <param name="magneticSide">The point whose side of each magnetic jump applies.</param>
    /// <param name="before">Whether a driven field is asked as the instant is approached from before.</param>
    /// <returns>The derivative.</returns>
    private static PhaseDerivative Derivative(
        in PhaseState state,
        IElectrostaticField field,
        double chargeToMass,
        double timeSeconds,
        in Vec3 magneticSide,
        bool before)
    {
        var position = state.Position;

        // One branch, predictable, and outside nothing: a static field is by far
        // the common case and pays a type test it will always fail the same way.
        var acceleration = field is ITimeVaryingField driven
            ? before ? driven.ElectricFieldBefore(in position, timeSeconds) : driven.ElectricFieldAt(in position, timeSeconds)
            : field.ElectricFieldAt(in position);

        // The velocity-dependent half of the Lorentz force, F = q (E + v x B). A second
        // type test, failed the same way by every model without a magnet, and nothing else
        // on that path: the arithmetic below is untouched for them, which is what keeps
        // every validated number bit-identical. v x B has the dimension of an electric field
        // (m/s times tesla is V/m), so it is added before the one multiplication by q/m.
        // Non-relativistic, as the rest of transport is: no gamma, here or anywhere.
        if (field is Einzel.Fields.Magnetic.IMagneticField magnetic)
        {
            acceleration += Vec3.Cross(state.Velocity, magnetic.MagneticFieldAt(in position, in magneticSide));
        }

        return new PhaseDerivative(state.Velocity, acceleration * chargeToMass);
    }

    private static PhaseState Offset(
        in PhaseState origin,
        double h,
        params ReadOnlySpan<(double Weight, PhaseDerivative Derivative)> stages)
    {
        var position = origin.Position;
        var velocity = origin.Velocity;

        foreach (var (weight, derivative) in stages)
        {
            var scale = h * weight;
            position += derivative.Velocity * scale;
            velocity += derivative.Acceleration * scale;
        }

        return new PhaseState(position, velocity);
    }
}
