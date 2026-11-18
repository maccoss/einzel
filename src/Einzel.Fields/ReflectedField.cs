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
/// where the declared half has no edge on the plane - it runs past it, or fills all space - so
/// the two genuinely overlap and the image answers everywhere, the plane included. See
/// <see cref="Contributes"/>.
/// </param>
public sealed class ReflectedField(IElectrostaticField inner, double planeX, bool? imageAbove = null)
    : IElectrostaticField, IConductorBounded
{
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
        inner is IConductorBounded bounded && Contributes(in position)
            ? bounded.SignedDistanceToConductor(Reflect(in position))
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        inner is IConductorBounded bounded && Contributes(in position) ? bounded.ConductorAt(Reflect(in position)) : null;

    /// <inheritdoc/>
    public double NearestConductor(in Vec3 position, out string? name)
    {
        name = null;

        return inner is IConductorBounded bounded && Contributes(in position)
            ? bounded.NearestConductor(Reflect(in position), out name)
            : double.PositiveInfinity;
    }

    private readonly IElectrostaticField _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private Vec3 Reflect(in Vec3 position) => new((2.0 * planeX) - position.X, position.Y, position.Z);

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
    public bool Contributes(in Vec3 position) => imageAbove switch
    {
        true => position.X > planeX,
        false => position.X < planeX,
        null => true,
    };

    /// <summary>How a declared half and its image meet at the plane: the side the image takes, or none.</summary>
    /// <param name="minX">The low x edge of the declared half.</param>
    /// <param name="maxX">The high x edge of the declared half.</param>
    /// <param name="planeX">The mirror plane.</param>
    /// <returns>
    /// True where the declared half ends at or below the plane, so the image is above and the two
    /// are a union; false where it starts at or above it; null where the plane lies strictly
    /// inside the declared half, so the half runs past the plane, overlaps its image, and the two
    /// are summed everywhere.
    /// </returns>
    /// <remarks>
    /// The one rule for every reflected sum: the solve builders and <c>MirrorPair</c> both ask it.
    /// The first version gave a side whatever the box, so a plane inside the box made the sum
    /// one half below the plane and both above it - asymmetric about its own mirror.
    /// </remarks>
    public static bool? ImageSide(double minX, double maxX, double planeX) =>
        planeX >= maxX ? true : planeX <= minX ? false : null;

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
    /// Where the image answers nothing (<see cref="Contributes"/>) it is field-free until the ray
    /// reaches the plane, where it may start to answer: the distance to the plane along a ray
    /// heading for the image's side, unbounded along one that is not. The inner field's own
    /// run, asked at the reflected point, says nothing there - a half that is free beyond its own
    /// edge claimed the image free straight through the plane and into its field.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        if (!Contributes(in position))
        {
            var towards = imageAbove == true ? direction.X : -direction.X;

            return towards > 0.0 ? Math.Abs(planeX - position.X) / towards : double.PositiveInfinity;
        }

        var mirrored = Reflect(in position);
        var mirroredDirection = new Vec3(-direction.X, direction.Y, direction.Z);
        return _inner.FieldFreeRunLength(in mirrored, in mirroredDirection);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Where the image answers nothing it has no discontinuity of its own, so none of the inner
    /// field's surfaces seen through the mirror is declared there - a surface the inner field
    /// declares beyond its edge would otherwise land on the declared side as a phantom the
    /// integrator stops at. The one surface that can be there is the plane, where the image
    /// starts to answer, and only where the image's field jumps there: where the inner field has a
    /// face on the plane, declared as the distance from the plane with the sign the inner field
    /// gives beyond that face. Otherwise the measure is the image's own at the plane, constant
    /// across this side, so its sign changes nowhere the field is continuous.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        if (!Contributes(in position))
        {
            var onPlane = new Vec3(planeX, position.Y, position.Z);

            var atPlane = _inner.SignedDistanceToDiscontinuity(in onPlane);
            var length = Length;

            if (Math.Abs(atPlane) > FaceTolerance * length)
            {
                // No face on the plane, so nothing jumps there: the image's own measure where it
                // starts to answer, held constant across this side - continuous with it at the
                // plane and changing sign nowhere here. Infinity here, beside a finite value
                // beyond the plane, was a change of sign at a plane where nothing is
                // discontinuous - a millimetre from a declared box's real edge when the box
                // stops short of the plane, the paired-surface layout that once cost an ion
                // 2.6e-4 of its energy.
                return atPlane;
            }

            // The sign the inner field gives just past its face on the plane - the side this point
            // mirrors to - so the change of sign is at the plane whichever way round the inner
            // field counts, and no further surface of the inner field reaches this side.
            var past = new Vec3(planeX + ((imageAbove == true ? 1.0 : -1.0) * PastFraction * length), position.Y, position.Z);
            return Math.CopySign(Math.Abs(position.X - planeX), _inner.SignedDistanceToDiscontinuity(in past));
        }

        var mirrored = Reflect(in position);
        return _inner.SignedDistanceToDiscontinuity(in mirrored);
    }

    // Both as fractions of the inner field's own length scale, as the shared-face check takes
    // 1e-9 of the finest spacing: how near zero its discontinuity measure must be on the plane for
    // the plane to be a face, and how far past the plane its sign is read. Fixed lengths in metres
    // were too tight for a kilometre of geometry and too loose for a micron.
    private const double FaceTolerance = 1e-9;
    private const double PastFraction = 1e-6;

    // The inner field's length scale: its resolution where it has one, else the plane's own
    // distance from the origin, else a millimetre - an analytic field at the origin has no scale
    // of its own to take.
    private double Length =>
        double.IsFinite(_inner.ResolutionLength) && _inner.ResolutionLength > 0.0 ? _inner.ResolutionLength
        : planeX != 0.0 ? Math.Abs(planeX)
        : 1e-3;

    /// <inheritdoc/>
    public double ResolutionLength => _inner.ResolutionLength;
}
