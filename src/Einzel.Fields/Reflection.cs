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
/// <para>
/// <b>Apart from the plane, a union declares exactly the mirror image of the declared half's own
/// surfaces.</b> Each half's discontinuity members are its own measure, on both sides of the plane:
/// the declared half's as it is, the image's that measure seen through the mirror. Neither declares
/// anything the other does not mirror, and nothing is read at a representative point or held across
/// a side - every such scheme either cancelled a surface at the rim or declared one the declared
/// side does not have.
/// </para>
/// <para>
/// <b>The plane is a member of its own</b>, carried by the declared half wherever the edge on the
/// plane is not a mirror (<see cref="DeclaresPlane"/>): the signed distance from the plane, which
/// changes sign at every point of it. The integrator tracks members separately
/// (<see cref="IElectrostaticField.DiscontinuityMembers"/>), so a surface lying on the plane - each
/// half's own box face there - and the plane itself are one landing, never two that cancel. Over the
/// box the field jumps there; beyond it nothing does, and the landing there is a cost only: one
/// sign function cannot bound a patch of the plane without another surface closing it, and the
/// plane continued is the closing that declares nothing the mirror does not.
/// </para>
/// </remarks>
/// <param name="EdgeIsContinuous">
/// Whether the union's field is continuous across the plane - a Neumann (mirror) edge, or a box
/// stopping short of the plane - so the plane is no surface. Decided by the plane edge's own
/// condition alone: a solve's <c>boundaryIsDiscontinuous</c> speaks for its far edges, not for this
/// one, which is interior to the union. False, the safe default, makes the plane a surface the
/// declared half carries, whatever surfaces the half's own field declares: an extra landing where
/// the field happens to be continuous costs a step, where a missed jump costs accuracy.
/// </param>
public sealed record Reflection(double PlaneX, bool? ImageAbove, bool EdgeIsContinuous = false)
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

    /// <summary>Whether the plane is a surface: a union whose edge on the plane is not continuous by construction.</summary>
    /// <remarks>
    /// One property, read from the boundary condition and from no field value. Where it is true the
    /// declared half carries the plane as a discontinuity member of its own
    /// (<see cref="PlaneMeasure"/>), and a field-free run is cut where it crosses the plane.
    /// </remarks>
    public bool DeclaresPlane => !Overlaps && !EdgeIsContinuous;

    /// <summary>The plane as a discontinuity measure: the signed distance from it, positive on the declared side.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <returns>The distance, in metres.</returns>
    public double PlaneMeasure(double x) => ImageAbove == true ? PlaneX - x : x - PlaneX;

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
