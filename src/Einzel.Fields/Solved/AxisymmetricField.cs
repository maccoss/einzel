using Einzel.Core.Geometry;

namespace Einzel.Fields.Solved;

/// <summary>
/// A half-plane solve, presented as the three-dimensional field it stands for.
/// </summary>
/// <remarks>
/// <para>
/// The second half of SYM-1: "The solver reduces accordingly and the interpolant
/// reconstructs the full field transparently." The solve happens once in (axial,
/// radial); an ion at (x, y, z) is sampled at (x, sqrt(y^2 + z^2)) and the radial
/// field it finds there is pointed back out along the ion's own azimuth.
/// </para>
/// <para>
/// Transparently is the operative word. Nothing above this knows the field was
/// solved in a half-plane - the integrator asks for a vector at a point and gets
/// one, the conductor test asks whether a point is in metal and gets an answer.
/// An electrode declared as a rectangle in the half-plane is a ring in space, which
/// is exactly what a lens element or a funnel plate is, and what a cross-section
/// could never express.
/// </para>
/// </remarks>
public sealed class AxisymmetricField : IElectrostaticField, IConductorBounded
{
    private readonly IElectrostaticField _halfPlane;

    /// <summary>Wraps a half-plane solve.</summary>
    /// <param name="halfPlane">
    /// The field solved in (x, y) with x axial and y the radius, occupying y &gt;= 0.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="halfPlane"/> is null.</exception>
    public AxisymmetricField(IElectrostaticField halfPlane)
    {
        ArgumentNullException.ThrowIfNull(halfPlane);
        _halfPlane = halfPlane;
    }

    /// <inheritdoc/>
    public double ResolutionLength => _halfPlane.ResolutionLength;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The radial field found in the half-plane, pointed along the ion's azimuth.
    /// On the axis there is no azimuth and no radial direction to point in, so the
    /// transverse components are exactly zero - which is not a special case papering
    /// over a singularity but the physics: an axisymmetric field cannot push an ion
    /// sideways from a place where every sideways is the same.
    /// </para>
    /// <para>
    /// The half-plane solve makes that true of itself as well, because the axis is
    /// a mirror plane and the interpolant reflects across it rather than
    /// extrapolating. Without that the radial field on the axis is small but not
    /// zero, and an ion launched exactly on axis drifts off it.
    /// </para>
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var radius = Math.Sqrt((position.Y * position.Y) + (position.Z * position.Z));
        var sample = new Vec3(position.X, radius, 0.0);

        var field = _halfPlane.ElectricFieldAt(in sample);

        if (radius <= 0.0)
        {
            return new Vec3(field.X, 0.0, 0.0);
        }

        var scale = field.Y / radius;

        return new Vec3(field.X, scale * position.Y, scale * position.Z);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var sample = Sample(in position);
        return _halfPlane.PotentialAt(in sample);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Always zero, so no run is ever taken analytically. The half-plane can say how
    /// far a ray stays field-free in its own plane, but the ray is not the same ray:
    /// a straight line in space traces a curve in (axial, radial), so a direction
    /// mapped once is only instantaneously right. Since the guarantee this returns
    /// is that the field is identically zero over the whole run, the honest answer
    /// is to claim nothing.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var sample = Sample(in position);
        return _halfPlane.SignedDistanceToDiscontinuity(in sample);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An electrode drawn as a rectangle in the half-plane is a ring in space, so
    /// the distance to it is measured from the ion's radius. This is what makes an
    /// aperture an aperture rather than a slot.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position)
    {
        if (_halfPlane is not IConductorBounded bounded)
        {
            return double.PositiveInfinity;
        }

        var sample = Sample(in position);
        return bounded.SignedDistanceToConductor(in sample);
    }

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position)
    {
        if (_halfPlane is not IConductorBounded bounded)
        {
            return null;
        }

        var sample = Sample(in position);
        return bounded.ConductorAt(in sample);
    }

    private static Vec3 Sample(in Vec3 position) =>
        new(position.X, Math.Sqrt((position.Y * position.Y) + (position.Z * position.Z)), 0.0);
}
