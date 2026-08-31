using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Core.Model;

/// <summary>
/// A magnitude and its unit, as they appear in a model document.
/// </summary>
/// <param name="Value">The magnitude, expressed in <paramref name="Unit"/>.</param>
/// <param name="Unit">The unit symbol.</param>
/// <remarks>
/// <para>
/// Spec section 9: "At every boundary units are explicit and required.
/// <c>{"energy": 4000}</c> is a validation error. Deliberately more annoying than
/// the alternative, because unit ambiguity is the commonest source of silent
/// wrongness and an agent building from prose is the actor most likely to
/// introduce it."
/// </para>
/// <para>
/// Making this a two-field object rather than a bare number is what enforces
/// that at the format level: there is no way to write a magnitude in a model
/// document without naming its unit, so the failure happens at parse time with a
/// path, rather than at run time as a factor of 1.602e-19.
/// </para>
/// </remarks>
public sealed record QuantityValue(double Value, string Unit)
{
    /// <summary>
    /// An arithmetic expression over the model's parameters, evaluated in place of
    /// <see cref="Value"/>. Spec section 9: "Every placement is a parametric
    /// expression, never a baked number."
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// Converts to SI, evaluating an expression against the parameter surface when
    /// one is present.
    /// </summary>
    /// <param name="path">JSON Pointer to this value, for the error object.</param>
    /// <param name="expected">The dimension this field requires.</param>
    /// <param name="parameters">Resolved parameters the expression may name.</param>
    /// <returns>The quantity, in SI.</returns>
    /// <exception cref="EinzelException">
    /// The expression is malformed, or the result has the wrong dimension.
    /// </exception>
    public Quantity ToQuantity(
        string path, Dimension expected, IReadOnlyDictionary<string, Quantity> parameters)
    {
        if (Expression is null)
        {
            return ToQuantity(path, expected);
        }

        var value = ExpressionEvaluator.Evaluate(Expression, parameters, path);

        if (!Components.Matches(Expression, value, expected))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsIncompatible,
                Path = path,
                Constraint = $"this field requires a quantity of dimension {expected}",
                Observed = new ObservedValue(value.SiValue, value.Dimension.ToString()),
                Suggestion = $"the expression '{Expression}' produces dimension {value.Dimension}",
            });
        }

        return value;
    }

    /// <summary>Converts to SI, reporting failures against a document path.</summary>
    /// <param name="path">JSON Pointer to this value, for the error object.</param>
    /// <param name="expected">The dimension this field requires.</param>
    /// <returns>The quantity, in SI.</returns>
    /// <exception cref="EinzelException">
    /// The unit is missing, unknown, or of the wrong dimension.
    /// </exception>
    public Quantity ToQuantity(string path, Dimension expected)
    {
        UnitDefinition definition;

        try
        {
            definition = UnitRegistry.Resolve(Unit);
        }
        catch (EinzelException failure)
        {
            // Re-throw with the document path attached: the registry does not
            // know where in the model it was called from, and AGT-3 requires the
            // offending path.
            throw new EinzelException(failure.Error with { Path = path }, failure);
        }

        if (definition.Dimension != expected)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsIncompatible,
                Path = path,
                Constraint = $"this field requires a quantity of dimension {expected}",
                Observed = new ObservedValue(Value, Unit),
                Suggestion = $"'{Unit}' has dimension {definition.Dimension}; supply a unit of dimension {expected}",
            });
        }

        if (!double.IsFinite(Value))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a quantity must be finite",
                Observed = new ObservedValue(Value, Unit),
                Suggestion = "supply a finite magnitude",
            });
        }

        return Quantity.From(Value, Unit);
    }
}

