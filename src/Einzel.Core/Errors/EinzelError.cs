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

    /// <summary>
    /// The path a placeholder stands in for, supplied by whoever caught the error.
    /// </summary>
    /// <param name="path">JSON Pointer to the location the caller was working on.</param>
    /// <returns>This error, located.</returns>
    /// <remarks>
    /// <para>
    /// <b>Arithmetic cannot know where it is.</b> A dimension mismatch is raised inside
    /// <c>Quantity</c>, which has two operands and no document, so it names the root as a
    /// placeholder. The caller catching it does know - it was resolving a named parameter
    /// or a numbered electrode's face - and AGT-3 is the requirement that an error be a
    /// recovery instruction, which a path of <c>/</c> is not.
    /// </para>
    /// <para>
    /// <b>Only the placeholder is replaced</b>, so an error raised deeper with a genuine
    /// path keeps it: filling in a location must never overwrite a more specific one.
    /// </para>
    /// <para>
    /// The consequence of dropping it, measured on a real document: a thickness written
    /// without a unit made 64 identical <c>UNITS_INCOMPATIBLE</c> errors, every one of
    /// them located at <c>/</c>, which names neither which electrode nor which face and
    /// is the same message a single mistake would give.
    /// </para>
    /// </remarks>
    public EinzelError At(string path) => Path == "/" ? this with { Path = path } : this;

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
