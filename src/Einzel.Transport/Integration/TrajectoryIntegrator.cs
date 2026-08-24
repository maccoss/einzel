using Einzel.Core.Geometry;
using Einzel.Core.Numerics;
using Einzel.Fields;

namespace Einzel.Transport.Integration;

/// <summary>
/// A scalar function of the ion's state whose descending zero crossing ends the
/// integration.
/// </summary>
/// <param name="state">The current state.</param>
/// <returns>
/// A value that is positive while the flight continues and becomes negative once
/// the ion has passed the stopping surface.
/// </returns>
/// <remarks>
/// A signed distance to a detector plane is the usual case. The integrator lands
/// on the zero exactly rather than at the end of whichever step happened to cross
/// it, which matters because the crossing time is the measurement.
/// </remarks>
public delegate double TrajectoryStopFunction(in PhaseState state);

/// <summary>
/// Adaptive-step trajectory integration through a static electric field.
/// </summary>
/// <remarks>
/// <para>
/// The scalar reference implementation. CMP-1: it is never deleted or allowed to
/// rot, because every SIMD or GPU path is tested against it.
/// </para>
/// <para>
/// Five things here are specified rather than chosen, and each addresses a
/// distinct way a flight time goes wrong: Dormand-Prince 5(4) with per-step error
/// control for truncation error, Neumaier compensation for accumulation error in
/// the time total, analytic advance through field-free regions so most of a
/// multi-reflection path contributes no integration error at all, exact landing
/// on declared field discontinuities so no step straddles one, and a forced step
/// cap while decelerating so the turning point is resolved.
/// </para>
/// <para>
/// The inner loop allocates nothing. Spec section 22 lists GC pauses in long runs
/// as a risk that is "preventable with allocation discipline from the start", and
/// <c>AllocationTests</c> asserts that allocation does not grow with step count.
/// </para>
/// </remarks>
public static class TrajectoryIntegrator
{
    // Below this, an analytic drift advance is not worth taking and would risk
    // stalling against a boundary the ion is already sitting on. At 1e-13 m the
    // skipped time is under 1e-17 s for any ion of interest, twelve orders below
    // the ACC-1 budget on a microsecond flight.
    private const double DegenerateDriftDistance = 1e-13;

    /// <summary>Integrates one ion until the stop condition or a limit is reached.</summary>
    /// <param name="initialState">Starting position and velocity, in SI.</param>
    /// <param name="species">The ion's mass and charge.</param>
    /// <param name="field">The field to integrate through.</param>
    /// <param name="settings">Tolerances and limits.</param>
    /// <param name="stopWhenNegative">
    /// Optional stopping surface. When null, <see cref="IntegrationSettings.MaximumFlightTime"/>
    /// must be finite.
    /// </param>
    /// <param name="recorder">
    /// Optional trajectory sampler for rendering and export (TRJ-1). Supplying
    /// one makes the run allocate in proportion to the sample count; omitting it
    /// keeps the inner loop allocation-free.
    /// </param>
    /// <returns>The trajectory outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> or <paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentException">The run is unbounded: no stop condition and no flight-time ceiling.</exception>
    public static TrajectoryResult Integrate(
        PhaseState initialState,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction? stopWhenNegative = null,
        TrajectoryRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settings);

        if (stopWhenNegative is null && double.IsPositiveInfinity(settings.MaximumFlightTime))
        {
            throw new ArgumentException(
                "an integration needs either a stop condition or a finite MaximumFlightTime",
                nameof(settings));
        }

        // Hoisted out of the loop: constructing this per step would allocate a
        // closure per step, which is exactly what the allocation discipline in
        // spec section 11 forbids.
        var boundarySurface = BoundarySurfaceFor(field);

        var chargeToMass = species.ChargeToMassSi;
        var state = initialState;
        var time = default(CompensatedSum);
        long fieldEvaluations = 0;
        var analyticDistance = 0.0;
        var accepted = 0;
        var rejected = 0;

