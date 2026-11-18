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
public sealed record Reflection(double PlaneX, bool? ImageAbove)
{
    /// <summary>The reflection of a solve, or none.</summary>
    /// <param name="planeX">The declared mirror plane, or null where the solve is not reflected.</param>
    /// <param name="minX">The low x edge of the declared half.</param>
    /// <param name="maxX">The high x edge of the declared half.</param>
    /// <returns>The reflection, with its side decided; null where there is no plane.</returns>
    public static Reflection? Of(double? planeX, double minX, double maxX) =>
        planeX is { } plane ? new Reflection(plane, SideOf(minX, maxX, plane)) : null;

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
    /// <param name="declared">The declared half's conductors.</param>
    /// <returns>Them, as they stand in the sum: gated by the same rule as <see cref="Declared(IElectrostaticField)"/>.</returns>
    public IConductorBounded Declared(IConductorBounded declared) =>
        Overlaps ? declared : new DeclaredMetal(declared, this);

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