/// <summary>
/// A three-component vector and its unit, as it appears in a model document.
/// </summary>
/// <param name="Value">The three components, expressed in <paramref name="Unit"/>.</param>
/// <param name="Unit">The unit symbol.</param>
public sealed record VectorValue(IReadOnlyList<double> Value, string Unit)
{
    /// <summary>
    /// Three arithmetic expressions over the model's parameters, evaluated in place
    /// of <see cref="Value"/>. Spec section 9: "Every placement is a parametric
    /// expression, never a baked number."
    /// </summary>
    /// <remarks>
    /// One expression per component, because the components are independent: a
    /// detector sits at a derived distance along one axis and on the axis in the
    /// other two, and a single expression could not say that. When present the unit
    /// is not consulted - each expression carries its own dimension, exactly as a
    /// scalar expression does.
    /// </remarks>
    public IReadOnlyList<string>? Expression { get; init; }

    /// <summary>
    /// Converts to an SI vector, evaluating expressions against the parameter
    /// surface when they are present.
    /// </summary>
    /// <param name="path">JSON Pointer to this value, for the error object.</param>
    /// <param name="expected">The dimension this field requires.</param>
    /// <param name="parameters">Resolved parameters the expressions may name.</param>
    /// <returns>The vector, in SI.</returns>
    /// <exception cref="EinzelException">
    /// There are not exactly three expressions, one is malformed, or one produces
    /// the wrong dimension.
    /// </exception>
    public Vec3 ToVec3(
        string path, Dimension expected, IReadOnlyDictionary<string, Quantity> parameters)
    {
        if (Expression is null)
        {
            return ToVec3(path, expected);
        }

        if (Expression is not { Count: 3 })
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = "a vector expression must have exactly three components",
                Observed = new ObservedValue(Expression.Count, "expressions"),
                Suggestion = "supply [\"x\", \"y\", \"z\"], using \"0\" for a component that is on axis",
            });
        }

        var components = new double[3];

        for (var i = 0; i < 3; i++)
        {
            var component = ExpressionEvaluator.Evaluate(
                Expression[i], parameters, path + "/expression/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (!Components.Matches(Expression[i], component, expected))
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.UnitsIncompatible,
                    Path = path + "/expression/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Constraint = $"this field requires a vector of dimension {expected}",
                    Observed = new ObservedValue(component.SiValue, component.Dimension.ToString()),
                    Suggestion = $"the expression '{Expression[i]}' produces dimension {component.Dimension}",
                });
            }

            if (!double.IsFinite(component.SiValue))
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = path + "/expression/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Constraint = "a vector component must be finite",
                    Observed = new ObservedValue(component.SiValue, "1"),
                    Suggestion = $"the expression '{Expression[i]}' is not finite; check for a division by zero",
                });
            }

            components[i] = component.SiValue;
        }

        return new Vec3(components[0], components[1], components[2]);
    }

    /// <summary>Element-wise equality.</summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the components and unit match.</returns>
    /// <remarks>
    /// The compiler-generated equality on a record holding a collection compares
    /// the reference, not the contents, so two documents parsed from identical
    /// text would compare unequal. That is a trap rather than a nuance: it is
    /// invisible at the call site and wrong in the direction that matters.
    /// </remarks>
    public bool Equals(VectorValue? other) =>
        other is not null
        && string.Equals(Unit, other.Unit, StringComparison.Ordinal)
        && Components.SequenceEqual(Value, other.Value)
        && Components.SequenceEqual(Expression, other.Expression);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Components.HashOf(Value, Unit), Components.HashOf(Expression));

    /// <summary>Converts to an SI vector, reporting failures against a document path.</summary>
    /// <param name="path">JSON Pointer to this value, for the error object.</param>
    /// <param name="expected">The dimension this field requires.</param>
    /// <returns>The vector, in SI.</returns>
    /// <exception cref="EinzelException">
    /// The vector does not have three components, or the unit is wrong.
    /// </exception>
    public Vec3 ToVec3(string path, Dimension expected)
    {
        if (Value is not { Count: 3 })
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = "a vector must have exactly three components",
                Observed = new ObservedValue(Value?.Count ?? 0, "components"),
                Suggestion = "supply [x, y, z]",
            });
        }

        var scale = new QuantityValue(1.0, Unit).ToQuantity(path, expected).SiValue;

        for (var i = 0; i < 3; i++)
        {
            if (!double.IsFinite(Value[i]))
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/value/{i}",
                    Constraint = "every vector component must be finite",
                    Observed = new ObservedValue(Value[i], Unit),
                    Suggestion = "supply a finite magnitude",
                });
            }
        }

        return new Vec3(Value[0] * scale, Value[1] * scale, Value[2] * scale);
    }
}

