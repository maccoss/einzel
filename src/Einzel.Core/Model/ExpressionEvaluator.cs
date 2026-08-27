using Einzel.Core.Errors;
using Einzel.Core.Units;

namespace Einzel.Core.Model;

/// <summary>
/// Evaluates the arithmetic expressions that make a model parametric.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 9: "Every placement is a parametric expression, never a baked
/// number: named parameters with units and bounds; derived parameters as
/// sandboxed expressions." The reason is section 13's tolerance work — bake a
/// design down to numbers and "move this stripe 50 microns and re-solve" stops
/// being sayable, which is the whole point of the sweep machinery.
/// </para>
/// <para>
/// Expressions evaluate over <see cref="Quantity"/>, not over doubles, so
/// dimensions propagate through the arithmetic and a term that adds a length to a
/// voltage fails where it is written rather than thousands of steps later. That
/// makes the evaluator a unit checker as much as a calculator.
/// </para>
/// <para>
/// Sandboxed in the sense that matters here: the grammar is arithmetic and a
/// handful of functions, there is no way to name anything but a parameter, and
/// evaluation cannot loop. It is not a scripting language and is not meant to
/// become one — spec section 5 is explicit that extension goes through Python
/// behind a process boundary, not through the model format.
/// </para>
/// </remarks>
public static class ExpressionEvaluator
{
    /// <summary>Evaluates an expression against a set of resolved parameters.</summary>
    /// <param name="expression">The expression text.</param>
    /// <param name="parameters">Parameters already resolved to quantities.</param>
    /// <param name="path">JSON Pointer to the expression, for the error object.</param>
    /// <returns>The resulting quantity, with its dimension derived from the operands.</returns>
    /// <exception cref="EinzelException">The expression is malformed, names an unknown parameter, or is dimensionally inconsistent.</exception>
    public static Quantity Evaluate(
        string expression, IReadOnlyDictionary<string, Quantity> parameters, string path)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(expression))
        {
            throw Failure(path, "an expression may not be empty", "supply an expression, for example 'depth * 0.5'");
        }

        var parser = new Parser(expression, parameters, path);
        var value = parser.ParseExpression();
        parser.ExpectEnd();

        return value;
    }

    /// <summary>The parameter names an expression refers to.</summary>
    /// <param name="expression">The expression text.</param>
    /// <returns>Every identifier that is not a known function name.</returns>
    /// <remarks>
    /// Used to order derived parameters before evaluating them, and to detect a
    /// cycle rather than recursing into one.
    /// </remarks>
    public static IReadOnlyList<string> References(string expression)
    {
        var names = new List<string>();

        if (string.IsNullOrEmpty(expression))
        {
            return names;
        }

        for (var i = 0; i < expression.Length; i++)
        {
            if (!char.IsLetter(expression[i]) && expression[i] != '_')
            {
                continue;
            }

            var start = i;

            while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
            {
                i++;
            }

            var name = expression[start..i];

            // A name immediately followed by '(' is a call, not a parameter.
            var next = i;

            while (next < expression.Length && char.IsWhiteSpace(expression[next]))
            {
                next++;
            }

            if (next < expression.Length && expression[next] == '(')
            {
                continue;
            }

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static EinzelException Failure(string path, string constraint, string suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion,
        });

    /// <summary>Recursive-descent parser over the expression grammar.</summary>
    private sealed class Parser(string text, IReadOnlyDictionary<string, Quantity> parameters, string path)
    {
        private int _position;

        public Quantity ParseExpression()
        {
            var left = ParseTerm();

            while (true)
            {
                SkipWhitespace();

                if (Match('+'))
                {
                    left += ParseTerm();
                }
                else if (Match('-'))
                {
                    left -= ParseTerm();
                }
                else
                {
                    return left;
                }
            }
        }

        private Quantity ParseTerm()
        {
            var left = ParseUnary();

            while (true)
            {
                SkipWhitespace();

                if (Match('*'))
                {
                    left *= ParseUnary();
                }
                else if (Match('/'))
                {
                    left /= ParseUnary();
                }
                else
                {
                    return left;
                }
            }
        }

        private Quantity ParseUnary()
        {
            SkipWhitespace();

            if (Match('-'))
            {
                return -ParseUnary();
            }

            Match('+');
            return ParsePrimary();
        }

        private Quantity ParsePrimary()
        {
            SkipWhitespace();

            if (Match('('))
            {
                var inner = ParseExpression();
                SkipWhitespace();

                if (!Match(')'))
                {
                    throw Failure(path, $"unbalanced parentheses in '{text}'", "add the missing ')'");
                }

                return inner;
            }

            if (_position < text.Length && (char.IsDigit(text[_position]) || text[_position] == '.'))
            {
                return ParseNumber();
            }

            if (_position < text.Length && (char.IsLetter(text[_position]) || text[_position] == '_'))
            {
                return ParseIdentifier();
            }

            throw Failure(
                path,
                $"unexpected character at position {_position} of '{text}'",
                "expressions accept numbers, parameter names, + - * /, parentheses, and sqrt, abs, min, max");
        }

        private Quantity ParseNumber()
        {
            var start = _position;

            while (_position < text.Length
                && (char.IsDigit(text[_position]) || text[_position] is '.' or 'e' or 'E'
                    || ((text[_position] is '+' or '-') && _position > start && text[_position - 1] is 'e' or 'E')))
            {
                _position++;
            }

            var literal = text[start.._position];

            return double.TryParse(literal, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? Quantity.Number(value)
                : throw Failure(path, $"'{literal}' is not a number", "use a decimal literal such as 0.5 or 1e-3");
        }

        private Quantity ParseIdentifier()
        {
            var start = _position;

            while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] == '_'))
            {
                _position++;
            }

            var name = text[start.._position];
            SkipWhitespace();

            if (Match('('))
            {
                return ParseCall(name);
            }

            if (parameters.TryGetValue(name, out var value))
            {
                return value;
            }

            throw Failure(
                path,
                $"'{name}' is not a declared parameter",
                parameters.Count == 0
                    ? "declare it under /parameters"
                    : $"declared parameters are: {string.Join(", ", parameters.Keys.Order(StringComparer.Ordinal))}");
        }

        private Quantity ParseCall(string name)
        {
            var arguments = new List<Quantity>();
            SkipWhitespace();

            if (!Match(')'))
            {
                do
                {
                    arguments.Add(ParseExpression());
                    SkipWhitespace();
                }
                while (Match(','));

                if (!Match(')'))
                {
                    throw Failure(path, $"unbalanced parentheses in call to '{name}'", "add the missing ')'");
                }
            }

            return Apply(name, arguments);
        }

        private Quantity Apply(string name, List<Quantity> arguments)
        {
            switch (name)
            {
                case "abs" when arguments.Count == 1:
                    return Quantity.Abs(arguments[0]);

                case "sqrt" when arguments.Count == 1:
                    // Restricted to dimensionless: the square root of a length has
                    // no representation in an integer-exponent dimension system,
                    // and silently dropping the dimension would defeat the check
                    // this evaluator exists to perform.
                    if (!arguments[0].Dimension.IsDimensionless)
                    {
                        throw Failure(
                            path,
                            $"sqrt requires a dimensionless argument, but was given one of dimension "
                            + $"{arguments[0].Dimension}",
                            "form a ratio first, for example sqrt(energy / referenceEnergy)");
                    }

                    return Quantity.Number(Math.Sqrt(arguments[0].SiValue));

                case "floor" when arguments.Count == 1:
                case "mod" when arguments.Count == 2:
                {
                    // Dimensionless only, for the same reason sqrt is: the floor of
                    // a length depends on which unit you take it in, and that is
                    // precisely the ambiguity this evaluator exists to refuse.
                    foreach (var argument in arguments)
                    {
                        if (!argument.Dimension.IsDimensionless)
                        {
                            throw Failure(
                                path,
                                $"{name} requires dimensionless arguments, but was given one of dimension "
                                + $"{argument.Dimension}",
                                "form a ratio first, for example floor(length / pitch)");
                        }
                    }

                    if (name == "floor")
                    {
                        return Quantity.Number(Math.Floor(arguments[0].SiValue));
                    }

                    if (arguments[1].SiValue == 0.0)
                    {
                        throw Failure(path, "mod by zero", "use a non-zero divisor");
                    }

                    // Euclidean rather than truncated, so mod(-1, 2) is 1 and an
                    // index counted backwards still alternates the way it should.
                    var quotient = Math.Floor(arguments[0].SiValue / arguments[1].SiValue);

                    return Quantity.Number(arguments[0].SiValue - (quotient * arguments[1].SiValue));
                }

                case "min" when arguments.Count == 2:
                    return arguments[0] <= arguments[1] ? arguments[0] : arguments[1];

                case "max" when arguments.Count == 2:
                    return arguments[0] >= arguments[1] ? arguments[0] : arguments[1];

                default:
                    throw Failure(
                        path,
                        $"'{name}' is not a known function, or was called with {arguments.Count} arguments",
                        "available: abs(x), sqrt(x), floor(x), mod(a, b), min(a, b), max(a, b)");
            }
        }

        public void ExpectEnd()
        {
            SkipWhitespace();

            if (_position < text.Length)
            {
                throw Failure(
                    path,
                    $"unexpected trailing text '{text[_position..]}' in '{text}'",
                    "check for a missing operator");
            }
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private bool Match(char expected)
        {
            if (_position >= text.Length || text[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }
    }
}