        var initialEnergy = TotalEnergy(in state, species, field, ref fieldEvaluations);
        var energyScale = Math.Abs(initialEnergy);
        var maximumEnergyDrift = 0.0;

        // The speed the ion would have at zero potential. In a mirror this is the
        // drift-region speed, which is the natural yardstick for the turning-point
        // cap because it does not collapse as the ion slows.
        var characteristicSpeed = energyScale > 0.0
            ? Math.Sqrt(2.0 * energyScale / species.MassSi)
            : state.Speed;

        recorder?.Offer(0.0, in state, force: true);

        var derivative = DormandPrince54.Derivative(in state, field, chargeToMass);
        fieldEvaluations++;

        var step = InitialStep(settings, in derivative, characteristicSpeed);
        var outcome = TrajectoryOutcome.MaximumStepsExceeded;

        while (accepted < settings.MaximumSteps)
        {
            if (settings.UseAnalyticDrift
                && TryAnalyticDrift(
                    ref state, ref time, field, settings, stopWhenNegative,
                    recorder, ref analyticDistance, out var stoppedOnDrift))
            {
                derivative = DormandPrince54.Derivative(in state, field, chargeToMass);
                fieldEvaluations++;

                if (stoppedOnDrift)
                {
                    outcome = TrajectoryOutcome.StopConditionMet;
                    break;
                }

                if (time.Total >= settings.MaximumFlightTime)
                {
                    outcome = TrajectoryOutcome.MaximumFlightTimeReached;
                    break;
                }

                continue;
            }

            step = ApplyStepCaps(step, settings, in state, in derivative, characteristicSpeed);
            step = Math.Min(step, settings.MaximumFlightTime - time.Total);

            if (step < settings.MinimumStep)
            {
                outcome = time.Total >= settings.MaximumFlightTime
                    ? TrajectoryOutcome.MaximumFlightTimeReached
                    : TrajectoryOutcome.StepSizeUnderflow;
                break;
            }

            DormandPrince54.Step(
                in state, in derivative, step, field, chargeToMass,
                out var candidate, out var errorPosition, out var errorVelocity, out var candidateDerivative);
            fieldEvaluations += 6;

            var error = ErrorNorm(in state, in candidate, in errorPosition, in errorVelocity, settings);

            if (error > 1.0)
            {
                rejected++;
                step *= Math.Max(settings.MinimumStepShrink, settings.SafetyFactor * Math.Pow(error, -0.2));
                continue;
            }

            // The step is accurate enough. Two surfaces can still cut it short,
            // and the earlier one wins: the field's own discontinuity, which the
            // ion must land on so that no step straddles it, and the stopping
            // surface, which ends the flight.
            var boundaryStep = BoundaryLandingStep(
                in state, in derivative, in candidate, step, field, chargeToMass,
                boundarySurface, ref fieldEvaluations);

            var stopStep = StopLandingStep(
                in state, in derivative, in candidate, step, field, chargeToMass,
                stopWhenNegative, ref fieldEvaluations);

            if (double.IsFinite(stopStep) && stopStep <= boundaryStep)
            {
                DormandPrince54.Step(
                    in state, in derivative, stopStep, field, chargeToMass,
                    out var landed, out _, out _, out _);
                fieldEvaluations += 6;

                time.Add(stopStep);
                state = landed;
                accepted++;
                recorder?.Offer(time.Total, in state, force: true);
                TrackEnergyDrift(
                    in state, species, field, initialEnergy, energyScale, ref maximumEnergyDrift, ref fieldEvaluations);
                outcome = TrajectoryOutcome.StopConditionMet;
                break;
            }

            if (double.IsFinite(boundaryStep))
            {
                DormandPrince54.Step(
                    in state, in derivative, boundaryStep, field, chargeToMass,
                    out var onBoundary, out _, out _, out _);
                fieldEvaluations += 6;

                time.Add(boundaryStep);
                state = onBoundary;
                accepted++;
                recorder?.Offer(time.Total, in state, force: true);
                TrackEnergyDrift(
                    in state, species, field, initialEnergy, energyScale, ref maximumEnergyDrift, ref fieldEvaluations);

                // The field on the far side is a different function, so the
                // cached derivative is stale.
                derivative = DormandPrince54.Derivative(in state, field, chargeToMass);
                fieldEvaluations++;
                continue;
            }

            time.Add(step);
            state = candidate;
            derivative = candidateDerivative;
            accepted++;
            recorder?.Offer(time.Total, in state, force: false);

            TrackEnergyDrift(
                in state, species, field, initialEnergy, energyScale, ref maximumEnergyDrift, ref fieldEvaluations);

            if (time.Total >= settings.MaximumFlightTime)
            {
                outcome = TrajectoryOutcome.MaximumFlightTimeReached;
                break;
            }

            var growth = error > 0.0
                ? settings.SafetyFactor * Math.Pow(error, -0.2)
                : settings.MaximumStepGrowth;

            step *= Math.Clamp(growth, settings.MinimumStepShrink, settings.MaximumStepGrowth);
        }

