using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A solve reflected through a plane normal to x: where the plane is, and how the declared half
/// and its image meet there.
/// </summary>
/// <param name="PlaneX">The mirror plane.</param>
/// <param name="ImageAbove">
/// The side the image takes, as <see cref="SideOf"/> decides it: true or false
/// for a union, null where the declared half runs past the plane and the two overlap.
/// </param>
/// <remarks>
/// <para>
/// <b>The one statement of a reflection, made once by whoever builds the solve</b> and carried to
/// everything that needs it: the sum of the field (<see cref="Sum"/>), the image's conductor leaf
/// (<see cref="ConductorLeaf.Of"/>), and through <see cref="FieldLeaf"/> to the probe, which reads
/// the plane and the overlap off the leaf instead of out of the model again. The side rule had
/// come to be applied in five places - two builders, the mirror pair, the probe's range and its
/// inline <c>2p - x</c> - and the leaf's image was built with no side at all, so its metal stood
/// where the field's image answered nothing.
/// </para>
/// </remarks>
/// <param name="EdgeIsContinuous">
/// Whether the declared half's edge on the plane keeps the union's field continuous across it by
/// construction - a Neumann (mirror) edge, or a solve that declares no edge surfaces at all - so
/// the plane is no surface. False, the safe default, declares one wherever the half has a face on
/// the plane: an extra landing where the field happens to be continuous costs a step, where a
/// missed jump costs accuracy.
/// </param>
public sealed record Reflection(double PlaneX, bool? ImageAbove, bool EdgeIsContinuous = false)
{
    /// <summary>The reflection of a solve, or none.</summary>
    /// <param name="planeX">The declared mirror plane, or null where the solve is not reflected.</param>
    /// <param name="minX">The low x edge of the declared half.</param>
    /// <param name="maxX">The high x edge of the declared half.</param>
    /// <returns>The reflection, with its side decided; null where there is no plane.</returns>
    /// <param name="edgeIsContinuous">
    /// Whether the edge on the plane is continuous by construction - a Neumann edge, or a solve that
    /// declares no edge surfaces. Decided from the boundary condition, once, by whoever builds the
    /// solve.
    /// </param>
    public static Reflection? Of(double? planeX, double minX, double maxX, bool edgeIsContinuous = false) =>
        planeX is { } plane ? new Reflection(plane, SideOf(minX, maxX, plane), edgeIsContinuous) : null;

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
    /// The one rule for every reflected sum: the solve builders and <c>MirrorPair</c> both reach it through <see cref="Of"/>.
    /// The first version gave a side whatever the box, so a plane inside the box made the sum
    /// one half below the plane and both above it - asymmetric about its own mirror.
    /// </remarks>
    public static bool? SideOf(double minX, double maxX, double planeX) =>
        planeX >= maxX ? true : planeX <= minX ? false : null;

    /// <summary>Whether the declared half runs past the plane and overlaps its image, so the two are summed.</summary>
    public bool Overlaps => ImageAbove is null;

    /// <summary>Whether the image answers at an x coordinate.</summary>
    /// <param name="x">The coordinate, in the declared half's own frame.</param>
    /// <returns>
    /// Strictly on the image's side of the plane, judged on the point and not on its reflection;
    /// everywhere where the halves overlap. The one statement of the rule: the field, the image's
    /// conductors and the probe's bounds all ask it.
    /// </returns>
    public bool ImageAnswersAt(double x) => ImageAbove switch
    {
        true => x > PlaneX,
        false => x < PlaneX,
        null => true,
    };

    /// <summary>Whether the declared half answers at an x coordinate: on its own side and on the plane, or everywhere where the halves overlap.</summary>
    /// <param name="x">The coordinate, in the declared half's own frame.</param>
    /// <returns>True unless the point is strictly on the image's side of a union.</returns>
    public bool DeclaredAnswersAt(double x) => ImageAbove is null || !ImageAnswersAt(x);

