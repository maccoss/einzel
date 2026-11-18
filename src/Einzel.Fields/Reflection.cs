namespace Einzel.Fields;

/// <summary>
/// A solve reflected through a plane normal to x: where the plane is, and how the declared half
/// and its image meet there.
/// </summary>
/// <param name="PlaneX">The mirror plane.</param>
/// <param name="ImageAbove">
/// The side the image takes, as <see cref="ReflectedField.ImageSide"/> decides it: true or false
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
        planeX is { } plane ? new Reflection(plane, ReflectedField.ImageSide(minX, maxX, plane)) : null;

    /// <summary>Whether the declared half runs past the plane and overlaps its image, so the two are summed.</summary>
    public bool Overlaps => ImageAbove is null;

    /// <summary>An x coordinate seen through the mirror.</summary>
    /// <param name="x">The coordinate.</param>
    /// <returns><c>2p - x</c>.</returns>
    public double ReflectX(double x) => (2.0 * PlaneX) - x;

    /// <summary>The declared half's image, answering where this reflection says it does.</summary>
    /// <param name="declared">The declared half.</param>
    /// <returns>The image.</returns>
    public ReflectedField Image(IElectrostaticField declared) => new(declared, PlaneX, ImageAbove);

    /// <summary>The declared half and its image, summed.</summary>
    /// <param name="declared">The declared half.</param>
    /// <returns>The whole reflected solve.</returns>
    public IElectrostaticField Sum(IElectrostaticField declared) => new SuperposedField([declared, Image(declared)]);
}
