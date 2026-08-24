namespace Einzel.Core.Results;

/// <summary>
/// What stands behind a reported value: the ensemble size or convergence
/// measure that GRD-1 requires alongside the number and its uncertainty.
/// </summary>
/// <remarks>
/// The distinction matters when reading a result. A transmission of 92 percent
/// from 100 ions and from 100,000 ions carry the same uncertainty apparatus but
/// very different weight, and a flight time is supported by grid convergence
/// rather than by an ensemble at all. Modelling these as separate cases stops a
/// caller from reading one as the other.
/// </remarks>
public abstract record Evidence
{
    private Evidence()
    {
    }

    /// <summary>A statistical result, supported by an ensemble. Accuracy class S.</summary>
    /// <param name="EnsembleSize">Number of ions in the ensemble.</param>
    /// <param name="Converged">
    /// Whether the ensemble met its convergence criterion. A result reported from
    /// an unconverged ensemble carries a warning; it is not silently withheld.
    /// </param>
    public sealed record Ensemble(int EnsembleSize, bool Converged) : Evidence;

    /// <summary>
    /// A deterministic result, supported by a convergence study. Accuracy classes
    /// T and B.
    /// </summary>
    /// <param name="Measure">
    /// What was refined, for example "grid spacing" or "integrator tolerance".
    /// </param>
    /// <param name="ObservedOrder">
    /// The convergence order observed across refinements. Spec section 19
    /// requires asserting this against the nominal order rather than assuming it.
    /// </param>
    /// <param name="NominalOrder">The order the scheme is expected to achieve.</param>
    /// <param name="ResidualSi">
    /// The residual between the two finest refinements, in SI units of the
    /// reported quantity.
    /// </param>
    public sealed record Convergence(
        string Measure,
        double ObservedOrder,
        double NominalOrder,
        double ResidualSi) : Evidence;

    /// <summary>
    /// A closed-form result, supported by derivation rather than by computation.
    /// Used by the analytic test tier of spec section 19.
    /// </summary>
    /// <param name="Reference">
    /// The derivation or published result the value comes from.
    /// </param>
    public sealed record Analytic(string Reference) : Evidence;
}
