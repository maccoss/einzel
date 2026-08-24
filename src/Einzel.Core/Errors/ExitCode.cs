namespace Einzel.Core.Errors;

/// <summary>
/// Process exit codes. CLI-3 requires meaningful and documented codes, with
/// distinct values for each failure class, so a script or agent can branch
/// without parsing output.
/// </summary>
/// <remarks>
/// The mapping lives in Einzel.Core rather than in Einzel.Cli because it is part
/// of the error taxonomy: a failure class and its exit code are the same
/// decision, and AGT-2 requires the CLI and MCP to agree on it.
/// </remarks>
public enum ExitCode
{
    /// <summary>The operation completed. Warnings may still be attached.</summary>
    Success = 0,

    /// <summary>
    /// The model or study failed validation: schema, units, bounds, solvability.
    /// </summary>
    ValidationFailure = 1,

    /// <summary>
    /// The selected transport mode is outside its validity for the regime. REG-2.
    /// </summary>
    RegimeViolation = 2,

    /// <summary>
    /// The operation exceeded the configured cost threshold and no prior estimate
    /// was supplied. GRD-8.
    /// </summary>
    CostGateRefused = 3,

    /// <summary>A solve, ensemble, or optimisation failed to converge.</summary>
    ConvergenceFailure = 4,

    /// <summary>
    /// The project pins an engine version other than the one running. UPD-5
    /// requires reporting the mismatch and exiting distinctly rather than
    /// proceeding.
    /// </summary>
    EnginePinMismatch = 5,

    /// <summary>A defect in the platform.</summary>
    InternalError = 6,
}
