using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Sweeps;

/// <summary>Which way an objective is better.</summary>
/// <remarks>
/// Stated rather than assumed, because the alternative is a caller remembering to
/// negate a resolving power and eventually not doing so. A sign error in an
/// objective does not throw; it quietly returns the worst design in the box and
/// looks like a result.
/// </remarks>
public enum ObjectiveSense
{
    /// <summary>Smaller is better. Aberration coefficients, spot sizes, envelopes.</summary>
    Minimise,

    /// <summary>Larger is better. Resolving power, transmission, acceptance.</summary>
    Maximise,
}

/// <summary>Which search to run.</summary>
public enum OptimisationAlgorithm
{
    /// <summary>
    /// Nelder-Mead simplex. Spec section 13's choice for small problems: cheap per
    /// iteration, no derivatives, and it copes with an objective that is only
    /// piecewise smooth. It stagnates on ridges, which is what the restarts are
    /// for.
    /// </summary>
    NelderMead,

    /// <summary>
    /// CMA-ES. Spec section 13's choice for larger and rougher problems. It learns
    /// the objective's local covariance, so it follows a curved valley that
    /// Nelder-Mead crawls along, and it is far less troubled by an objective with
    /// numerical noise on it - which every objective here has, since each
    /// evaluation ends in a solve at a finite tolerance.
    /// </summary>
    CmaEs,
}

/// <summary>How a search is run.</summary>
public sealed record OptimisationSettings
{
    /// <summary>Ceiling on objective evaluations.</summary>
    /// <remarks>
    /// A budget rather than an iteration count, because an evaluation is a solve
    /// and the solve is the whole cost. Exhausting it is not an error: the
    /// best-so-far is returned, carrying a non-suppressible warning that says the
    /// search stopped because it ran out rather than because it arrived.
    /// </remarks>
    public int MaximumEvaluations { get; init; } = 300;

    /// <summary>
    /// Convergence tolerance on the parameters, as a fraction of the box.
    /// </summary>
    public double ParameterTolerance { get; init; } = 1e-4;

    /// <summary>
    /// Convergence tolerance on the objective, relative to its spread over the
    /// search so far.
    /// </summary>
    public double ObjectiveTolerance { get; init; } = 1e-8;

    /// <summary>Seed for any stochastic part of the search.</summary>
    /// <remarks>
    /// PRJ-3: a run manifest fully determines its run. CMA-ES samples, so it needs
    /// a seed for the same run to be regenerable; Nelder-Mead is deterministic and
    /// ignores it.
    /// </remarks>
    public int Seed { get; init; } = 1;

    /// <summary>The dimension of the objective, for the reported envelope.</summary>
    /// <remarks>
    /// Dimensionless by default, which covers resolving power, transmission, and
    /// aberration coefficients. An objective in seconds or metres should say so,
    /// since GRD-1 reports it as a quantity and a quantity without its dimension
    /// is exactly the bare number the rule exists to prevent.
    /// </remarks>
    public Dimension ObjectiveDimension { get; init; } = Dimension.Dimensionless;

    /// <summary>How many extra restarts Nelder-Mead may take from its best point.</summary>
    /// <remarks>
    /// A simplex can collapse onto a ridge and report convergence while sitting
    /// nowhere in particular. Restarting from the best vertex with a fresh
    /// full-size simplex either confirms the point or walks away from it, and it
    /// is the cheapest insurance against a false optimum. Ignored by CMA-ES, which
    /// has its own restart story.
    /// </remarks>
    public int Restarts { get; init; } = 2;
}

/// <summary>One improvement in the search, recorded as it happened.</summary>
/// <param name="Evaluation">Which evaluation produced it.</param>
/// <param name="Objective">The objective there, in the caller's sense.</param>
/// <param name="Parameters">The parameter values, in SI.</param>
public sealed record OptimisationStep(
    int Evaluation,
    double Objective,
    IReadOnlyDictionary<string, double> Parameters);

/// <summary>What a search found.</summary>
/// <param name="Algorithm">Which search ran.</param>
/// <param name="Best">
/// The optimum, one envelope per variable. The interval on each is the spread of
/// the final simplex or population in that direction: how far apart the candidates
/// still under consideration were, which says how sharply the optimum is defined.
/// </param>
/// <param name="Objective">The objective at the optimum, in the caller's sense.</param>
/// <param name="Evaluations">Objective evaluations spent.</param>
/// <param name="Iterations">Iterations of the search.</param>
/// <param name="Failures">Evaluations that produced no figure of merit.</param>
/// <param name="Converged">Whether the search met its tolerance rather than its budget.</param>
/// <param name="History">Every improvement on the best-so-far, in order.</param>
public sealed record OptimisationResult(
    OptimisationAlgorithm Algorithm,
    IReadOnlyDictionary<string, Measured> Best,
    Measured Objective,
    int Evaluations,
    int Iterations,
    int Failures,
    bool Converged,
    IReadOnlyList<OptimisationStep> History)
{
    /// <summary>Every warning on the result, deduplicated by code.</summary>
    /// <remarks>
    /// GRD-2: warnings propagate. They are attached to each envelope so they
    /// survive being pulled apart; this gathers them for a caller reporting the
    /// search as a whole.
    /// </remarks>
    public IReadOnlyList<ValidityWarning> Warnings =>
        [.. Objective.Warnings
            .Concat(Best.Values.SelectMany(m => m.Warnings))
            .GroupBy(w => w.Code, StringComparer.Ordinal)
            .Select(g => g.First())];
}

