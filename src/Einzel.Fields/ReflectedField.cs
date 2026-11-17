using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field reflected through a plane normal to x.
/// </summary>
/// <remarks>
/// The second mirror of a pair is the first one turned around, so it is built by
/// reflection rather than solved again. That is not only cheaper: it makes the
/// two halves identical by construction, so a difference between the inbound and
/// outbound legs of a trajectory cannot come from the two mirrors having been
/// meshed differently.
/// </remarks>
/// <param name="inner">The declared half.</param>
/// <param name="planeX">The mirror plane, as an x coordinate.</param>
/// <param name="imageAbove">
/// Which side of the plane the image lies on: true where the declared half is below the plane
/// (the usual <c>reflectAboutX</c> at a solve's <c>maxX</c>), false where it is above, and null
/// where the declared half has no edge on the plane (an analytic field filling all space), so the
/// image answers everywhere but on the plane itself. See <see cref="Contributes"/>.
/// </param>
public sealed class ReflectedField(IElectrostaticField inner, double planeX, bool? imageAbove = null)
    : IElectrostaticField, IConductorBounded
{
    /// <inheritdoc/>
    /// <remarks>
    /// The inner geometry seen through the mirror, so an electrode in the original
    /// half has a solid twin in the reflected one. Leaving this out would make a
    /// reflected mirror pair solid on one side and transparent on the other, which
    /// is worse than transparent on both.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position) =>
        inner is IConductorBounded bounded
            ? bounded.SignedDistanceToConductor(Reflect(in position))
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        inner is IConductorBounded bounded ? bounded.ConductorAt(Reflect(in position)) : null;

    /// <inheritdoc/>
    public double NearestConductor(in Vec3 position, out string? name)
    {
        name = null;

        return inner is IConductorBounded bounded
            ? bounded.NearestConductor(Reflect(in position), out name)
            : double.PositiveInfinity;
    }

    private readonly IElectrostaticField _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private Vec3 Reflect(in Vec3 position) => new((2.0 * planeX) - position.X, position.Y, position.Z);

    /// <summary>Whether the image answers at a point at all.</summary>
    /// <param name="position">The point, in the world.</param>
    /// <returns>
    /// True strictly on the image's side of the plane, judged on the point's own x; false on the
    /// declared side and on the plane. With no side given, true everywhere but on the plane.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE PLANE BELONGS TO THE DECLARED HALF. This field is summed with the one it mirrors, and
    /// a point on the plane is its own image: where the declared half reaches the plane - a solve
    /// whose edge is the mirror, which is how a reflected solve is written - both answered for
    /// it, and the sum there was twice the potential and twice the field along the plane. A half
    /// einzel lens read 999.5 V on the plane of a 500 V electrode.
    /// </para>
    /// <para>
    /// <b>Judged on the point, not on its reflection, and that is the whole fix.</b> The first
    /// version gave the image nothing only where <c>x == planeX</c>, on the argument that a point
    /// one rounding away has its image outside the declared half. Not always: one ulp below a
    /// power-of-two plane, <c>2p - x</c> rounds back onto the plane, both halves answered, and the
    /// sum doubled there. The side of the plane a point is on is exact; its reflected coordinate
    /// is a rounded sum. So the declared half answers on its side and on the plane, the image on
    /// its side only, and no rounding of the reflection can put a point in both.
    /// </para>
    /// </remarks>
    public bool Contributes(in Vec3 position) => imageAbove switch
    {
        true => position.X > planeX,
        false => position.X < planeX,
        null => position.X != planeX,
    };

    /// <summary>Which side of its plane a reflected solve's image lies on.</summary>
    /// <param name="minX">The low x edge of the declared half.</param>
    /// <param name="maxX">The high x edge of the declared half.</param>
    /// <param name="planeX">The mirror plane.</param>
    /// <returns>
    /// True where the declared half is below the plane - the plane at <paramref name="maxX"/>, or
    /// nearer it than <paramref name="minX"/> - so the image is above.
    /// </returns>
    public static bool ImageAbove(double minX, double maxX, double planeX) => planeX - minX >= maxX - planeX;

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        if (!Contributes(in position))
        {
            return default;
        }

        var mirrored = Reflect(in position);
        var field = _inner.ElectricFieldAt(in mirrored);

        // x flips with the coordinate; y and z do not.
        return new Vec3(-field.X, field.Y, field.Z);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        if (!Contributes(in position))
        {
            return 0.0;
        }

        var mirrored = Reflect(in position);
        return _inner.PotentialAt(in mirrored);
    }

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var mirrored = Reflect(in position);
        var mirroredDirection = new Vec3(-direction.X, direction.Y, direction.Z);
        return _inner.FieldFreeRunLength(in mirrored, in mirroredDirection);
    }

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var mirrored = Reflect(in position);
        return _inner.SignedDistanceToDiscontinuity(in mirrored);
    }

    /// <inheritdoc/>
    public double ResolutionLength => _inner.ResolutionLength;
}
