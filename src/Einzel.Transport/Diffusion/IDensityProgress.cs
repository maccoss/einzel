namespace Einzel.Transport.Diffusion;

/// <summary>One population's state part way through a density solve.</summary>
/// <param name="Name">
/// Which species it is, or empty where the run has only one and the model named none.
/// </param>
/// <param name="Density">
/// Its density as it stands, <b>not a copy</b> — the solver's live buffer, valid for the
/// duration of the call and overwritten by the next step. A consumer that wants to keep it
/// clones it.
/// </param>
public sealed record DensityProgressSpecies(string Name, DensityField Density);

/// <summary>How far a density solve has got, while it is still going.</summary>
/// <param name="Steps">Steps taken so far.</param>
/// <param name="TimeSeconds">Where it has reached on the run's own clock.</param>
/// <param name="UntilSeconds">Where it is going.</param>
/// <param name="StepSeconds">The step it is currently taking.</param>
/// <param name="CollectedIons">Real ions that have reached the detector so far.</param>
/// <param name="Species">One entry per population, in the order they were declared.</param>
public sealed record DensityProgressReport(
    int Steps,
    double TimeSeconds,
    double UntilSeconds,
    double StepSeconds,
    double CollectedIons,
    IReadOnlyList<DensityProgressSpecies> Species);

/// <summary>
/// Told how far a density solve has got, so a run measured in hours can say something
/// before it ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes is an engineering one rather than a physical one.</b> A driven
/// diffusive window is set by a Courant limit against a ponderomotive gradient, so it is
/// hundreds of thousands of steps whatever the per-step cost — and until this existed a
/// run of that size produced its first byte of output when it finished. The TIMS front-end
/// sequence failed to finish three times, the last of them because the machine rebooted
/// after seven and a half hours, and on none of the three was anything observed: whether
/// the estimate was low or the run did not terminate could not be told apart.
/// </para>
/// <para>
/// <b>Two methods, because the policy and the data belong on opposite sides.</b>
/// <see cref="Wants"/> is asked every step and gets two doubles, so a consumer that reports
/// once a minute costs one virtual call per step and nothing else. <see cref="Reached"/> is
/// where the expensive part lives — a centroid and a width are full passes over the grid,
/// and computing them every step would make watching a run cost more than the run. A
/// single-method interface would have to allocate a report per step and would invite
/// exactly that.
/// </para>
/// <para>
/// <b>An observer must not change the answer.</b> Nothing here is handed to the solver;
/// what comes back is read and the solve proceeds from its own state, so a run with a
/// consumer attached and one without are bit-identical. That is asserted rather than
/// assumed, the same way snapshot recording is — a recorder that perturbs what it records
/// is worse than no recorder, because its output looks like a measurement.
/// </para>
/// </remarks>
public interface IDensityProgress
{
    /// <summary>Whether a report is wanted now.</summary>
    /// <param name="steps">Steps taken so far.</param>
    /// <param name="timeSeconds">Where the run has reached on its own clock.</param>
    /// <returns>Whether to call <see cref="Reached"/> for this step.</returns>
    /// <remarks>
    /// Asked once per step, so it must be cheap — a wall-clock comparison or a step count,
    /// not anything that touches the grid.
    /// </remarks>
    bool Wants(int steps, double timeSeconds);

    /// <summary>What the solve has reached.</summary>
    /// <param name="report">Where it has got to, and the live densities.</param>
    /// <remarks>
    /// Called only where <see cref="Wants"/> said so. The densities in the report are the
    /// solver's own buffers rather than copies.
    /// </remarks>
    void Reached(DensityProgressReport report);
}