    /// <summary>The declared half, its field, potential and conductors answering only where the half does.</summary>
    /// <param name="declared">The declared half.</param>
    /// <returns>The half, as it stands in the sum.</returns>
    /// <remarks>
    /// The image's conductors were gated on the declared side, and the declared half's were not on
    /// the image side: a declared electrode running past the plane still stopped ions there, and
    /// was itemised as a loss, where the image's metal - its reflection of the near side - is what
    /// stands. Its field and potential follow the plane too: gated on the image's side as the
    /// image's are on the declared side, so a solve whose outside potential is not zero sums to
    /// the same value on both sides of its own mirror.
    /// </remarks>
    public IElectrostaticField Declared(IElectrostaticField declared) =>
        Overlaps ? declared : new DeclaredHalf(declared, this);

    /// <summary>The declared half's conductors, answering only where the half does.</summary>
    /// <remarks>
    /// Named apart from <see cref="Declared"/>: as two overloads of one name, a solved field -
    /// which is both a field and bounded by conductors - matched both, and a call with one was
    /// ambiguous (CS0121).
    /// </remarks>
    /// <param name="declared">The declared half's conductors.</param>
    /// <returns>Them, as they stand in the sum: gated by the same rule as <see cref="Declared"/>.</returns>
    public IConductorBounded DeclaredMetal(IConductorBounded declared) =>
        Overlaps ? declared : new GatedMetal(declared, this);

    /// <summary>
    /// How a half reads its discontinuity measure where it contributes nothing: the declared half
    /// on the image's side, which owns the plane, or the image on the declared side, which does not.
    /// </summary>
    /// <param name="inner">The declared half's field, in its own frame.</param>
    /// <param name="ownsPlane">Whether this half declares the plane as a surface where the edge makes it one.</param>
    /// <returns>The measure, made once per half and asked per point.</returns>
    public PlaneFace FaceOf(IElectrostaticField inner, bool ownsPlane) => new(inner, this, ownsPlane);

    /// <summary>An x coordinate seen through the mirror.</summary>
    /// <param name="x">The coordinate.</param>
    /// <returns><c>2p - x</c>.</returns>
    public double ReflectX(double x) => (2.0 * PlaneX) - x;

    /// <summary>The declared half's image, answering where this reflection says it does.</summary>
    /// <param name="declared">The declared half.</param>
    /// <returns>The image.</returns>
    public ReflectedField Image(IElectrostaticField declared) => new(declared, this);

    /// <summary>The declared half and its image, summed.</summary>
    /// <param name="declared">The declared half.</param>
    /// <returns>The whole reflected solve.</returns>
    public IElectrostaticField Sum(IElectrostaticField declared) => new SuperposedField([Declared(declared), Image(declared)]);
}

/// <summary>
/// A half's discontinuity measure where it contributes nothing, and whether it owns the plane.
/// </summary>
/// <remarks>
/// <para>
/// Where a half answers nothing it has no discontinuity of its own, so none of its surfaces is
/// declared there - a surface it declares beyond its edge would otherwise be a phantom the
/// integrator stops at. The one surface that can be there is the plane, and only where the edge
/// makes it one and the half has a face on it. Otherwise the measure is the half's own at the
/// plane, so its sign changes nowhere on this side.
/// </para>
/// <para>
/// <b>Exactly one half owns the plane.</b> A sum multiplies its members' signs
/// (<see cref="DiscontinuityDistance.Combine"/>), so a surface both halves declare at one place
/// cancels: where the declared half's box ends on the plane and the field is not zero there, the
/// union's field jumps from +E to -E across it, both halves changed sign there, and the sum
/// reported no surface at all - Stage 4's paired-surface failure, at the one plane every reflected
/// solve has. Measured on a uniform field reflected at its box's edge: an energy drift of 5.1e-8
/// across the plane, against below 1e-12 landed on. So the declared half changes sign at the plane
/// and the image reads its measure on the declared side with the sign it has just past the plane
/// on its own side - continuous with itself, no surface.
/// </para>
/// <para>
/// <b>Whether the plane is a surface is the edge's, decided once.</b> The union's normal field goes
/// from E to -E across the plane, so it jumps unless the half's edge on the plane holds the normal
/// field at zero - a Neumann mirror - or declares no surfaces at all. That is a property of the
/// boundary condition, known when the solve is built (<see cref="Reflection.EdgeIsContinuous"/>), not
/// of a field value: deciding it from the field at each point read a driven solve's first channel
/// rather than its weighted sum, flipped wherever the normal field crossed zero along the plane,
/// left the answer to round-off, and cost interpolations on every step. A surface declared where
/// the field happens to be continuous costs a landing; a jump missed costs accuracy.
/// </para>
/// </remarks>
public sealed class PlaneFace
{
    // As fractions of the half's own length scale, as the shared-face check takes 1e-9 of the
    // finest spacing: how near zero its measure must be on the plane for the plane to be a face,
    // and how far from the plane its sign is read. Fixed lengths in metres were too tight for a
    // kilometre of geometry and too loose for a micron.
    private const double FaceTolerance = 1e-9;
    private const double PastFraction = 1e-6;

