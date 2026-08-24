namespace Einzel.Transport.Integration;

/// <summary>
/// Tolerances and limits for a trajectory integration.
/// </summary>
/// <remarks>
/// Spec section 11 requires per-step error control "against the run's accuracy
/// class". Stage 1 exposes the tolerances directly; binding them to a declared
/// accuracy class is the job of the run configuration, once models exist.
/// </remarks>
public sealed record IntegrationSettings
{
    /// <summary>
    /// Relative tolerance applied to both position and velocity. The default is
    /// tight enough that the ACC-1 flight-time budget is dominated by neither the
    /// controller nor round-off.
    /// </summary>
    public double RelativeTolerance { get; init; } = 1e-11;

    /// <summary>
    /// Absolute position tolerance, in metres. Sets the floor near a coordinate
    /// zero crossing, where the relative term vanishes.
    /// </summary>
    public double AbsolutePositionTolerance { get; init; } = 1e-13;

    /// <summary>
    /// Absolute velocity tolerance, in metres per second. Its floor matters most
    /// at a turning point, where the speed passes through zero.
    /// </summary>
    public double AbsoluteVelocityTolerance { get; init; } = 1e-9;

    /// <summary>
    /// Initial step size, in seconds. Zero selects a step from the local
    /// deceleration timescale; the controller corrects a poor guess within a few
    /// steps either way.
    /// </summary>
    public double InitialStep { get; init; }

    /// <summary>Largest permitted step, in seconds.</summary>
    public double MaximumStep { get; init; } = double.PositiveInfinity;

    /// <summary>
    /// Smallest permitted step, in seconds. Reaching it ends the run with
    /// <see cref="TrajectoryOutcome.StepSizeUnderflow"/> rather than grinding.
    /// </summary>
    public double MinimumStep { get; init; } = 1e-18;

    /// <summary>
    /// Step cap near a turning point, as a fraction of the time needed to change
    /// the velocity by the ion's characteristic speed. Zero disables the cap.
    /// </summary>
    /// <remarks>
    /// Spec section 11: "Turning points get forced step refinement. The velocity
    /// minimum inside a mirror is where relative timing error is largest and where
    /// position-error controllers under-refine." The mechanism is worth stating
    /// plainly. Near turnaround the ion barely moves, so a controller weighting
    /// position error sees a small change and happily lengthens the step — while
    /// the arrival time, which is what a TOF actually measures, is determined
    /// precisely there. The cap is applied whenever the ion is decelerating,
    /// which in a mirror is the whole penetration.
    /// </remarks>
    public double TurningPointStepFactor { get; init; } = 0.01;

    /// <summary>
    /// Whether to advance exactly through field-free regions instead of
    /// integrating them. Spec section 11 requires this; the switch exists so a
    /// test can measure what it buys.
    /// </summary>
    public bool UseAnalyticDrift { get; init; } = true;

    /// <summary>Ceiling on accepted steps, as a runaway guard.</summary>
    public int MaximumSteps { get; init; } = 20_000_000;

    /// <summary>
    /// Wall-clock ceiling on simulated flight, in seconds. Infinite by default;
    /// required when the trajectory has no stop condition.
    /// </summary>
    public double MaximumFlightTime { get; init; } = double.PositiveInfinity;

    /// <summary>Controller safety factor on the proposed step size.</summary>
    public double SafetyFactor { get; init; } = 0.9;

    /// <summary>Largest factor by which one step may grow over its predecessor.</summary>
    public double MaximumStepGrowth { get; init; } = 5.0;

    /// <summary>Smallest factor by which one step may shrink after a rejection.</summary>
    public double MinimumStepShrink { get; init; } = 0.1;
}

/// <summary>Why an integration stopped.</summary>
public enum TrajectoryOutcome
{
    /// <summary>The stop condition was met and the final state sits on it.</summary>
    StopConditionMet,

    /// <summary>The flight-time ceiling was reached.</summary>
    MaximumFlightTimeReached,

    /// <summary>The step ceiling was reached. The result is incomplete.</summary>
    MaximumStepsExceeded,

    /// <summary>
    /// The controller demanded a step below the floor. Usually a discontinuity
    /// the field did not declare, or a tolerance tighter than round-off allows.
    /// </summary>
    StepSizeUnderflow,
}
