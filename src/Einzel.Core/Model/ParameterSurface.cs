using Einzel.Core.Errors;
using Einzel.Core.Units;

namespace Einzel.Core.Model;

/// <summary>
/// One named parameter: either a free value with bounds, or an expression
/// derived from other parameters.
/// </summary>
/// <remarks>
/// <para>
/// The declared parameter surface LIB-1 asks for. A template is a model document
/// plus this: the set of things a caller may vary and the range each may be
/// varied over. Everything downstream reads from it — a sweep perturbs these, an
/// optimiser searches these, and a tolerance study draws from their bounds.
/// </para>
/// <para>
/// Bounds are part of the declaration rather than a separate validation pass
/// because they carry design intent. A mirror depth that may run from 40 to
/// 200 mm says something a bare nominal value does not, and it is what lets a
/// sweep be written as "vary everything over its declared range" instead of
/// requiring every study to restate limits the template already knows.
/// </para>
/// </remarks>
public sealed record ParameterDocument
{
    /// <summary>The nominal magnitude, in <see cref="Unit"/>. Omitted for a derived parameter.</summary>
    public double? Value { get; init; }

    /// <summary>
    /// An arithmetic expression over other parameters. Omitted for a free
    /// parameter; supplying both is an error.
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// The unit the value or the expression's result is expressed in. Required
    /// either way; for an expression its dimension is checked against the one the
    /// expression actually produces.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>Lower bound, in the same unit. Optional.</summary>
    public double? Minimum { get; init; }

    /// <summary>Upper bound, in the same unit. Optional.</summary>
    public double? Maximum { get; init; }

    /// <summary>What this parameter means. Carried into schema self-description (AGT-7).</summary>
    public string? Description { get; init; }
}

/// <summary>A parameter resolved to a quantity, with its bounds.</summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Value">The resolved value, in SI.</param>
/// <param name="Minimum">Lower bound, in SI, or null.</param>
/// <param name="Maximum">Upper bound, in SI, or null.</param>
/// <param name="IsDerived">Whether the value came from an expression.</param>
/// <param name="Description">What it means.</param>
public sealed record ResolvedParameter(
    string Name,
    Quantity Value,
    Quantity? Minimum,
    Quantity? Maximum,
    bool IsDerived,
    string? Description)
{
    /// <summary>Whether a candidate value lies within the declared bounds.</summary>
    /// <param name="candidate">The value to test.</param>
    /// <returns><see langword="true"/> when within bounds, or when none are declared.</returns>
    public bool IsWithinBounds(Quantity candidate) =>
        (Minimum is null || candidate >= Minimum.Value)
        && (Maximum is null || candidate <= Maximum.Value);
}

/// <summary>
/// The resolved parameter surface of a model: every parameter evaluated, in
/// dependency order, with cycles refused.
/// </summary>
public sealed class ParameterSurface
{
    private readonly Dictionary<string, ResolvedParameter> _resolved;

    private ParameterSurface(Dictionary<string, ResolvedParameter> resolved) => _resolved = resolved;

    /// <summary>An empty surface, for documents that declare no parameters.</summary>
    public static ParameterSurface Empty { get; } = new([]);

    /// <summary>Every parameter, by name.</summary>
    public IReadOnlyDictionary<string, ResolvedParameter> Parameters => _resolved;

    /// <summary>The free parameters, which are the ones a sweep or optimiser may vary.</summary>
    public IReadOnlyList<ResolvedParameter> FreeParameters =>
        [.. _resolved.Values.Where(p => !p.IsDerived).OrderBy(p => p.Name, StringComparer.Ordinal)];

    /// <summary>Looks up a resolved quantity.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="KeyNotFoundException">No such parameter.</exception>
    public Quantity this[string name] => _resolved[name].Value;

