namespace Einzel.Core.Model;

/// <summary>
/// What a two-dimensional solve is a cross-section of.
/// </summary>
/// <remarks>
/// <para>
/// SYM-1: "A geometry subtree may declare cylindrical symmetry, a mirror plane, or
/// discrete periodicity. The solver reduces accordingly and the interpolant
/// reconstructs the full field transparently."
/// </para>
/// <para>
/// The choice is not a detail of presentation - it changes the operator. A
/// translationally invariant solve is Laplace in the plane; an axisymmetric one
/// carries the extra radial term that comes from the shrinking circumference of a
/// ring as it approaches the axis, and a field solved with the wrong one converges
/// happily to the wrong answer.
/// </para>
/// </remarks>
public enum SolveSymmetry
{
    /// <summary>
    /// A cross-section extruded along the third axis. The default, and what a mass
    /// filter or a rectilinear trap is.
    /// </summary>
    Translational,

    /// <summary>
    /// A half-plane rotated about an axis: x is axial, y is the radius, and the
    /// solve occupies y >= 0.
    /// </summary>
    /// <remarks>
    /// What most of the device table in spec section 1 actually is - einzel lenses,
    /// ion funnels, stacked-ring guides, apertures, and the drift tubes between
    /// them. A cross-section cannot express any of them, because the thing that
    /// makes them work is that the electrode wraps all the way round.
    /// </remarks>
    Cylindrical,
}