/// <summary>
/// Derivative-free optimisation over a model's declared parameters.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 13: "Optimization composes objectives from section 12, which may
/// be Python extensions. Nelder-Mead for small problems, CMA-ES for larger and
/// rougher ones." Both are here, behind one entry point, because which one suits
/// is a property of the problem rather than of the caller.
/// </para>
/// <para>
/// The optimiser knows nothing about what it is optimising. It takes a model, a
/// set of design variables, and a function from a validated model to a number, so
/// the same driver tunes a mirror's second-order coefficient, a quadrupole's rod
/// ratio, or a lens's spot size without alteration - the same seam
/// <see cref="ToleranceStudy"/> uses, and for the same reason.
/// </para>
/// <para>
/// Everything happens in a normalised box. Each variable is mapped affinely onto
/// the unit interval, so the search takes steps of comparable size in a length, a
/// voltage, and a dimensionless ratio without anyone tuning per-variable scales.
/// A candidate outside the box is repaired to its face and charged a penalty
/// proportional to the square of how far it was moved, which is the standard
/// handling and keeps the objective defined everywhere without a hard wall for the
/// simplex to slide along.
/// </para>
/// </remarks>
public static class Optimiser
{
    /// <summary>Runs a search.</summary>
    /// <param name="document">The model to vary.</param>
    /// <param name="variables">The parameters to vary, and over what interval.</param>
    /// <param name="objective">
    /// Produces the figure of merit from a validated model, or null when the
    /// design does not work. Exceptions are caught and counted as failures rather
    /// than ending the search: an impossible geometry partway through a search is
    /// a result about that geometry, not a crash.
    /// </param>
    /// <param name="sense">Whether the objective is to be minimised or maximised.</param>
    /// <param name="algorithm">Which search to run.</param>
    /// <param name="settings">How to run it.</param>
    /// <returns>The optimum, with its envelope and its warnings.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">
    /// The model does not validate, a variable does not name a free bounded
    /// parameter, or the starting point produces no figure of merit.
    /// </exception>
    /// <param name="sourceDirectory">
    /// The directory the model document was read from, which any file it references is
    /// resolved against. Null when the caller has none, and a model declaring an
    /// imported gas field is then refused rather than run in a gas it does not
    /// describe.
    /// </param>
    public static OptimisationResult Run(
        ModelDocument document,
        IReadOnlyList<DesignVariable> variables,
        Func<CompiledModel, double?> objective,
        ObjectiveSense sense = ObjectiveSense.Minimise,
        OptimisationAlgorithm algorithm = OptimisationAlgorithm.NelderMead,
        OptimisationSettings? settings = null,
        string? sourceDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(objective);

        if (variables.Count == 0)
        {
            throw new ArgumentException("a search needs at least one design variable", nameof(variables));
        }

        var resolved = settings ?? new OptimisationSettings();

        // At least enough to evaluate the starting point. Below that the search
        // would return an optimum it had never looked at, which is worse than
        // refusing.
        ArgumentOutOfRangeException.ThrowIfLessThan(resolved.MaximumEvaluations, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(resolved.ParameterTolerance);
        ArgumentOutOfRangeException.ThrowIfNegative(resolved.ObjectiveTolerance);
        ArgumentOutOfRangeException.ThrowIfNegative(resolved.Restarts);

        var problem = new SearchProblem(document, variables, objective, sense, resolved, sourceDirectory);

        var (bestPoint, spread, iterations, converged) = algorithm switch
        {
            OptimisationAlgorithm.NelderMead => NelderMead.Search(problem),
            OptimisationAlgorithm.CmaEs => CmaEs.Search(problem),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "unknown algorithm"),
        };

        return problem.Report(algorithm, bestPoint, spread, iterations, converged);
    }
}

/// <summary>
/// The box, the bookkeeping, and the objective, shared by both searches.
/// </summary>
internal sealed class SearchProblem
{
    private readonly ModelDocument _document;
    private readonly string? _sourceDirectory;
    private readonly Func<CompiledModel, double?> _objective;
    private readonly ObjectiveSense _sense;
    private readonly ResolvedParameter[] _parameters;
    private readonly double[] _low;
    private readonly double[] _high;
    private readonly List<OptimisationStep> _history = [];

