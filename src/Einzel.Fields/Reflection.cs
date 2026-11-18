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
/// Whether the union's field is continuous across the plane - a Neumann (mirror) edge, or a box
/// stopping short of the plane - so the plane is no surface. Decided by the plane edge's own
/// condition alone: a solve's <c>boundaryIsDiscontinuous</c> speaks for its far edges, not for this
/// one, which is interior to the union. False, the safe default,
/// makes the plane a surface the declared half declares over <paramref name="Face"/>, whatever
/// surfaces the half's own field declares: an extra landing where the field happens to be
/// continuous costs a step, where a missed jump costs accuracy.
/// </param>
/// <param name="Face">
/// The part of the plane the solve's box covers, where each half's one sign on its other side is
/// read (<see cref="PlaneFace.At"/>); null for a reflection made by hand, read at each point.
/// </param>
public sealed record Reflection(double PlaneX, bool? ImageAbove, bool EdgeIsContinuous = false, PlaneRegion? Face = null)
{
    /// <summary>The reflection of a solve, or none.</summary>
    /// <param name="planeX">The declared mirror plane, or null where the solve is not reflected.</param>
    /// <param name="minX">The low x edge of the declared half.</param>
    /// <param name="maxX">The high x edge of the declared half.</param>
    /// <returns>The reflection, with its side decided; null where there is no plane.</returns>
    /// <param name="edgeIsContinuous">
    /// Whether the edge on the plane is continuous by construction - a Neumann edge. Decided from the
    /// boundary condition, once, by whoever builds the solve.
    /// </param>
    /// <param name="face">The part of the plane the solve's box covers; null to read each point at its own place.</param>
    public static Reflection? Of(double? planeX, double minX, double maxX, bool edgeIsContinuous = false, PlaneRegion? face = null) =>
        planeX is { } plane ? new Reflection(plane, SideOf(minX, maxX, plane), edgeIsContinuous, face) : null;

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
    /// <exception cref="ArgumentException">
    /// The halves overlap: each answers everywhere, so there is no side where a half answers
    /// nothing and no face to read - and one read just inside would be on the wrong side.
    /// </exception>
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
/// <b>The union declares, apart from the plane, exactly the mirror image of the declared half's
/// own surfaces.</b> The image side may declare a surface only where the declared side declares
/// the mirror one, so a union is as symmetric about its mirror in what it declares as in its field.
/// So a half declares nothing of its own where it answers nothing: it carries one sign over the
/// whole of that side, which changes nowhere and cannot cancel the other half's surfaces there. A
/// surface it declared beyond its edge would be a phantom the integrator stops at; one closing the
/// plane with the image box's faces, for a half that declares no surfaces of its own, made the union
/// declare faces its declared side does not have, and could cancel a neighbouring element's real
/// edge.
/// </para>
/// <para>
/// <b>Where a sign function forces a change with no surface behind it, it is the plane beyond the
/// box.</b> On a grounded plane the declared box, the image's box and the outside meet at the rim,
/// and one sign cannot change across all three boundaries there; the change is put on the plane
/// beyond the box, where the drift is cut, and never on a face of the image box.
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
/// field at zero - a Neumann mirror; <c>boundaryIsDiscontinuous</c> speaks for a solve's far edges
/// and cannot vouch for this one, which is interior to the union. Where it does not,
/// the reflection declares the plane itself, whether or not the half's own field declares any
/// surface there: a volume solve declares none. That is a property of the
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

    // Whether this half declares the plane: the owner, where the field is not continuous across it
    // by construction. Decided once, from the boundary condition, and independent of whether the
    // half's own field declares any surface there - a volume solve declares none.
    private readonly bool _declares;

    // The part of the plane the box covers: where the one sign each half carries on the side it
    // answers nothing is read. Null for a reflection made by hand, read at each point's own place.
    private readonly PlaneRegion? _region;