    /// <summary>The resolved values, in the form the expression evaluator consumes.</summary>
    /// <returns>A name-to-quantity map.</returns>
    public IReadOnlyDictionary<string, Quantity> Values() =>
        _resolved.ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.Ordinal);

    /// <summary>
    /// Resolves a declaration, evaluating derived parameters in dependency order.
    /// </summary>
    /// <param name="declared">The declared parameters.</param>
    /// <param name="overrides">
    /// Replacement values for free parameters, as a sweep or optimiser supplies.
    /// Derived parameters re-evaluate against them, which is the point: perturb a
    /// depth and everything expressed in terms of it follows.
    /// </param>
    /// <param name="errors">Collects failures rather than throwing on the first.</param>
    /// <returns>The resolved surface, or null when resolution failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is null.</exception>
    public static ParameterSurface? Resolve(
        IReadOnlyDictionary<string, ParameterDocument>? declared,
        IReadOnlyDictionary<string, Quantity>? overrides,
        List<EinzelError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (declared is null || declared.Count == 0)
        {
            return Empty;
        }

        var order = TopologicalOrder(declared, errors);

        if (order is null)
        {
            return null;
        }

        var resolved = new Dictionary<string, ResolvedParameter>(StringComparer.Ordinal);
        var values = new Dictionary<string, Quantity>(StringComparer.Ordinal);

        foreach (var name in order)
        {
            var document = declared[name];
            var path = $"/parameters/{name}";

            if (document.Value is not null && document.Expression is not null)
            {
                errors.Add(Error(path, "a parameter is either a value or an expression, not both",
                    "remove whichever of 'value' and 'expression' is not wanted"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(document.Unit))
            {
                errors.Add(Error(path, "a parameter must declare its unit",
                    "add \"unit\": \"mm\", or whichever unit applies; use \"1\" for a pure ratio"));
                continue;
            }

            UnitDefinition unit;

            try
            {
                unit = UnitRegistry.Resolve(document.Unit);
            }
            catch (EinzelException failure)
            {
                errors.Add(failure.Error with { Path = path });
                continue;
            }

            Quantity value;

            if (document.Expression is not null)
            {
                try
                {
                    value = ExpressionEvaluator.Evaluate(document.Expression, values, path);
                }
                catch (EinzelException failure)
                {
                    errors.Add(failure.Error);
                    continue;
                }

                if (unit.Dimension != value.Dimension)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.UnitsIncompatible,
                        Path = path,
                        Constraint =
                            $"the expression produces dimension {value.Dimension} but the declared unit "
                            + $"'{document.Unit}' has dimension {unit.Dimension}",
                        Suggestion = $"declare a unit of dimension {value.Dimension}",
                    });
                    continue;
                }
            }
            else if (document.Value is { } magnitude)
            {
                if (!double.IsFinite(magnitude))
                {
                    errors.Add(Error(path, "a parameter value must be finite", "supply a finite magnitude"));
                    continue;
                }

                value = Quantity.From(magnitude, document.Unit);

                // An override replaces a free parameter's nominal value. Dimension
                // agreement is required: a sweep that hands a voltage to a length
                // has made a mistake worth stopping on.
                if (overrides is not null && overrides.TryGetValue(name, out var replacement))
                {
                    if (replacement.Dimension != value.Dimension)
                    {
                        errors.Add(new EinzelError
                        {
                            Code = ErrorCodes.UnitsIncompatible,
                            Path = path,
                            Constraint =
                                $"an override of dimension {replacement.Dimension} was supplied for a parameter "
                                + $"of dimension {value.Dimension}",
                            Suggestion = "supply the override in a compatible unit",
                        });
                        continue;
                    }

                    value = replacement;
                }
            }
            else
            {
                errors.Add(Error(path, "a parameter must declare either a value or an expression",
                    "add \"value\": 90 alongside its unit"));
                continue;
            }

            var minimum = document.Minimum is { } low ? Quantity.From(low, document.Unit) : (Quantity?)null;
            var maximum = document.Maximum is { } high ? Quantity.From(high, document.Unit) : (Quantity?)null;

            var parameter = new ResolvedParameter(
                name, value, minimum, maximum, document.Expression is not null, document.Description);

            // Bounds are checked, not silently clamped. A sweep that walks a
            // parameter past its declared range has found something the template
            // author did not intend, and clamping would hide it.
            if (!parameter.IsWithinBounds(value))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = path,
                    Constraint =
                        $"the value lies outside the declared bounds of "
                        + $"[{document.Minimum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-inf"}, "
                        + $"{document.Maximum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "+inf"}] "
                        + $"{document.Unit}",
                    Observed = new ObservedValue(value.In(document.Unit), document.Unit),
                    Suggestion = "widen the bounds, or bring the value inside them",
                });
            }

            resolved[name] = parameter;
            values[name] = value;
        }

        return errors.Count > 0 ? null : new ParameterSurface(resolved);
    }

    /// <summary>
    /// Orders parameters so every expression is evaluated after what it depends
    /// on, refusing a cycle rather than recursing into one.
    /// </summary>
    private static List<string>? TopologicalOrder(
        IReadOnlyDictionary<string, ParameterDocument> declared, List<EinzelError> errors)
    {
        var order = new List<string>(declared.Count);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var failed = false;

        foreach (var name in declared.Keys.Order(StringComparer.Ordinal))
        {
            Visit(name, []);
        }

        return failed ? null : order;

        void Visit(string name, List<string> chain)
        {
            if (state.TryGetValue(name, out var mark))
            {
                if (mark == 1)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"/parameters/{name}",
                        Constraint = "parameter definitions form a cycle: "
                            + string.Join(" -> ", [.. chain, name]),
                        Suggestion = "break the cycle by giving one of these a literal value",
                    });

                    failed = true;
                }

                return;
            }

            if (!declared.TryGetValue(name, out var document))
            {
                // A reference to something undeclared: reported when the
                // expression is evaluated, with the full list of what does exist.
                return;
            }

            state[name] = 1;

            if (document.Expression is not null)
            {
                foreach (var reference in ExpressionEvaluator.References(document.Expression))
                {
                    Visit(reference, [.. chain, name]);
                }
            }

            state[name] = 2;
            order.Add(name);
        }
    }

    private static EinzelError Error(string path, string constraint, string suggestion) => new()
    {
        Code = ErrorCodes.SchemaInvalid,
        Path = path,
        Constraint = constraint,
        Suggestion = suggestion,
    };
}