    private readonly IElectrostaticField _inner;
    private readonly double _plane;
    private readonly double _length;
    private readonly double _inside;
    private readonly double _past;

    // Whether this half declares the plane where it has a face there: the owner, on an edge that
    // is not continuous by construction. Decided once, from the boundary condition.
    private readonly bool _declares;

    internal PlaneFace(IElectrostaticField inner, Reflection reflection, bool ownsPlane)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _plane = reflection.PlaneX;
        _declares = ownsPlane && !reflection.EdgeIsContinuous;

        // The half's length scale: its resolution where it has one, else the plane's distance from
        // the origin, else a millimetre - an analytic field at the origin has no scale of its own.
        _length = double.IsFinite(inner.ResolutionLength) && inner.ResolutionLength > 0.0 ? inner.ResolutionLength
            : _plane != 0.0 ? Math.Abs(_plane)
            : 1e-3;

        // In the half's own frame: just inside the declared side, and just past the face on the
        // image's side.
        var imageSide = reflection.ImageAbove == true ? 1.0 : -1.0;
        _inside = _plane - (imageSide * PastFraction * _length);
        _past = _plane + (imageSide * PastFraction * _length);
    }

    /// <summary>Whether this half declares the plane as a surface beneath a point.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <returns>True where it owns the plane, the edge is not continuous by construction, and it has a face there.</returns>
    public bool Declares(double y, double z) =>
        _declares && IsFace(_inner.SignedDistanceToDiscontinuity(new Vec3(_plane, y, z)));

    /// <summary>The measure at a point on the side where the half answers nothing.</summary>
    /// <param name="position">The point.</param>
    /// <returns>The measure.</returns>
    /// <remarks>
    /// Where the half has no face on the plane beneath the point, its own measure at the plane,
    /// held constant across this side. Where it has one, the distance from the plane, signed as the
    /// half is just past its face - changing sign at the plane - where this half declares the
    /// surface, and as it is just inside the declared side - not changing - where it does not. Read
    /// at the point's own place; nothing a query writes is held.
    /// </remarks>
    public double At(in Vec3 position)
    {
        var atPlane = _inner.SignedDistanceToDiscontinuity(new Vec3(_plane, position.Y, position.Z));

        if (!IsFace(atPlane))
        {
            return atPlane;
        }

        var read = new Vec3(_declares ? _past : _inside, position.Y, position.Z);
        return Math.CopySign(Math.Abs(position.X - _plane), _inner.SignedDistanceToDiscontinuity(in read));
    }

    // Whether the half's measure on the plane makes the plane a face: within a tolerance of zero,
    // and a number. One test for the measure and the run, so the two cannot disagree.
    private bool IsFace(double atPlane) => Math.Abs(atPlane) <= FaceTolerance * _length;
}