    private double _bestValue = double.PositiveInfinity;
    private double[] _bestPoint = [];

    internal SearchProblem(
        ModelDocument document,
        IReadOnlyList<DesignVariable> variables,
        Func<CompiledModel, double?> objective,
        ObjectiveSense sense,
        OptimisationSettings settings,
        string? sourceDirectory = null)
    {
        _document = document;
        _sourceDirectory = sourceDirectory;
        _objective = objective;
        _sense = sense;
        Settings = settings;

        var baseline = ModelValidator.Validate(document, null, sourceDirectory);

        if (!baseline.IsValid)
        {
            throw new EinzelException(baseline.Errors[0]);
        }

        var surface = baseline.Model!.Parameters;
        var count = variables.Count;

        _parameters = new ResolvedParameter[count];
        _low = new double[count];
        _high = new double[count];
        Start = new double[count];

        for (var k = 0; k < count; k++)
        {
            var (parameter, low, high) = variables[k].Bind(surface, $"/variables/{k}");
            _parameters[k] = parameter;
            _low[k] = low;
            _high[k] = high;

            // The nominal value is the starting point, clamped into the box. A
            // nominal outside its own declared bounds is the model's problem and
            // the validator's to report; here it only has to not start outside.
            Start[k] = Math.Clamp((parameter.Value.SiValue - low) / (high - low), 0.0, 1.0);
        }

        Evaluate(Start);

        // Asking whether the value came back finite would not do it: a failed
        // evaluation is deliberately a large finite number rather than an
        // infinity, so the only honest test is whether it was counted as a
        // failure.
        if (Failures > 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ConvergenceFailed,
                Path = "/",
                Constraint = "the starting design produced no figure of merit",
                Suggestion = "a search cannot improve on a point it cannot evaluate; check the objective at "
                    + "nominal parameters before optimising",
            });
        }
    }

    internal OptimisationSettings Settings { get; }

    internal int Dimension => _parameters.Length;

    internal double[] Start { get; }

    internal int Evaluations { get; private set; }

    internal int Failures { get; private set; }

    internal bool BudgetSpent => Evaluations >= Settings.MaximumEvaluations;

    internal double[] BestPoint => _bestPoint;

    /// <summary>
    /// Evaluates a normalised point, always as something to minimise.
    /// </summary>
    /// <remarks>
    /// Three things fold together here so neither search has to know about any of
    /// them. The sense is applied, so both algorithms only ever descend. The box
    /// is enforced by repair and penalty rather than by refusal, so the objective
    /// is defined everywhere and a simplex reflecting past a face is pushed back
    /// rather than stopped dead. And a failed evaluation becomes a large finite
    /// number rather than an infinity, because an infinity gives a search no
    /// gradient to escape along and it will sit in the failed region.
    /// </remarks>
    internal double Evaluate(double[] point)
    {
        if (BudgetSpent)
        {
            return double.PositiveInfinity;
        }

        var overrides = new Dictionary<string, Quantity>(StringComparer.Ordinal);
        var penalty = 0.0;

        for (var k = 0; k < point.Length; k++)
        {
            var clamped = Math.Clamp(point[k], 0.0, 1.0);
            var excess = point[k] - clamped;
            penalty += excess * excess;

            overrides[_parameters[k].Name] = Quantity.Si(
                _low[k] + (clamped * (_high[k] - _low[k])), _parameters[k].Value.Dimension);
        }

        Evaluations++;
        double? raw;

        try
        {
            var validation = ModelValidator.Validate(_document, overrides, _sourceDirectory);
            raw = validation.IsValid ? _objective(validation.Model!) : null;
        }
        catch (EinzelException)
        {
            raw = null;
        }
        catch (ArithmeticException)
        {
            raw = null;
        }
        catch (InvalidOperationException)
        {
            raw = null;
        }

        if (raw is not { } value || !double.IsFinite(value))
        {
            Failures++;
            return FailedObjective;
        }

        var minimised = (_sense == ObjectiveSense.Maximise ? -value : value) + (BoundaryPenalty * penalty);

        if (minimised < _bestValue)
        {
            _bestValue = minimised;
            _bestPoint = [.. point.Select(u => Math.Clamp(u, 0.0, 1.0))];
            _history.Add(new OptimisationStep(Evaluations, Sensed(minimised), Physical(_bestPoint)));
        }

        return minimised;
    }

    /// <summary>What a failed evaluation is worth.</summary>
    /// <remarks>
    /// Large but finite, and deliberately not infinity. A simplex whose reflection
    /// lands on infinity learns nothing about which way to go and can spend its
    /// whole budget contracting against a wall of equal values; a large finite
    /// number leaves the penalty term visible underneath, so the search is still
    /// pushed back toward where the model works.
    /// </remarks>
    private const double FailedObjective = 1e30;

    private const double BoundaryPenalty = 1e6;

    private double Sensed(double minimised) => _sense == ObjectiveSense.Maximise ? -minimised : minimised;

    private Dictionary<string, double> Physical(double[] point)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);

        for (var k = 0; k < point.Length; k++)
        {
            values[_parameters[k].Name] = _low[k] + (point[k] * (_high[k] - _low[k]));
        }

        return values;
    }

    /// <summary>Assembles the GRD-1 envelopes and the warnings that go with them.</summary>
    /// <param name="algorithm">Which search ran.</param>
    /// <param name="best">The optimum, normalised.</param>
    /// <param name="spread">Per-variable spread of the final simplex or population, normalised.</param>
    /// <param name="iterations">Iterations taken.</param>
    /// <param name="converged">Whether the tolerance was met.</param>
    /// <returns>The result.</returns>
    internal OptimisationResult Report(
        OptimisationAlgorithm algorithm, double[] best, double[] spread, int iterations, bool converged)
    {
        var warnings = new List<ValidityWarning>();

        if (!converged)
        {
            // Which tolerance, not "its tolerance". Convergence is two tests and
            // both must hold, so a reader who tightens the one that was already met
            // changes nothing and concludes the setting does not work - which is
            // what happened to the first agent to hit this.
            var widest = spread.Length > 0 ? spread.Max() : 0.0;
            var parameterMet = widest <= Settings.ParameterTolerance;

            warnings.Add(new ValidityWarning(
                "optimiser.budget-exhausted",
                $"the search stopped after {Evaluations} evaluations without meeting both convergence "
                + "tests, so this is the best design found rather than an optimum. The final spread was "
                + $"{widest:G3} of the box against parameterTolerance {Settings.ParameterTolerance:G3}, "
                + $"which it {(parameterMet ? "met" : "did not meet")}; the other test asks the objective "
                + $"to settle to within objectiveTolerance {Settings.ObjectiveTolerance:G3} of its own "
                + $"spread, and {(parameterMet ? "that is the one still open" : "both are still open")}. "
                + "Loosen whichever is not met, or raise maximumEvaluations",
                WarningSeverity.Qualified));
        }

        if (Failures > 0)
        {
            var fraction = (double)Failures / Evaluations;

            warnings.Add(new ValidityWarning(
                "optimiser.failed-evaluations",
                $"{Failures} of {Evaluations} evaluations produced no figure of merit ({fraction:P1}). The "
                + "search treated them as very poor designs and continued",
                fraction > 0.25 ? WarningSeverity.Qualified : WarningSeverity.Advisory));
        }

        var atBound = new List<string>();

        for (var k = 0; k < best.Length; k++)
        {
            if (best[k] < 1e-6 || best[k] > 1.0 - 1e-6)
            {
                atBound.Add(_parameters[k].Name);
            }
        }

        if (atBound.Count > 0)
        {
            // The most useful thing an optimiser can say, and the easiest to miss.
            warnings.Add(new ValidityWarning(
                "optimiser.optimum-at-bound",
                $"the optimum sits on a bound in {string.Join(", ", atBound)}. What is reported is where the "
                + "search was stopped by the box, not a stationary point of the objective; widen the bound to "
                + "find out where the objective actually turns",
                WarningSeverity.Qualified));
        }

        var envelopes = new Dictionary<string, Measured>(StringComparer.Ordinal);

        for (var k = 0; k < best.Length; k++)
        {
            var scale = _high[k] - _low[k];
            var centre = _low[k] + (best[k] * scale);
            var half = 0.5 * spread[k] * scale;
            var dimension = _parameters[k].Value.Dimension;

            envelopes[_parameters[k].Name] = new Measured(
                Quantity.Si(centre, dimension),
                UncertaintyInterval.Symmetric(
                    Quantity.Si(centre, dimension), Quantity.Si(half, dimension), 1.0),
                new Evidence.Search(Evaluations, converged, spread[k] * scale),
                warnings);
        }

        var objectiveValue = Sensed(_bestValue);

        var objective = new Measured(
            Quantity.Si(objectiveValue, Settings.ObjectiveDimension),
            UncertaintyInterval.Symmetric(
                Quantity.Si(objectiveValue, Settings.ObjectiveDimension),
                Quantity.Si(0.0, Settings.ObjectiveDimension),
                1.0),
            new Evidence.Search(Evaluations, converged, spread.Length == 0 ? 0.0 : spread.Max()),
            warnings);

        return new OptimisationResult(
            algorithm, envelopes, objective, Evaluations, iterations, Failures, converged, _history);
    }
}
