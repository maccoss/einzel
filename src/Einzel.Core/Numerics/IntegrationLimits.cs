namespace Einzel.Core.Numerics;

/// <summary>What the trajectory integrator can resolve.</summary>
/// <remarks>
/// <para>
/// The default of <c>IntegrationSettings.MinimumStep</c>, which is what governs a flight: a step
/// cut shorter than it is refused as an underflow, except where what is left is the flight's
/// ceiling or a sequencer switch, which the integrator then reaches without a step.
/// </para>
/// <para>
/// Validation does not use it. A sequence phase shorter than the smallest step was once refused at
/// its duration, because such a leg underflowed at once; now the integrator reaches a switch or a
/// ceiling closer than that step, such a phase runs, and only a phase of no length is refused.
/// </para>
/// </remarks>
public static class IntegrationLimits
{
    /// <summary>The smallest step the trajectory integrator takes by default, in seconds.</summary>
    public const double MinimumStepSeconds = 1e-18;
}
