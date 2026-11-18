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
/// The part of the plane the solve's box covers, where a non-mirror edge makes the plane a surface;
/// null for the whole plane. Beyond it neither half has field on either side, so the plane is no
/// surface there and a drift across it is not cut.
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
    /// <param name="face">The part of the plane the solve's box covers; null for the whole plane.</param>
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
/// field at zero - a Neumann mirror; <c>boundaryIsDiscontinuous</c> speaks for a solve's far edges
/// and cannot vouch for this one, which is interior to the union. Where it does not,
/// the reflection declares the plane itself, over the part of it the solve's box covers, whether or
/// not the half's own field declares any surface there: a volume solve declares none. Beyond the
/// box neither half has field, so the plane is no surface there. That is a property of the
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
    private readonly bool _imageAbove;

    // Whether this half declares the plane: the owner, where the field is not continuous across it
    // by construction. Decided once, from the boundary condition, and independent of whether the
    // half's own field declares any surface there - a volume solve declares none.
    private readonly bool _declares;

    // Where on the plane it declares it: the part the solve's box covers, or all of it.
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
        _imageAbove = reflection.ImageAbove == true;

        // The half's length scale: its resolution where it has one, else the plane's distance from
        // the origin, else a millimetre - an analytic field at the origin has no scale of its own.
        _length = double.IsFinite(inner.ResolutionLength) && inner.ResolutionLength > 0.0 ? inner.ResolutionLength
            : _plane != 0.0 ? Math.Abs(_plane)
            : 1e-3;

        // In the half's own frame: just inside the declared side.
        var imageSide = _imageAbove ? 1.0 : -1.0;
        _inside = _plane - (imageSide * PastFraction * _length);
    }

    /// <summary>Whether this half declares the plane as a surface beneath a point.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <returns>
    /// True for the owner, where the edge on the plane is not a mirror, over the part of the plane
    /// the solve's box covers (<see cref="Reflection.Face"/>). A geometric rule, read from the box
    /// and the boundary condition and from no field value.
    /// </returns>
    public bool Declares(double y, double z) => _declares && Covers(y, z);

    /// <summary>Whether the union's measure changes sign at the plane beneath a point.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <returns>
    /// True where this half declares the plane (<see cref="Declares"/>), and - for an owner whose
    /// half has a face of its own on the plane - on the plane beyond the box too, the one change of
    /// sign no physical surface stands behind and none can be avoided (see <see cref="At"/>). A drift
    /// is cut wherever this is true, so no change of sign is jumped.
    /// </returns>
    public bool ChangesSignAt(double y, double z)
    {
        if (!_declares)
        {
            return false;
        }

        if (Covers(y, z))
        {
            return true;
        }

        var (qy, qz) = Clamped(y, z);
        return IsFace(_inner.SignedDistanceToDiscontinuity(new Vec3(_plane, qy, qz)));
    }

    /// <summary>The measure on the side where the half answers, with the plane added where it declares it.</summary>
    /// <param name="position">The point.</param>
    /// <param name="own">The half's own measure there.</param>
    /// <returns>
    /// Its own measure where it does not declare the plane; otherwise that and the distance to the
    /// plane combined as a sum combines its members (<see cref="DiscontinuityDistance.Combine"/>), so
    /// the sign is the half's own and the magnitude never exceeds how far the plane is.
    /// </returns>
    public double Answering(in Vec3 position, double own) =>
        Declares(position.Y, position.Z) ? DiscontinuityDistance.Combine(own, Math.Abs(position.X - _plane)) : own;

    /// <summary>The measure at a point on the side where the half answers nothing.</summary>
    /// <param name="position">The point.</param>
    /// <returns>The measure.</returns>
    /// <remarks>
    /// <para>
    /// <b>Two members must never change sign on the same rim, and a sign may change only where a
    /// surface is.</b> A sum multiplies its members' signs, so two changes in one place cancel. The
    /// sign here is therefore read at the point moved just inside the box's part of the plane, so it
    /// is constant across the rim where the box ends: the other half's own box edge there is the
    /// only change, and is not cancelled. Reading it at the point itself - its own measure at the
    /// plane, held - changed sign along the rim exactly where the answering half's box edge does,
    /// and the two cancelled: a reflected cross-section declaring its edges lost both its top edges.
    /// </para>
    /// <para>
    /// <b>The owner, where the half has a face of its own on the plane</b> - a cross-section declaring
    /// its edges: the distance from the plane signed opposite to the half just inside, constant
    /// across the rim, so the image's own box carries the image's edges. The declared box, the
    /// image's box and the outside all meet at the rim, and a sign cannot change across all three
    /// boundaries there, so one change of sign with no surface behind it cannot be avoided: it is the
    /// plane beyond the box, made by the image alone, where neither half has field.
    /// </para>
    /// <para>
    /// <b>The owner, where the half has no face on the plane</b> - a volume solve, which declares no
    /// surfaces of its own: the plane's part is closed with the image box's other faces, where the
    /// field drops to nothing, so every change of sign is at a surface and none is beyond the box.
    /// </para>
    /// <para>
    /// <b>Not the owner</b>: no change of sign at the plane - over the box's part, as before, its own
    /// measure at the plane held, or the distance signed as it is just inside where it has a face
    /// there; beyond it, the sign read just inside the box's part with the magnitude kept.
    /// </para>
    /// </remarks>
    public double At(in Vec3 position)
    {
        var distance = Math.Abs(position.X - _plane);
        var covered = Covers(position.Y, position.Z);
        var (qy, qz) = covered ? (position.Y, position.Z) : Clamped(position.Y, position.Z);

        if (_declares)
        {
            // The sign the owner has just inside - its own where that is a number, positive where it
            // declares nothing, since Answering then takes the plane's distance.
            var just = _inner.SignedDistanceToDiscontinuity(new Vec3(_inside, qy, qz));
            var inside = double.IsFinite(just) && just < 0.0 ? -1.0 : 1.0;

            if (IsFace(_inner.SignedDistanceToDiscontinuity(new Vec3(_plane, qy, qz))))
            {
                return -inside * distance;
            }

            var box = ImageBox(in position);
            return box > 0.0 ? -inside * box : inside * Math.Abs(box);
        }

        if (covered)
        {
            var atPlane = _inner.SignedDistanceToDiscontinuity(new Vec3(_plane, position.Y, position.Z));

            if (!IsFace(atPlane))
            {
                return atPlane;
            }

            return Math.CopySign(distance, _inner.SignedDistanceToDiscontinuity(new Vec3(_inside, position.Y, position.Z)));
        }

        // Beyond the box's part: the sign read just inside it, the magnitude this point's own.
        var nearest = _inner.SignedDistanceToDiscontinuity(new Vec3(_plane, qy, qz));
        var sign = IsFace(nearest) ? _inner.SignedDistanceToDiscontinuity(new Vec3(_inside, qy, qz)) : nearest;
        var here = _inner.SignedDistanceToDiscontinuity(new Vec3(_plane, position.Y, position.Z));
        return Math.CopySign(IsFace(here) ? distance : Math.Abs(here), sign);
    }

    // Whether the half's measure on the plane makes the plane a face of its own: within a tolerance
    // of zero.
    private bool IsFace(double atPlane) => Math.Abs(atPlane) <= FaceTolerance * _length;

    private bool Covers(double y, double z) => _region?.Covers(y, z) ?? true;

    // A point of the plane moved just inside the box's part, by the distance the sign is read past
    // the plane - well inside the rounding the region was widened by.
    private (double Y, double Z) Clamped(double y, double z) =>
        _region is { } region ? region.Clamp(y, z, PastFraction * _length) : (y, z);

    // Signed distance to the image's box - the plane's part, between the plane and the declared
    // box's far edge reflected - positive inside: what closes the plane's part for a half with no
    // surfaces of its own.
    private double ImageBox(in Vec3 position)
    {
        var far = _region?.FarX is { } declaredFar ? (2.0 * _plane) - declaredFar : (_imageAbove ? double.PositiveInfinity : double.NegativeInfinity);
        var low = Math.Min(_plane, far);
        var high = Math.Max(_plane, far);

        var across = Math.Min(position.X - low, high - position.X);
        var (inY, inZ) = _region is { } region ? region.Depth(position.Y, position.Z) : (double.PositiveInfinity, double.PositiveInfinity);
        var inside = Math.Min(across, Math.Min(inY, inZ));

        if (inside >= 0.0)
        {
            return inside;
        }

        static double Out(double depth) => depth < 0.0 ? -depth : 0.0;
        return -Math.Sqrt((Out(across) * Out(across)) + (Out(inY) * Out(inY)) + (Out(inZ) * Out(inZ)));
    }
}