/// <summary>
/// A direction in a model document: three dimensionless components, normalised
/// on conversion.
/// </summary>
/// <param name="Value">The three components.</param>
/// <remarks>
/// A direction carries no unit because it has no dimension. Normalising on
/// conversion rather than requiring a unit vector means a document can say
/// <c>[1, 0, 0]</c> or <c>[2, 0, 0]</c> and mean the same thing, which is one
/// fewer way for a hand-written or generated model to be subtly wrong.
/// </remarks>
public sealed record DirectionValue(IReadOnlyList<double> Value)
{
    /// <summary>
    /// Three arithmetic expressions over the model's parameters, evaluated in place
    /// of <see cref="Value"/>. Spec section 9: "Every placement is a parametric
    /// expression, never a baked number."
    /// </summary>
    /// <remarks>
    /// <para>
    /// A direction is a placement, and this was the one kind that could not be
    /// written as an expression. <see cref="VectorValue"/> has had it since the
    /// parameter surface existed and cites the same sentence.
    /// </para>
    /// <para>
    /// <b>The device that needed it is a trap with a curved axis.</b> Launching an
    /// ion along such an axis means a direction that depends on where round the arc
    /// it starts - so with a literal only, changing the arc angle moves the geometry
    /// and leaves the launch pointing where it used to, silently. The same shape as
    /// <c>drivePhase</c> being a plain double until a travelling wave needed a phase
    /// that could ramp with the repeat index.
    /// </para>
    /// <para>
    /// Dimensionless, because a direction is: what is declared is the ratio between
    /// the components, and the vector is normalised. That is why there is no unit
    /// here and why an expression may mix parameters of any dimension as long as the
    /// result is a pure number.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? Expression { get; init; }

    /// <summary>Element-wise equality. See <see cref="VectorValue.Equals(VectorValue)"/>.</summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the components match.</returns>
    public bool Equals(DirectionValue? other) =>
        other is not null && Components.SequenceEqual(Value, other.Value);

    /// <inheritdoc/>
    public override int GetHashCode() => Components.HashOf(Value, unit: null);

