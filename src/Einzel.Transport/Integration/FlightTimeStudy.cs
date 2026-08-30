using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Fields;

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
    /// <param name="collisions">
    /// Makes a fresh collision sampler for each refinement level, or null for a
    /// flight in vacuum.
    /// </param>
    /// <remarks>
    /// <para>
    /// In a gas the interval this study produces is <em>not</em> the uncertainty on
    /// the answer. It measures how far the integrator is from its own limit, and a
    /// collisional flight time also carries a stochastic spread that refining the
    /// tolerance does nothing to. Worse, the two are not independent: a slightly
    /// different state at a scheduling instant maps the same uniform draw to a
    /// slightly different collision time, and that difference compounds.
    /// </para>
    /// <para>
    /// So a collisional single-ion flight time is qualified where it is reported,
    /// and the number with an honest interval is the ensemble one.
    /// </para>
    /// </remarks>
    public static FlightTimeStudyResult Run(
        PhaseState initialState,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction stopWhenNegative,
        int refinements = 3,
        double refinementRatio = 10.0,
        Func<Collisions.CollisionSampler>? collisions = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stopWhenNegative);
        ArgumentOutOfRangeException.ThrowIfLessThan(refinements, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refinementRatio, 1.0);

        var runs = new List<TrajectoryResult>(refinements);
        var warnings = new List<ValidityWarning>();

        // Accumulated across every refinement rather than read off the last one.
        // Each level gets a fresh sampler, so keeping only the final one would drop
        // an exceeded bound that happened at a coarser tolerance - which is the same
        // failure this reporting exists to fix, one level in. A flag that is true on
        // any level describes the study.
        var boundExceeded = false;
        var sampledOutsideFlow = false;
        var sampledOutsideDensity = false;

        for (var level = 0; level < refinements; level++)
        {
            var scale = Math.Pow(refinementRatio, -level);

            // The VELOCITY floor is held; the relative tolerance and the position floor
            // refine. The asymmetry is measured rather than assumed: tightening the
            // velocity floor alone reproduces the failure below, and tightening the
            // position floor alone does not.
            //
            // A floor states what is negligible, and for velocity that does not change
            // because a more accurate answer was asked for. Scaled, the deepest rung
            // reached 1e-11 m/s - ten picometres per second, against thermal speeds of
            // hundreds of metres - which is not an accuracy requirement but round-off.
            // And it is load-bearing: this floor is what stops ErrorNorm being a
            // position-error controller, which section 11's findings turn on.
            //
            // What scaling it cost: an ion at rest when a field switches on could not be
            // integrated at all. The normalised velocity error is then unsatisfiable at
            // any step size, so the step halves 63 times and reports StepSizeUnderflow -
            // a numerical failure standing in for ordinary physics, the same sentence
            // the collision path already carries. A pulsed-extraction trap is the
            // archetype, and it could not be run.
            //
            // The reflectron's flight time is bit-identical either way; its interval
            // narrows seventeenfold, from a saturated floor to a measured residual.
            var refined = settings with
            {
                RelativeTolerance = settings.RelativeTolerance * scale,
                AbsolutePositionTolerance = settings.AbsolutePositionTolerance * scale,
            };

            // A fresh sampler per refinement, from the same seed, so each level
            // draws the same sequence of uniforms and the comparison is of the
            // integrator rather than of the dice.
            var sampler = collisions?.Invoke();

            runs.Add(TrajectoryIntegrator.Integrate(
                initialState, species, field, refined, stopWhenNegative, collisions: sampler));

            // Read here, where the sampler that produced them is still in scope, so
            // there is no later place for them to be lost.
            boundExceeded |= sampler is { BoundExceeded: true };
            sampledOutsideFlow |= sampler is { SampledOutsideFlow: true };
            sampledOutsideDensity |= sampler is { SampledOutsideDensity: true };
        }

        // What the collision samplers learned about their own validity. These used to
        // be computed and read by nothing, which is a pattern this project has now hit
        // four times - a biased collision rate looks exactly like a correct one, and so
        // does a gas nobody imported. The first fix read only the last refinement's
        // sampler, which is the same loss one level in.
        //
        // The density one was added with the pressure field and was, on the first
        // draft, dropped in exactly the same place as the two above it. Adding a
        // quantity to a sampler is not the same as reporting it, and the shortest
        // spelling remains the one that loses it.
        if (boundExceeded)
        {
            warnings.Add(new ValidityWarning(
                "collisions.rate-underestimated",
                "a sampled relative speed exceeded the null-collision bound, so the collision rate "
                + "was too low for at least one event and every result that depends on it is "
                + "biased. The bound is the true rate plus a fixed headroom in thermal speeds, and "
                + "an ion far faster than thermal outruns it",
                WarningSeverity.ValidityViolation));
        }

        if (sampledOutsideDensity)
        {
            warnings.Add(new ValidityWarning(
                "gas.pressure-extrapolated",
                "at least one collision was drawn outside the imported pressure field, where the "
                + "density is the edge value continued rather than anything that was measured. A "
                + "pressure gradient is steepest at the ends of a pumped region, which is exactly "
                + "where continuing the last plane is most likely to be wrong - and every "
                + "collision rate, mean free path and mobility there is scaled by it",
                WarningSeverity.Qualified));
        }

        if (sampledOutsideFlow)
        {
            warnings.Add(new ValidityWarning(
                "gas.flow-extrapolated",
                "at least one collision was drawn outside the imported velocity field, where the "
                + "flow is the edge value continued rather than anything that was measured. That "
                + "is right for a stream and wrong for the end of a jet, and the samples cannot "
                + "say which",
                WarningSeverity.Qualified));
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
        var (residual, atResolution) = ConvergenceResidual(runs);

        if (atResolution)
        {
            warnings.Add(new ValidityWarning(
                "convergence.at-resolution",
                $"the two finest refinements agreed to within one unit in the last place, so this "
                + $"interval is a floor set by double precision rather than a measured convergence. "
                + $"The value is at least as good as {residual:G3} s says; how much better is not "
                + "knowable from a comparison of two numbers this close",
                WarningSeverity.Provenance));
        }

        var observedOrder = ObservedOrder(runs, refinementRatio);

        // An order fitted to differences at round-off is fitting noise. The guard
        // used to key on a residual of exactly zero, so an unperturbed model was
        // quiet and any perturbation of it tripped the warning on every draw - which
        // two independent readers reported as spurious before any test did.
        if (!atResolution && double.IsFinite(observedOrder) && observedOrder < NominalOrder * 0.5)
        {
            warnings.Add(new ValidityWarning(
                "CONVERGENCE_ORDER_BELOW_NOMINAL",
                $"observed order {observedOrder:G3} against nominal {NominalOrder:G3} on tolerance "
                + "refinement: the flight time stopped improving as fast as the tolerance tightened, "
                + "which is what a floor other than the integrator looks like. In a solved field it "
                + "is usually the interpolation error, and the fix is a finer grid rather than a "
                + "tighter tolerance",
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

    /// <summary>
    /// The convergence residual a ladder of runs supports, and whether it collapsed.
    /// </summary>
    /// <param name="runs">The runs, coarsest first, at least two.</param>
    /// <returns>
    /// The half-width to report, and whether it is a floor set by double precision
    /// rather than a measured convergence.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="runs"/> is null.</exception>
    /// <exception cref="ArgumentException">There are fewer than two runs.</exception>
    /// <remarks>
    /// <para>
    /// The residual is the difference between the two finest runs. When they agree to
    /// within one unit in the last place the pair says nothing about how far either is
    /// from the truth, so it falls back to the whole ladder, and to one ulp if even that
    /// collapses.
    /// </para>
    /// <para>
    /// <b>A zero here is what GRD-1 exists to prevent.</b> It is not "no uncertainty" -
    /// it is an uncertainty smaller than the comparison can see - and it printed as
    /// "+/- 0", which a reader takes for an exact number and a paper cannot defend. An
    /// agent asked to quote a defensible result refused one and measured its own ladder
    /// instead, which is the right instinct and should not have been necessary.
    /// </para>
    /// <para>
    /// Named and public because it is the rule rather than a step: buried inside the
    /// study it could only be exercised by finding a model whose rungs happen to agree
    /// to the bit, and the model that used to do that only did so because the ladder was
    /// over-tightening a floor it no longer touches.
    /// </para>
    /// </remarks>
    public static (double ResidualSi, bool AtResolution) ConvergenceResidual(
        IReadOnlyList<TrajectoryResult> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Count < 2)
        {
            throw new ArgumentException(
                $"a convergence residual needs at least two runs and was given {runs.Count}",
                nameof(runs));
        }

        var finest = runs[^1];
        var residual = Math.Abs(runs[^2].FlightTimeSeconds - finest.FlightTimeSeconds);

        // One unit in the last place of the answer: the smallest difference this
        // arithmetic can express, and therefore the smallest uncertainty it can
        // honestly claim.
        var resolution = Math.Abs(finest.FlightTimeSeconds) is var magnitude and > 0.0
            ? Math.BitIncrement(magnitude) - magnitude
            : double.Epsilon;

        return residual <= resolution
            ? (Math.Max(
                Math.Abs(runs[0].FlightTimeSeconds - finest.FlightTimeSeconds), resolution), true)
            : (residual, false);
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