        return new TrajectoryResult
        {
            FinalState = state,
            FlightTimeSeconds = time.Total,
            TimeCompensation = time.Compensation,
            Outcome = outcome,
            AcceptedSteps = accepted,
            RejectedSteps = rejected,
            FieldEvaluations = fieldEvaluations,
            AnalyticDriftDistance = analyticDistance,
            MaximumRelativeEnergyDrift = maximumEnergyDrift,
        };
    }

    private static TrajectoryStopFunction BoundarySurfaceFor(IElectrostaticField field) =>
        (in PhaseState state) =>
        {
            var position = state.Position;
            return field.SignedDistanceToDiscontinuity(in position);
        };

    private static double BoundaryLandingStep(
        in PhaseState state,
        in PhaseDerivative derivative,
        in PhaseState candidate,
        double step,
        IElectrostaticField field,
        double chargeToMass,
        TrajectoryStopFunction boundarySurface,
        ref long fieldEvaluations)
    {
        var before = boundarySurface(in state);
        fieldEvaluations++;

        // Smooth field, or the ion is sitting exactly on the boundary having just
        // landed on it. In the second case it is leaving, and the next step will
        // see a non-zero value.
        if (!double.IsFinite(before) || before == 0.0)
        {
            return double.PositiveInfinity;
        }

        var after = boundarySurface(in candidate);
        fieldEvaluations++;

        if (!double.IsFinite(after) || Math.Sign(after) == Math.Sign(before))
        {
            return double.PositiveInfinity;
        }

        return LandingStep(
            in state, in derivative, step, field, chargeToMass, boundarySurface, before, ref fieldEvaluations);
    }

    private static double StopLandingStep(
        in PhaseState state,
        in PhaseDerivative derivative,
        in PhaseState candidate,
        double step,
        IElectrostaticField field,
        double chargeToMass,
        TrajectoryStopFunction? stopWhenNegative,
        ref long fieldEvaluations)
    {
        if (stopWhenNegative is null)
        {
            return double.PositiveInfinity;
        }

        var before = stopWhenNegative(in state);

        if (before <= 0.0 || stopWhenNegative(in candidate) > 0.0)
        {
            return double.PositiveInfinity;
        }

        return LandingStep(
            in state, in derivative, step, field, chargeToMass, stopWhenNegative, before, ref fieldEvaluations);
    }

    /// <summary>
    /// Finds the step size at which a surface function crosses zero, by bisection
    /// on real Runge-Kutta steps.
    /// </summary>
    /// <remarks>
    /// Bisection rather than a secant method: each probe is a full step from the
    /// same starting state, so the landing carries the integrator's own accuracy
    /// rather than an interpolant's, and bisection cannot be defeated by the
    /// curvature near a turning point. It costs about fifty probes per landing,
    /// which against two landings per flight is not worth optimising.
    /// </remarks>
    private static double LandingStep(
        in PhaseState start,
        in PhaseDerivative startDerivative,
        double bracketingStep,
        IElectrostaticField field,
        double chargeToMass,
        TrajectoryStopFunction surface,
        double valueAtStart,
        ref long fieldEvaluations)
    {
        var low = 0.0;
        var high = bracketingStep;
        var startSign = Math.Sign(valueAtStart);

        for (var i = 0; i < 80; i++)
        {
            var mid = 0.5 * (low + high);

            if (mid <= low || mid >= high)
            {
                break;
            }

            DormandPrince54.Step(
                in start, in startDerivative, mid, field, chargeToMass, out var probe, out _, out _, out _);
            fieldEvaluations += 6;

            var value = surface(in probe);

            if (value == 0.0)
            {
                return mid;
            }

            if (Math.Sign(value) == startSign)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return high;
    }

    private static double InitialStep(
        IntegrationSettings settings, in PhaseDerivative derivative, double characteristicSpeed)
    {
        if (settings.InitialStep > 0.0)
        {
            return settings.InitialStep;
        }

        var acceleration = derivative.Acceleration.Length;

        // Field-free at the start: analytic drift will carry the ion to the first
        // boundary, so the guess only has to be harmless.
        var guess = acceleration > 0.0 && characteristicSpeed > 0.0
            ? 1e-3 * characteristicSpeed / acceleration
            : 1e-9;

        return Math.Min(guess, settings.MaximumStep);
    }

    private static double ApplyStepCaps(
        double step,
        IntegrationSettings settings,
        in PhaseState state,
        in PhaseDerivative derivative,
        double characteristicSpeed)
    {
        step = Math.Min(step, settings.MaximumStep);

        if (settings.TurningPointStepFactor <= 0.0)
        {
            return step;
        }

        var acceleration = derivative.Acceleration.Length;

        if (acceleration <= 0.0)
        {
            return step;
        }

        // Only while decelerating. Accelerating away from a mirror is the
        // well-behaved half of the trajectory and does not need the cap.
        if (Vec3.Dot(state.Velocity, derivative.Acceleration) >= 0.0)
        {
            return step;
        }

        return Math.Min(step, settings.TurningPointStepFactor * characteristicSpeed / acceleration);
    }

    private static bool TryAnalyticDrift(
        ref PhaseState state,
        ref CompensatedSum time,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction? stopWhenNegative,
        TrajectoryRecorder? recorder,
        ref double analyticDistance,
        out bool stopped)
    {
        stopped = false;

        var speed = state.Speed;

        if (speed <= 0.0)
        {
            return false;
        }

        var direction = state.Velocity / speed;
        var position = state.Position;
        var run = field.FieldFreeRunLength(in position, in direction);

        if (run <= DegenerateDriftDistance)
        {
            return false;
        }

        var remaining = settings.MaximumFlightTime - time.Total;

        if (remaining <= 0.0)
        {
            return false;
        }

        if (!double.IsPositiveInfinity(remaining))
        {
            run = Math.Min(run, remaining * speed);
        }

        if (double.IsPositiveInfinity(run))
        {
            // Unbounded field-free flight with no ceiling. The caller was required
            // to supply a stop condition, so bracket against it instead.
            run = BracketUnboundedDrift(in state, in direction, speed, stopWhenNegative!);
        }

        if (stopWhenNegative is not null)
        {
            var atEnd = Drift(in state, in direction, run);

            if (stopWhenNegative(in atEnd) <= 0.0)
            {
                // Straight-line motion, so the crossing distance can be found to
                // machine precision without a single field evaluation.
                run = BisectDrift(in state, in direction, run, stopWhenNegative);
                stopped = true;
            }
        }

        // Both ends of a straight segment, so a figure keeps its endpoints even
        // when the whole drift is a single advance.
        recorder?.Offer(time.Total, in state, force: true);

        state = Drift(in state, in direction, run);
        time.Add(run / speed);
        analyticDistance += run;

        recorder?.Offer(time.Total, in state, force: true);
        return true;
    }

    private static PhaseState Drift(in PhaseState state, in Vec3 direction, double distance) =>
        new(state.Position + (direction * distance), state.Velocity);

    private static double BracketUnboundedDrift(
        in PhaseState state, in Vec3 direction, double speed, TrajectoryStopFunction stop)
    {
        // Grow geometrically until the surface is passed. Field-free and straight,
        // so this costs arithmetic only.
        var run = Math.Max(1.0, speed * 1e-9);

        for (var i = 0; i < 200; i++)
        {
            var probe = Drift(in state, in direction, run);

            if (stop(in probe) <= 0.0)
            {
                return run;
            }

            run *= 2.0;
        }

        return run;
    }

    private static double BisectDrift(
        in PhaseState state, in Vec3 direction, double upper, TrajectoryStopFunction stop)
    {
        var low = 0.0;
        var high = upper;

        // Straight-line geometry: 80 halvings takes the bracket to the last bit of
        // a double regardless of the starting span.
        for (var i = 0; i < 80; i++)
        {
            var mid = 0.5 * (low + high);

            if (mid <= low || mid >= high)
            {
                break;
            }

            var probe = Drift(in state, in direction, mid);

            if (stop(in probe) <= 0.0)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return high;
    }

    private static double ErrorNorm(
        in PhaseState state,
        in PhaseState candidate,
        in Vec3 errorPosition,
        in Vec3 errorVelocity,
        IntegrationSettings settings)
    {
        var sum = 0.0;
        var atolX = settings.AbsolutePositionTolerance;
        var atolV = settings.AbsoluteVelocityTolerance;
        var rtol = settings.RelativeTolerance;

        sum += Component(errorPosition.X, state.Position.X, candidate.Position.X, atolX, rtol);
        sum += Component(errorPosition.Y, state.Position.Y, candidate.Position.Y, atolX, rtol);
        sum += Component(errorPosition.Z, state.Position.Z, candidate.Position.Z, atolX, rtol);
        sum += Component(errorVelocity.X, state.Velocity.X, candidate.Velocity.X, atolV, rtol);
        sum += Component(errorVelocity.Y, state.Velocity.Y, candidate.Velocity.Y, atolV, rtol);
        sum += Component(errorVelocity.Z, state.Velocity.Z, candidate.Velocity.Z, atolV, rtol);

        return Math.Sqrt(sum / 6.0);

        static double Component(double error, double before, double after, double absolute, double relative)
        {
            var scale = absolute + (relative * Math.Max(Math.Abs(before), Math.Abs(after)));
            var scaled = error / scale;
            return scaled * scaled;
        }
    }

    private static double TotalEnergy(
        in PhaseState state, IonSpecies species, IElectrostaticField field, ref long fieldEvaluations)
    {
        var position = state.Position;
        fieldEvaluations++;
        return (0.5 * species.MassSi * state.Velocity.LengthSquared)
            + (species.ChargeSi * field.PotentialAt(in position));
    }

    private static void TrackEnergyDrift(
        in PhaseState state,
        IonSpecies species,
        IElectrostaticField field,
        double initialEnergy,
        double energyScale,
        ref double maximumDrift,
        ref long fieldEvaluations)
    {
        if (energyScale <= 0.0)
        {
            return;
        }

        var energy = TotalEnergy(in state, species, field, ref fieldEvaluations);
        var drift = Math.Abs(energy - initialEnergy) / energyScale;

        if (drift > maximumDrift)
        {
            maximumDrift = drift;
        }
    }
}