    /// <summary>Converts to a unit vector, evaluating expressions if present.</summary>
    /// <param name="path">JSON Pointer to this value, for the error object.</param>
    /// <param name="parameters">The model's resolved parameter surface.</param>
    /// <returns>The normalised direction.</returns>
    /// <exception cref="EinzelException">
    /// The vector does not have three components, has zero length, or an expression
    /// does not evaluate to a dimensionless number.
    /// </exception>
    public Vec3 ToUnitVector(string path, IReadOnlyDictionary<string, Quantity> parameters)
    {
        if (Expression is null)
        {
            return ToUnitVector(path);
        }

        if (Expression is not { Count: 3 })
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/expression",
                Constraint = "a direction must have exactly three components",
                Observed = new ObservedValue(Expression?.Count ?? 0, "components"),
                Suggestion = "supply three expressions, one per axis",
            });
        }

        var components = new double[3];

        for (var k = 0; k < 3; k++)
        {
            var evaluated = ExpressionEvaluator.Evaluate(
                Expression[k], parameters, $"{path}/expression/{k}");

            // Dimensionless, because a direction is a ratio between its components. A
            // length here would be a vector wearing a direction's clothes, and the
            // normalisation would hide the mistake rather than catch it.
            if (!evaluated.Dimension.IsDimensionless)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.UnitsIncompatible,
                    Path = $"{path}/expression/{k}",
                    Constraint = "a direction component must be dimensionless, but this one "
                        + $"has dimension {evaluated.Dimension}",
                    Suggestion = "form a ratio, for example cosPi(arcHalfTurns / 4), or use a "
                        + "vector with a unit if you meant a position",
                });
            }

            components[k] = evaluated.SiValue;
        }

        return new DirectionValue(components).ToUnitVector(path);
    }

    /// <summary>Converts a literal direction to a unit vector.</summary>
    /// <param name="path">JSON Pointer to this value, for the error object.</param>
    /// <returns>The normalised direction.</returns>
    /// <exception cref="EinzelException">
    /// The vector does not have three components, or has zero length.
    /// </exception>
    public Vec3 ToUnitVector(string path)
    {
        if (Value is not { Count: 3 })
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = "a direction must have exactly three components",
                Observed = new ObservedValue(Value?.Count ?? 0, "components"),
                Suggestion = "supply [x, y, z]",
            });
        }

        var vector = new Vec3(Value[0], Value[1], Value[2]);

        if (vector.LengthSquared <= 0.0 || !double.IsFinite(vector.LengthSquared))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a direction must have non-zero, finite length",
                Observed = new ObservedValue(vector.Length, "1"),
                Suggestion = "supply a non-zero direction, for example [1, 0, 0]",
            });
        }

        return vector.Normalized();
    }
}

/// <summary>
/// Element-wise comparison helpers for the document types that hold component
/// lists.
/// </summary>
/// <remarks>
/// Records give value equality for scalar members and reference equality for
/// collection members, which is a difference no call site can see. These two
/// helpers keep the corrected behaviour in one place.
/// </remarks>
internal static class Components
{
    internal static bool SequenceEqual(IReadOnlyList<double>? left, IReadOnlyList<double>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether an evaluated expression carries the dimension a field requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exact, with one exception: an expression that is <em>written</em> as a
    /// literal zero satisfies any dimension. The grammar has no unit literals, so a
    /// bare 0 is dimensionless and there is otherwise no way to write "on axis" -
    /// which every placement off the origin needs for its other two components.
    /// </para>
    /// <para>
    /// The test is on the text, not on the value, and that distinction is the whole
    /// design. A value test would make dimensional validity depend on a number: a
    /// document naming a dimensionless parameter that happens to be zero would
    /// validate at nominal and then fail with a units error partway through a sweep
    /// when the optimiser moved it off zero. Dimensions are a property of what was
    /// written and must not change under a parameter override.
    /// </para>
    /// <para>
    /// Safe because a literal zero is the one value whose unit conversion is the
    /// identity: the ambiguity that makes units mandatory here - is 4000 volts or
    /// kilovolts - does not exist at zero. Everything else is still refused,
    /// including a parameter whose value is zero.
    /// </para>
    /// </remarks>
    internal static bool Matches(string? expression, Quantity value, Dimension expected) =>
        value.Dimension == expected || IsLiteralZero(expression);

    /// <summary>Whether an expression is written as a zero constant.</summary>
    private static bool IsLiteralZero(string? expression) =>
        expression is not null
        && double.TryParse(
            expression.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var literal)
        && literal == 0.0;

    internal static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    internal static int HashOf(IReadOnlyList<string>? expressions)
    {
        if (expressions is null)
        {
            return 0;
        }

        var hash = default(HashCode);

        foreach (var expression in expressions)
        {
            hash.Add(expression, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    internal static int HashOf(IReadOnlyList<double>? components, string? unit)
    {
        var hash = new HashCode();

        if (unit is not null)
        {
            hash.Add(unit, StringComparer.Ordinal);
        }

        if (components is null)
        {
            return hash.ToHashCode();
        }

        foreach (var component in components)
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }
}
