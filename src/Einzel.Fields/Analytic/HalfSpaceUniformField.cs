using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Fields;

/// <summary>
/// Field-free on one side of a plane, uniform and retarding on the other.
/// </summary>
/// <remarks>
/// <para>
/// The field primitive an ideal single-stage ion mirror is built from: an ion
/// crosses the plane, decelerates linearly, turns, and comes back out. Composing
/// it with a drift length gives the single-stage reflectron of the analytic test
/// tier, whose first-order energy focusing condition — total field-free path
/// equal to four penetration depths — is one of the sharpest checks available
/// that the integrator and the field agree.
/// </para>
/// <para>
/// Named for what it is rather than for what it is used to build. Architecture
/// invariant 2 keeps device classes above Einzel.Library; a reflectron is an
/// arrangement of this field, not a thing the engine knows about.
/// </para>
/// <para>
/// The potential is continuous across the plane and the field is discontinuous,
/// which is physically an idealisation — a real mirror has a fringe. That
/// discontinuity is deliberate here: it gives an exact closed form to test
/// against, and it is the integrator's job not to step across the boundary
/// blindly.
/// </para>
/// </remarks>
public sealed class HalfSpaceUniformField : IElectrostaticField
{
    private readonly Vec3 _origin;
    private readonly Vec3 _normal;
    private readonly double _gradientSi;

    private HalfSpaceUniformField(Vec3 origin, Vec3 normal, double gradientSi)
    {
        _origin = origin;
        _normal = normal;
        _gradientSi = gradientSi;
    }

    /// <summary>
    /// Creates a retarding half-space.
    /// </summary>
    /// <param name="planePoint">A point on the boundary plane.</param>
    /// <param name="inwardNormal">
    /// Unit normal pointing into the field region, along the direction an
    /// entering ion travels. Normalised internally.
    /// </param>
    /// <param name="potentialGradient">
    /// The rate at which potential rises with depth, of electric-field dimension.
    /// Positive values retard a positive ion entering along the normal.
    /// </param>
    /// <returns>The field.</returns>
    /// <exception cref="Core.Errors.EinzelException">The gradient has the wrong dimension.</exception>
    public static HalfSpaceUniformField Create(Vec3 planePoint, Vec3 inwardNormal, Quantity potentialGradient) =>
        new(planePoint, inwardNormal.Normalized(), potentialGradient.In("V/m"));

    /// <summary>
    /// Creates a retarding half-space specified by the depth at which a given
    /// potential is reached, which is how an ion mirror is actually designed.
    /// </summary>
    /// <param name="planePoint">A point on the boundary plane.</param>
    /// <param name="inwardNormal">Unit normal pointing into the field region.</param>
    /// <param name="capPotential">The potential reached at <paramref name="depth"/>.</param>
    /// <param name="depth">The depth at which that potential is reached.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The depth is not positive.</exception>
    /// <exception cref="Core.Errors.EinzelException">A quantity has the wrong dimension.</exception>
    /// <remarks>
    /// An ion accelerated through a potential equal to <paramref name="capPotential"/>
    /// turns exactly at <paramref name="depth"/>, which makes this the natural
    /// way to place the turning point where a design wants it.
    /// </remarks>
    public static HalfSpaceUniformField FromTurningDepth(
        Vec3 planePoint,
        Vec3 inwardNormal,
        Quantity capPotential,
        Quantity depth)
    {
        var depthM = depth.In("m");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depthM);

        return new HalfSpaceUniformField(planePoint, inwardNormal.Normalized(), capPotential.In("V") / depthM);
    }

    /// <summary>The potential gradient inside the field region, in volts per metre.</summary>
    public double PotentialGradientSi => _gradientSi;

    /// <summary>Signed depth into the field region; negative outside it.</summary>
    /// <param name="position">The point, in metres.</param>
    /// <returns>The signed depth, in metres.</returns>
    public double DepthAt(in Vec3 position) => Vec3.Dot(position - _origin, _normal);

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position) =>
        DepthAt(position) < 0.0 ? Vec3.Zero : _normal * -_gradientSi;

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var depth = DepthAt(position);
        return depth < 0.0 ? 0.0 : _gradientSi * depth;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The boundary plane is the discontinuity: the field steps from zero to the
    /// full gradient across it. Signed depth is exactly the measure the
    /// integrator needs, positive inside and negative outside.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position) => DepthAt(in position);

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var depth = DepthAt(position);

        if (depth > 0.0)
        {
            return 0.0;
        }

        var approach = Vec3.Dot(direction, _normal);

        // Receding from the plane, or running parallel to it: field-free forever,
        // and the caller clamps by the remaining flight time. Depth of exactly
        // zero counts as outside for an outbound ion, which is the case that
        // matters — an ion leaving the mirror lands precisely on the boundary,
        // and treating that as inside would apply the mirror field to a step it
        // spends entirely outside.
        if (approach <= 0.0)
        {
            return double.PositiveInfinity;
        }

        // Stop exactly at the plane. Stepping past it would advance an ion in a
        // straight line through a region where it should have been decelerating.
        return -depth / approach;
    }
}
