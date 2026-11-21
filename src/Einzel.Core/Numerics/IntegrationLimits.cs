namespace Einzel.Core.Numerics;

/// <summary>What the trajectory integrator can resolve, stated where validation can read it too.</summary>
/// <remarks>
/// In Core rather than beside the integrator because validation runs before any transport is
/// built and must refuse what the integrator would not be able to take: a sequence phase shorter
/// than the smallest step reaches the run as a leg that underflows at once.
/// </remarks>
public static class IntegrationLimits
{
    /// <summary>
    /// The smallest step the integrator takes, in seconds; the default of its settings and the
    /// shortest phase validation accepts.
    /// </summary>
    public const double MinimumStepSeconds = 1e-18;
}