/// <summary>The part of a mirror plane a reflected solve's box covers.</summary>
/// <param name="MinY">The low y bound, or the least radius where <paramref name="Radial"/>.</param>
/// <param name="MaxY">The high y bound, or the greatest radius where <paramref name="Radial"/>.</param>
/// <param name="MinZ">The low z bound.</param>
/// <param name="MaxZ">The high z bound.</param>
/// <param name="Radial">
/// Whether the bounds on y are a radius about the x axis - an axisymmetric solve, which covers the
/// disc its half-plane sweeps - with z unused. A radius is not negative: a radial region with a
/// negative least radius is refused.
/// </param>
/// <param name="FarX">
/// The declared box's edge away from the plane, which reflected bounds the image's box; null where
/// the box is unbounded that way.
/// </param>
/// <remarks>
/// Inclusive: a point on the box's edge is covered. Builders widen the bounds by a rounding (a
/// billionth of the cell) so an edge meant to lie on the box is on it.
/// </remarks>
public sealed record PlaneRegion(double MinY, double MaxY, double MinZ, double MaxZ, bool Radial = false, double? FarX = null)
{
    /// <summary>The radial bounds are radii, so the least is not negative.</summary>
    public double MinY { get; } = Radial && MinY < 0.0
        ? throw new ArgumentOutOfRangeException(nameof(MinY), MinY, "a radial region's least radius is not negative")
        : MinY;

