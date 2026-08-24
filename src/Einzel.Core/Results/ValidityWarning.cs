namespace Einzel.Core.Results;

/// <summary>
/// How serious a <see cref="ValidityWarning"/> is, and — by GRD-3 — whether it
/// can be silenced.
/// </summary>
public enum WarningSeverity
{
    /// <summary>
    /// Advice a caller may reasonably not want repeated. The only suppressible
    /// severity.
    /// </summary>
    Advisory,

    /// <summary>
    /// The result is usable but qualified: an unconverged ensemble, a coarse
    /// mesh, an approximation near its limit.
    /// </summary>
    Qualified,

    /// <summary>
    /// The result was computed outside the validity of the model used. GRD-3 and
    /// REG-2: never suppressible, in any mode, by any caller.
    /// </summary>
    ValidityViolation,

    /// <summary>
    /// The result was produced by an engine version below the published floor, or
    /// by a preview tier, or by a third-party extension. GRD-5, GRD-6, GRD-11:
    /// travels with the artifact and is never suppressible.
    /// </summary>
    Provenance,
}

/// <summary>
/// A warning attached to a result, travelling with it through every layer.
/// </summary>
/// <remarks>
/// <para>
/// GRD-2 requires validity warnings to propagate through engine, command layer,
/// CLI output, MCP response, exported file, rendered figure, and video. GRD-3
/// makes anything above advisory non-suppressible, including in batch mode.
/// </para>
/// <para>
/// <see cref="IsSuppressible"/> is computed from severity rather than set by the
/// caller. A constructor parameter would eventually be passed <c>true</c> by
/// someone in a hurry, which is precisely the failure GRD-3 exists to prevent.
/// </para>
/// </remarks>
/// <param name="Code">Stable machine-readable code, as for <see cref="Errors.EinzelError"/>.</param>
/// <param name="Message">What is wrong, in physical terms.</param>
/// <param name="Severity">How serious, which determines suppressibility.</param>
public sealed record ValidityWarning(string Code, string Message, WarningSeverity Severity)
{
    /// <summary>
    /// Whether a caller may silence this warning. True only for
    /// <see cref="WarningSeverity.Advisory"/>, per GRD-3.
    /// </summary>
    public bool IsSuppressible => Severity == WarningSeverity.Advisory;

    /// <summary>Renders the warning for a terminal.</summary>
    /// <returns>Severity, code, and message.</returns>
    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}