    internal PlaneFace(IElectrostaticField inner, Reflection reflection, bool ownsPlane)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        if (reflection.Overlaps)
        {
            // Where the halves overlap, each answers everywhere and neither has a side where it
            // answers nothing: there is no face to read, and the point read just inside would be on
            // the wrong side. Refused rather than built and left unused.
            throw new ArgumentException("a plane face exists only for a union; where the halves overlap there is none", nameof(reflection));
        }

        _plane = reflection.PlaneX;
        _declares = ownsPlane && !reflection.EdgeIsContinuous;
        _region = reflection.Face;

        // The half's length scale: its resolution where it has one, else the plane's distance from
        // the origin, else a millimetre - an analytic field at the origin has no scale of its own.
        _length = double.IsFinite(inner.ResolutionLength) && inner.ResolutionLength > 0.0 ? inner.ResolutionLength
            : _plane != 0.0 ? Math.Abs(_plane)
            : 1e-3;

        // In the half's own frame: just inside the declared side.
        var imageSide = reflection.ImageAbove == true ? 1.0 : -1.0;
        _inside = _plane - (imageSide * PastFraction * _length);
    }

    /// <summary>Whether this half declares the plane as a surface beneath a point.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <returns>
    /// True for the owner wherever the edge on the plane is not a mirror: over the box's part of the
    /// plane, where the field jumps, and beyond it, where the one change of sign no surface stands
    /// behind is put (see <see cref="At"/>). A geometric rule, read from the boundary condition and
    /// from no field value.
    /// </returns>
    public bool Declares(double y, double z) => _declares;

    /// <summary>Whether the union's measure changes sign at the plane beneath a point.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <returns>
    /// The same as <see cref="Declares"/>: the plane is the only place the owner's measure changes
    /// sign on the side it answers nothing, so a drift is cut there and nowhere else.
    /// </returns>
    public bool ChangesSignAt(double y, double z) => _declares;

    /// <summary>The measure on the side where the half answers, with the plane added where it declares it.</summary>
    /// <param name="position">The point.</param>
    /// <param name="own">The half's own measure there.</param>
    /// <returns>
    /// Its own measure where it does not declare the plane; otherwise that and the distance to the
    /// plane combined as a sum combines its members (<see cref="DiscontinuityDistance.Combine"/>), so
    /// the sign is the half's own and the magnitude never exceeds how far the plane is.
    /// </returns>
    public double Answering(in Vec3 position, double own) =>
        _declares ? DiscontinuityDistance.Combine(own, Math.Abs(position.X - _plane)) : own;

    /// <summary>The measure at a point on the side where the half answers nothing.</summary>
    /// <param name="position">The point.</param>
    /// <returns>The measure.</returns>
    /// <remarks>
    /// <para>
    /// <b>One sign over the whole side</b>: the half's own just inside the plane at one point of the
    /// box's part of it (<see cref="PlaneRegion.Representative"/>), negated where this half declares
    /// the plane. A member that changes sign nowhere on a side cannot cancel the other member's
    /// surfaces there, and adds none: the union's surfaces on each side are exactly the answering
    /// half's own - the mirror image of each other - which is the rule <see cref="Reflection"/>
    /// states. Read at each point instead, the sign changed along the rim where the box ends and
    /// cancelled the other half's box edge there; closed by the image box's faces instead, the
    /// union declared surfaces the declared side does not, asymmetric about its own mirror.
    /// </para>
    /// <para>
    /// <b>At the plane</b>, the union's sign just past it is the answering half's at the mirror point
    /// times this sign, and just before it the same with the roles changed, so the two sides differ
    /// by the owner's negation: a grounded plane changes sign everywhere on it, a mirror nowhere.
    /// Over the box's part the field jumps there. Beyond it nothing does, and that change of sign is
    /// the one a sign function cannot avoid - the declared box, the image's box and the outside all
    /// meet at the rim - put on the plane, where the drift is cut, rather than on any face the
    /// declared side does not have.
    /// </para>
    /// <para>
    /// <b>The magnitude</b> is the half's own measure at the plane beneath the point where that is not
    /// zero there - unchanged from what it always was, so a half declaring no surfaces is +infinity as
    /// before - and the distance from the plane where it is, or where this half declares the plane.
    /// </para>
    /// </remarks>
    public double At(in Vec3 position)
    {
        var distance = Math.Abs(position.X - _plane);
        var (ry, rz) = _region?.Representative ?? (position.Y, position.Z);

        // The sign the half has just inside the plane: its own where that is a number, positive
        // where it declares nothing, since Answering then takes the plane's distance.
        var just = _inner.SignedDistanceToDiscontinuity(new Vec3(_inside, ry, rz));
        var sign = double.IsFinite(just) && just < 0.0 ? -1.0 : 1.0;

        if (_declares)
        {
            return -sign * distance;
        }

        var here = _inner.SignedDistanceToDiscontinuity(new Vec3(_plane, position.Y, position.Z));
        return Math.CopySign(IsFace(here) ? distance : Math.Abs(here), sign);
    }

    // Whether the half's measure on the plane is zero there: within a tolerance of zero.
    private bool IsFace(double atPlane) => Math.Abs(atPlane) <= FaceTolerance * _length;
}