    /// <summary>Whether the region covers a point of the plane.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <returns>True inside or on the bounds.</returns>
    public bool Covers(double y, double z)
    {
        if (Radial)
        {
            var r = Math.Sqrt((y * y) + (z * z));
            return r >= MinY && r <= MaxY;
        }

        return y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;
    }

    /// <summary>A point of the plane moved into the region, a margin inside its bounds.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <param name="margin">How far inside the bounds; less where the region is thinner than twice it.</param>
    /// <returns>The nearest such point.</returns>
    public (double Y, double Z) Clamp(double y, double z, double margin)
    {
        static double Into(double v, double low, double high, double margin)
        {
            if (!double.IsFinite(low) && !double.IsFinite(high))
            {
                return v;
            }

            var m = double.IsFinite(low) && double.IsFinite(high) ? Math.Min(margin, 0.5 * (high - low)) : margin;
            return Math.Clamp(v, double.IsFinite(low) ? low + m : double.NegativeInfinity, double.IsFinite(high) ? high - m : double.PositiveInfinity);
        }

        if (Radial)
        {
            var r = Math.Sqrt((y * y) + (z * z));
            var to = Into(r, MinY, MaxY, margin);
            return r > 0.0 ? (y * to / r, z * to / r) : (to, 0.0);
        }

        return (Into(y, MinY, MaxY, margin), Into(z, MinZ, MaxZ, margin));
    }

    /// <summary>How far inside the region's bounds a point lies across y and across z; negative outside.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="z">The point's z.</param>
    /// <returns>The two depths; infinite along an unbounded direction, and z's infinite for a radial region.</returns>
    public (double Y, double Z) Depth(double y, double z)
    {
        if (Radial)
        {
            var r = Math.Sqrt((y * y) + (z * z));
            return (Math.Min(r - MinY, MaxY - r), double.PositiveInfinity);
        }

        return (Math.Min(y - MinY, MaxY - y), Math.Min(z - MinZ, MaxZ - z));
    }
}
