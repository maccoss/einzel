namespace Einzel.Core.Errors;

/// <summary>
/// The error taxonomy. Codes are stable identifiers that callers branch on, so
/// they are added but never reworded or repurposed.
/// </summary>
/// <remarks>
/// Spec section 21 lists the error taxonomy as a Phase 1 deliverable, alongside
/// the schema, for the same reason: both are compatibility surfaces that agent
/// workflows bind to, and churn in either breaks callers silently.
/// </remarks>
public static class ErrorCodes
{
    /// <summary>A quantity was supplied without a unit. Spec section 9.</summary>
    public const string UnitsRequired = "UNITS_REQUIRED";

    /// <summary>A unit symbol was not recognised.</summary>
    public const string UnitsUnknown = "UNITS_UNKNOWN";

    /// <summary>
    /// Two quantities of different physical dimension were combined, or a value
    /// was supplied in a unit of the wrong dimension for its field.
    /// </summary>
    public const string UnitsIncompatible = "UNITS_INCOMPATIBLE";

    /// <summary>A value fell outside its declared bounds.</summary>
    public const string ValueOutOfBounds = "VALUE_OUT_OF_BOUNDS";

    /// <summary>The model document did not satisfy the schema.</summary>
    public const string SchemaInvalid = "SCHEMA_INVALID";

    /// <summary>
    /// The selected transport mode is outside its validity for the regime.
    /// Non-suppressible per REG-2.
    /// </summary>
    public const string RegimeInvalid = "REGIME_INVALID";

    /// <summary>A solve, ensemble, or optimisation failed to converge.</summary>
    public const string ConvergenceFailed = "CONVERGENCE_FAILED";

    /// <summary>An operation exceeding the cost threshold was refused. GRD-8.</summary>
    public const string CostGateRefused = "COST_GATE_REFUSED";

    /// <summary>
    /// The project pins an engine version other than the one running. UPD-5.
    /// </summary>
    public const string EnginePinMismatch = "ENGINE_PIN_MISMATCH";

    /// <summary>A defect in the platform. Always a bug report.</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
