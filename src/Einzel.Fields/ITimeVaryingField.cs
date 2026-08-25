using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field that changes with time: the RF path.
/// </summary>
/// <remarks>
/// <para>
/// Kept as a separate interface rather than a time argument added to
/// <see cref="IElectrostaticField"/>, because the distinction is real and worth
/// keeping visible. A static field conserves energy and can be sampled in any
/// order; a time-varying one does neither. Code that has checked for this
/// interface knows which world it is in, and code that has not keeps the fast path
/// unchanged.
/// </para>
/// <para>
/// The inherited time-free members sample at t = 0. That is a real field - the
/// instantaneous one at the start of the cycle - so the inherited contract is
/// honoured rather than stubbed, and a caller that ignores time gets a definite
/// answer instead of an arbitrary one.
/// </para>
/// <para>
/// Almost all of RF costs nothing to build here, because the electric field is
/// linear in the applied potentials: solve once per electrode at unit potential
/// and any voltage set is a weighted sum of the results. Making those weights
/// functions of time <em>is</em> radio frequency, with nothing re-solved. The
/// basis machinery that makes it free has been in place since the field solver.
/// </para>
/// </remarks>
public interface ITimeVaryingField : IElectrostaticField
{
    /// <summary>The electric field vector at a point and an instant.</summary>
    /// <param name="position">The point, in metres.</param>
    /// <param name="timeSeconds">The instant, in seconds from the launch.</param>
    /// <returns>The field vector, in volts per metre.</returns>
    Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds);

    /// <summary>The electric potential at a point and an instant.</summary>
    /// <param name="position">The point, in metres.</param>
    /// <param name="timeSeconds">The instant, in seconds from the launch.</param>
    /// <returns>The potential, in volts.</returns>
    double PotentialAt(in Vec3 position, double timeSeconds);

    /// <summary>
    /// The shortest period in the drive, in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What stops the step controller outrunning the field. A gridded field
    /// carries no information below its node spacing and reports
    /// <see cref="IElectrostaticField.ResolutionLength"/> so a step cannot skip
    /// over it; a driven field carries none below its period, and the failure
    /// looks identical.
    /// </para>
    /// <para>
    /// It is worth being explicit about why an error estimator will not catch it.
    /// An embedded estimate compares two Runge-Kutta solutions of the same
    /// problem, and if every stage of a step happens to sample the same phase of
    /// the cycle both solutions agree and the step is accepted as accurate. It was
    /// accurate, for the field the step was shown. It was not shown the field.
    /// </para>
    /// </remarks>
    double ShortestPeriodSeconds { get; }
}
