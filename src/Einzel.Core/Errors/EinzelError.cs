namespace Einzel.Core.Errors;

/// <summary>How much weight an <see cref="EinzelError"/> carries.</summary>
public enum ErrorSeverity
{
    /// <summary>Informational; the operation proceeds unchanged.</summary>
    Info,

    /// <summary>The operation proceeds, but the result is qualified.</summary>
    Warning,

    /// <summary>The operation cannot proceed as specified.</summary>
    Error,
}

/// <summary>
/// A value as observed, with its unit, so an error can state what was actually
/// seen rather than only what was expected.
/// </summary>
/// <param name="Value">The observed magnitude, in the stated unit.</param>
/// <param name="Unit">
/// The unit symbol, or <c>ratio</c> for a dimensionless comparison.
/// </param>
public sealed record ObservedValue(double Value, string Unit);

/// <summary>
/// The platform error object required by AGT-3: errors are recovery
/// instructions.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 2 fixes the shape. Every field exists so that a caller — very
/// often an agent, which cannot ask a follow-up question — can act on the error
/// without further information: a machine-readable <see cref="Code"/> to branch
/// on, the <see cref="Path"/> of the offending input, the
/// <see cref="Constraint"/> that was violated, the <see cref="Observed"/> value
/// that violated it, and a concrete <see cref="Suggestion"/>.
/// </para>
/// <para>
/// A message that says only "invalid transport mode" forces a guess. The worked
/// example in the spec says the collision frequency was 42.7 times the RF
/// frequency where the limit is 0.1, and suggests two specific corrections.
/// That difference is the requirement.
/// </para>
/// </remarks>
public sealed record EinzelError
{
    /// <summary>
    /// Stable machine-readable code from <see cref="ErrorCodes"/>. Callers branch
    /// on this; it is part of the platform's compatibility surface and does not
    /// change to improve wording.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// JSON Pointer (RFC 6901) to the offending location in the model document,
    /// for example <c>/devices/funnel_1/transport</c>.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The violated constraint, stated in physical terms rather than as a code
    /// path, for example "trajectory integration requires collision frequency
    /// below 0.1 x RF frequency".
    /// </summary>
    public required string Constraint { get; init; }

    /// <summary>What was actually observed, where a value can be named.</summary>
    public ObservedValue? Observed { get; init; }

    /// <summary>
    /// A concrete correction, naming values where possible. The suggestion is
    /// what makes the object actionable without a second round trip.
    /// </summary>
    public string? Suggestion { get; init; }

    /// <summary>Severity. Defaults to <see cref="ErrorSeverity.Error"/>.</summary>
    public ErrorSeverity Severity { get; init; } = ErrorSeverity.Error;

    /// <summary>Renders the error for a terminal, one line per populated field.</summary>
    /// <returns>A human-readable rendering; the JSON form is the machine surface.</returns>
    public override string ToString()
    {
        var text = $"{Code} at {Path}: {Constraint}";

        if (Observed is not null)
        {
            text += $" (observed {Observed.Value} {Observed.Unit})";
        }

        if (!string.IsNullOrEmpty(Suggestion))
        {
            text += $" -- {Suggestion}";
        }

        return text;
    }
}
