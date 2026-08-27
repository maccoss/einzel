using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field that knows where its conductors are, so an ion can hit one.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IElectrostaticField"/> because most fields have no
/// conductors to speak of. A uniform field, a half-space, an ideal quadrupole -
/// each is an expression valid everywhere, with no surface anywhere for an ion to
/// land on. Only a solved geometry has electrodes as objects, and only it can say
/// that a trajectory has entered one.
/// </para>
/// <para>
/// The distinction matters for what a run means. Without this, every ion reaches
/// the detector and transmission is 100% by construction - a slot is decorative
/// and an aperture is scenery. ACC-5 asks for transmission itemised by loss
/// surface and refuses the bare figure ("never 92 percent"); a loss surface is
/// exactly what this provides.
/// </para>
/// <para>
/// Expressed as a signed distance rather than as a segment intersection so that
/// the integrator can reuse its existing exact-landing machinery: an ion strikes a
/// conductor at the zero of this function, which is the same kind of event as a
/// stopping surface and is found the same way. A separate intersection test would
/// have been a second event mechanism with its own edge cases.
/// </para>
/// </remarks>
public interface IConductorBounded
{
    /// <summary>
    /// Distance to the nearest conductor surface, in metres, negative inside one.
    /// </summary>
    /// <param name="position">Where to measure from.</param>
    /// <returns>The signed distance; positive infinity when there are no conductors.</returns>
    /// <remarks>
    /// The minimum over every conductor, which is exact outside them all and an
    /// underestimate within one. Only the sign and the location of the zero matter
    /// here, and both are right.
    /// </remarks>
    double SignedDistanceToConductor(in Vec3 position);

    /// <summary>Which conductor a point is in, or nearest to.</summary>
    /// <param name="position">Where to look.</param>
    /// <returns>The electrode's name, or null when there are no conductors.</returns>
    /// <remarks>
    /// Called once, at the point of impact, so it is free to be the slower of the
    /// two. Named rather than indexed because the name is what a loss itemisation
    /// reports and what a model author wrote.
    /// </remarks>
    string? ConductorAt(in Vec3 position);
}