/// <summary>The part of a mirror plane a reflected solve's box covers.</summary>
/// <remarks>
/// Where the one sign each half carries on its other side is read
/// (<see cref="PlaneFace.At"/>): at <see cref="Representative"/>, the middle of the box's face on
/// the plane, away from every edge of it. A radial region is an axisymmetric solve's disc, whose
/// radii are not negative; the axis is no edge of it. Every bound is checked as it is set, by a
/// <c>with</c> as by the constructor, so no region of negative radius can be made.
/// </remarks>
public sealed record PlaneRegion
{
    private readonly double _minY;
    private readonly bool _radial;

    /// <summary>A region of the plane.</summary>
    /// <param name="minY">The low y bound, or the least radius for a radial region.</param>
    /// <param name="maxY">The high y bound, or the greatest radius.</param>
    /// <param name="minZ">The low z bound.</param>
    /// <param name="maxZ">The high z bound.</param>
    /// <param name="radial">Whether the bounds on y are a radius about the x axis, z unused.</param>
    /// <exception cref="ArgumentOutOfRangeException">A radial region's least radius is negative.</exception>
    public PlaneRegion(double minY, double maxY, double minZ, double maxZ, bool radial = false)
    {
        Check(minY, radial);
        _radial = radial;
        _minY = minY;
        MaxY = maxY;
        MinZ = minZ;
        MaxZ = maxZ;
    }

    /// <summary>The low y bound, or the least radius of a radial region - not negative.</summary>
    public double MinY
    {
        get => _minY;
        init
        {
            Check(value, _radial);
            _minY = value;
        }
    }

    /// <summary>The high y bound, or the greatest radius.</summary>
    public double MaxY { get; init; }

    /// <summary>The low z bound.</summary>
    public double MinZ { get; init; }

    /// <summary>The high z bound.</summary>
    public double MaxZ { get; init; }

    /// <summary>Whether the bounds on y are a radius about the x axis - an axisymmetric solve's disc - with z unused.</summary>
    public bool Radial
    {
        get => _radial;
        init
        {
            Check(_minY, value);
            _radial = value;
        }
    }

    /// <summary>The point the region is read at: the middle of its bounds, on y and on z.</summary>
    /// <remarks>
    /// The middle radius of a radial region, off the axis: an axisymmetric solve's own box edge lies
    /// along the axis, and a sign read there is a sign read on an edge. An unbounded direction reads
    /// at zero.
    /// </remarks>
    public (double Y, double Z) Representative =>
        (Middle(MinY, MaxY), Radial ? 0.0 : Middle(MinZ, MaxZ));

    private static double Middle(double low, double high) =>
        double.IsFinite(low) && double.IsFinite(high) ? 0.5 * (low + high)
            : double.IsFinite(low) ? low
            : double.IsFinite(high) ? high
            : 0.0;

    private static void Check(double minY, bool radial)
    {
        if (radial && !(minY >= 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(minY), minY, "a radial region's least radius is not negative");
        }
    }
}
