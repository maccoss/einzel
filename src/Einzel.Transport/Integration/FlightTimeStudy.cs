using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Transport.Integration;

/// <summary>
/// A flight time reported with the convergence evidence behind it.
/// </summary>
/// <param name="FlightTime">The GRD-1 envelope: value, interval, evidence, warnings.</param>
/// <param name="Runs">The individual integrations, finest last, for diagnostics.</param>
public sealed record FlightTimeStudyResult(Measured FlightTime, IReadOnlyList<TrajectoryResult> Runs);

/// <summary>
/// Integrates the same trajectory at successively tighter tolerances and reports
/// the flight time with a convergence bound.
/// </summary>
/// <remarks>
/// <para>
/// This is where a raw integration becomes a reportable result. A single run
/// yields a number and no way to know whether it is converged; spec section 19
/// requires "every physics test at two mesh densities and two tolerances,
/// asserting observed convergence order against nominal", and GRD-4 requires
/// validity to be checked rather than assumed. Refining the tolerance and
/// watching the answer stop moving is the cheapest form of both.
/// </para>
/// <para>
/// On what the interval means. For an adaptive integrator with local error
/// control, the global error is expected to fall roughly in proportion to the
/// tolerance, so the nominal order with respect to tolerance refinement is one.
/// The reported half-width is the residual between the two finest runs, which is
/// conservative: Richardson would divide it by the refinement ratio less one.
/// The confidence level is quoted as 1.0 because this is a deterministic
/// convergence bound rather than a statistical interval — an important
/// distinction when the same envelope also carries Class S results, where the
/// interval really is a 95 percent confidence interval over an ensemble.
/// </para>
/// </remarks>
public static class FlightTimeStudy
{
    /// <summary>Nominal convergence order with respect to tolerance refinement.</summary>
    public const double NominalOrder = 1.0;

    /// <summary>ACC-4: energy drift in a static field is budgeted at 1 ppm.</summary>
    private const double EnergyDriftBudget = 1e-6;

    /// <summary>Runs the study.</summary>
    /// <param name="initialState">Starting position and velocity, in SI.</param>
    /// <param name="species">The ion's mass and charge.</param>
    /// <param name="field">The field to integrate through.</param>
    /// <param name="settings">The coarsest settings; tolerances tighten from here.</param>
    /// <param name="stopWhenNegative">The stopping surface.</param>
    /// <param name="refinements">
    /// How many tolerance levels to run. Three is the minimum that yields an
    /// observed order as well as a residual.
    /// </param>
    /// <param name="refinementRatio">Factor by which tolerances tighten each level.</param>
    /// <returns>The flight time and the runs behind it.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Fewer than two refinements, or a ratio not greater than one.
    /// </exception>
    public static FlightTimeStudyResult Run(
        PhaseState initialState,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction stopWhenNegative,
        int refinements = 3,
        double refinementRatio = 10.0)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stopWhenNegative);
        ArgumentOutOfRangeException.ThrowIfLessThan(refinements, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refinementRatio, 1.0);

        var runs = new List<TrajectoryResult>(refinements);
        var warnings = new List<ValidityWarning>();

        for (var level = 0; level < refinements; level++)
        {
            var scale = Math.Pow(refinementRatio, -level);

            var refined = settings with
            {
                RelativeTolerance = settings.RelativeTolerance * scale,
                AbsolutePositionTolerance = settings.AbsolutePositionTolerance * scale,
                AbsoluteVelocityTolerance = settings.AbsoluteVelocityTolerance * scale,
            };

            runs.Add(TrajectoryIntegrator.Integrate(initialState, species, field, refined, stopWhenNegative));
        }

        foreach (var run in runs)
        {
            if (run.Outcome != TrajectoryOutcome.StopConditionMet)
            {
                warnings.Add(new ValidityWarning(
                    "TRAJECTORY_INCOMPLETE",
                    $"an integration ended as {run.Outcome} rather than reaching the stopping surface",
                    WarningSeverity.ValidityViolation));
                break;
            }
        }

        var finest = runs[^1];
        var residual = Math.Abs(runs[^2].FlightTimeSeconds - finest.FlightTimeSeconds);
        var observedOrder = ObservedOrder(runs, refinementRatio);

        if (double.IsFinite(observedOrder) && observedOrder < NominalOrder * 0.5)
        {
            warnings.Add(new ValidityWarning(
                "CONVERGENCE_ORDER_BELOW_NOMINAL",
                $"observed order {observedOrder:G3} against nominal {NominalOrder:G3} on tolerance refinement",
                WarningSeverity.Qualified));
        }

        var energyDrift = runs.Max(r => r.MaximumRelativeEnergyDrift);

        if (energyDrift > EnergyDriftBudget)
        {
            warnings.Add(new ValidityWarning(
                "ENERGY_DRIFT_EXCEEDS_BUDGET",
                $"relative energy drift {energyDrift:G3} exceeds the ACC-4 budget of {EnergyDriftBudget:G3}",
                WarningSeverity.ValidityViolation));
        }

        var value = Quantity.Si(finest.FlightTimeSeconds, Dimension.TimeDimension);
        var halfWidth = Quantity.Si(residual, Dimension.TimeDimension);

        var measured = new Measured(
            value,
            UncertaintyInterval.Symmetric(value, halfWidth, confidenceLevel: 1.0),
            new Evidence.Convergence(
                Measure: "integrator tolerance",
                ObservedOrder: observedOrder,
                NominalOrder: NominalOrder,
                ResidualSi: residual),
            warnings);

        return new FlightTimeStudyResult(measured, runs);
    }

    private static double ObservedOrder(List<TrajectoryResult> runs, double ratio)
    {
        if (runs.Count < 3)
        {
            return double.NaN;
        }

        var coarse = Math.Abs(runs[^3].FlightTimeSeconds - runs[^2].FlightTimeSeconds);
        var fine = Math.Abs(runs[^2].FlightTimeSeconds - runs[^1].FlightTimeSeconds);

        // Already at round-off: the differences carry no order information, and
        // reporting a number derived from noise would be worse than saying so.
        if (fine <= 0.0 || coarse <= 0.0)
        {
            return double.PositiveInfinity;
        }

        return Math.Log(coarse / fine) / Math.Log(ratio);
    }
}
