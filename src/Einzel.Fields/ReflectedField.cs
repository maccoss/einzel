using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field reflected through a plane normal to x.
/// </summary>
/// <remarks>
/// <para>
/// The second mirror of a pair is the first one turned around, so it is built by
/// reflection rather than solved again. That is not only cheaper: it makes the
/// two halves identical by construction, so a difference between the inbound and
/// outbound legs of a trajectory cannot come from the two mirrors having been
/// meshed differently.
/// </para>
/// <para>
/// <b>The side rule is the <see cref="Reflection"/>'s, held, not restated.</b> Where the image
/// answers, and which side it takes, were written here as well as there, and two statements of
/// one rule are two rules the first time either is edited.
/// </para>
/// </remarks>
public sealed class ReflectedField : IElectrostaticField, IConductorBounded
{
    private readonly IElectrostaticField _inner;
    private readonly Reflection _reflection;

    /// <summary>The image of a declared half, answering where a reflection says it does.</summary>
    /// <param name="inner">The declared half.</param>
    /// <param name="reflection">The plane, and the side the image takes.</param>
    public ReflectedField(IElectrostaticField inner, Reflection reflection)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reflection = reflection ?? throw new ArgumentNullException(nameof(reflection));
    }

    /// <summary>The image of a declared half through a plane, answering everywhere.</summary>
    /// <param name="inner">The declared half.</param>
    /// <param name="planeX">The mirror plane, as an x coordinate.</param>
    /// <remarks>
    /// The constructor <c>main</c> had, kept so the type does not lose it: an image answering on
    /// both sides of the plane, which is the overlapping sum. A side is not taken here - it is the
    /// reflection's to decide from the declared half's box (<see cref="Reflection.Of"/>), and a
    /// constructor taking one let a caller give a side the rule would not.
    /// </remarks>
    [Obsolete("Build the image through a Reflection - Reflection.Of(plane, minX, maxX).Image(declared) - which decides the side from the declared half's box.")]
    public ReflectedField(IElectrostaticField inner, double planeX)
        : this(inner, new Reflection(planeX, null))
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The inner geometry seen through the mirror, so an electrode in the original
    /// half has a solid twin in the reflected one. Leaving this out would make a
    /// reflected mirror pair solid on one side and transparent on the other, which
    /// is worse than transparent on both. And only where the image answers at all
    /// (<see cref="Contributes"/>): an image conductor on the declared side would stop an ion,
    /// and be itemized as a loss, where the image's field had been declared absent - metal
    /// standing where the solve holds none.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position) =>
        _inner is IConductorBounded bounded && Contributes(in position)
            ? bounded.SignedDistanceToConductor(Reflect(in position))
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        _inner is IConductorBounded bounded && Contributes(in position) ? bounded.ConductorAt(Reflect(in position)) : null;

    /// <inheritdoc/>
    public double NearestConductor(in Vec3 position, out string? name)
    {
        name = null;

        return _inner is IConductorBounded bounded && Contributes(in position)
            ? bounded.NearestConductor(Reflect(in position), out name)
            : double.PositiveInfinity;
    }

    private Vec3 Reflect(in Vec3 position) => new(_reflection.ReflectX(position.X), position.Y, position.Z);

    /// <summary>Whether the image answers at a point at all.</summary>
    /// <param name="position">The point, in the world.</param>
    /// <returns>
    /// True strictly on the image's side of the plane, judged on the point's own x; false on the
    /// declared side and on the plane. With no side given, true everywhere: a half with no edge on
    /// the plane overlaps its image, and the sum there is both. Leaving the image out on the plane
    /// alone, as the first version did, made that sum jump at the plane.
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
    public bool Contributes(in Vec3 position) => _reflection.ImageAnswersAt(position.X);

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
    /// <remarks>
    /// <para>
    /// Where the image answers nothing (<see cref="Contributes"/>) it is field-free until the ray
    /// reaches the plane, and from there as far as its own field is free: the distance to the
    /// plane plus the image's run from the point the ray crosses it, along a ray heading for the
    /// image's side; unbounded along one that is not. The inner field's own run, asked at the
    /// reflected point, says nothing on this side - a half that is free beyond its own edge
    /// claimed the image free straight through the plane and into its field.
    /// </para>
    /// <para>
    /// <b>The distance to the plane alone was zero on the plane.</b> The plane is the declared
    /// half's, so a ray standing on it and heading across had a run of nothing, the sum's shortest
    /// run was nothing, and the analytic drift stopped at every mid-plane crossing of a reflected
    /// pair - one integrated step through a field-free gap for each pass. The image answers just
    /// past the plane, so its run from the crossing is what follows.
    /// </para>
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var mirroredDirection = new Vec3(-direction.X, direction.Y, direction.Z);

        if (!Contributes(in position))
        {
            var towards = _reflection.ImageAbove == true ? direction.X : -direction.X;

            if (towards <= 0.0)
            {
                return double.PositiveInfinity;
            }

            var toPlane = Math.Abs(_reflection.PlaneX - position.X) / towards;

            // Where the ray crosses the plane, x set to the plane exactly rather than to a
            // rounded sum, seen through the mirror: the plane is its own image.
            var crossing = new Vec3(_reflection.PlaneX, position.Y + (toPlane * direction.Y), position.Z + (toPlane * direction.Z));
            var beyond = _inner.FieldFreeRunLength(in crossing, in mirroredDirection);

            return toPlane + beyond;
        }

        var mirrored = Reflect(in position);
        return _inner.FieldFreeRunLength(in mirrored, in mirroredDirection);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The declared half's measure seen through the mirror where the image answers
    /// (<see cref="Contributes"/>), and plus infinity where it does not - the image declares exactly
    /// the mirror of what the declared half declares, and only where the image is: not the plane,
    /// which the declared half carries as a member of its own where it is a surface.
    /// </para>
    /// <para>
    /// Read on both sides, the image's measure changed sign in the declared half wherever a surface
    /// of the declared field lay on the image's side, and an ion in the declared half landed on the
    /// mirror of a surface the declared half does not have. Gated to plus infinity, as a bounded
    /// element's inner members are, it changes at the plane only by going between finite and
    /// infinite, which the integrator reads by its sign and checks again at a landing's bracket, so
    /// a real crossing of an image surface just past the plane is still caught. Combined into one
    /// sign, as it once was, gating could cancel a surface of the declared half at the rim; tracked
    /// by member, it cannot.
    /// </para>
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        if (!Contributes(in position))
        {
            return double.PositiveInfinity;
        }

        var mirrored = Reflect(in position);
        return _inner.SignedDistanceToDiscontinuity(in mirrored);
    }

    /// <inheritdoc/>
    public int DiscontinuityMembers => Math.Max(1, _inner.DiscontinuityMembers);

    /// <inheritdoc/>
    public double MemberDistanceToDiscontinuity(int member, in Vec3 position)
    {
        if (!Contributes(in position))
        {
            var onPlane = OnPlane(in position);
            return Math.CopySign(double.PositiveInfinity, _inner.MemberDistanceToDiscontinuity(member, in onPlane));
        }

        var mirrored = Reflect(in position);
        return _inner.MemberDistanceToDiscontinuity(member, in mirrored);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The declared half's members seen through the mirror where the image answers
    /// (<see cref="Contributes"/>). Where it does not, each is an infinity - no surface there - with
    /// the sign its measure has on the plane, straight across from the point.
    /// </para>
    /// <para>
    /// Read through the mirror on both sides, as they were, a member changed sign in the declared
    /// half wherever a surface of the declared field lay on the image's side, and an ion in the
    /// declared half landed on the mirror of a surface the declared half does not have. Plus infinity
    /// alone, as a bounded element's inner members are, changes sign at the plane wherever the
    /// declared measure is negative there - a solved domain's interior, at a mirror edge - which the
    /// integrator reads by sign as a crossing of a plane where nothing jumps. With the plane's own
    /// sign the infinity agrees with the image across the plane, so the plane is crossed only where
    /// the declared half carries it as a member of its own, and an image surface just past the plane
    /// is still a change of sign from the plane's side.
    /// </para>
    /// </remarks>
    public void MemberDistancesToDiscontinuity(in Vec3 position, Span<double> distances)
    {
        if (!Contributes(in position))
        {
            var onPlane = OnPlane(in position);
            _inner.MemberDistancesToDiscontinuity(in onPlane, distances);

            for (var k = 0; k < distances.Length; k++)
            {
                distances[k] = double.IsNaN(distances[k]) ? double.NaN : Math.CopySign(double.PositiveInfinity, distances[k]);
            }

            return;
        }

        var mirrored = Reflect(in position);
        _inner.MemberDistancesToDiscontinuity(in mirrored, distances);
    }

    // The point on the plane straight across from a position: its own image.
    private Vec3 OnPlane(in Vec3 position) => new(_reflection.PlaneX, position.Y, position.Z);

    /// <inheritdoc/>
    public void DiscontinuityMemberGates(Span<int> gates) => _inner.DiscontinuityMemberGates(gates);

    /// <inheritdoc/>
    public double ResolutionLength => _inner.ResolutionLength;
}
