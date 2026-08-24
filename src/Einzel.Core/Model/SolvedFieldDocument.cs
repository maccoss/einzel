namespace Einzel.Core.Model;

/// <summary>Which side of a solve domain a condition or an electrode refers to.</summary>
public enum GridEdge
{
    /// <summary>The x-minimum edge.</summary>
    Left,

    /// <summary>The x-maximum edge.</summary>
    Right,

    /// <summary>The y-minimum edge.</summary>
    Bottom,

    /// <summary>The y-maximum edge.</summary>
    Top,
}

/// <summary>How the solver treats an edge of the domain.</summary>
public enum BoundaryKind
{
    /// <summary>The edge holds whatever potential the electrodes put there.</summary>
    Dirichlet,

    /// <summary>Zero normal derivative: a symmetry plane, or a wall the field is parallel to.</summary>
    Neumann,
}

/// <summary>The shapes an electrode may take in a two-dimensional solve.</summary>
/// <remarks>
/// <para>
/// Three primitives, chosen for coverage rather than convenience. A rectangle
/// gives plates, apertures, and any stripe of a printed board. A disc gives round
/// rods, which is what a quadrupole, hexapole, or octopole cross-section is. An
/// edge profile gives a potential that varies along a boundary, which is how a
/// printed-circuit mirror applies its ramp.
/// </para>
/// <para>
/// LIB-1's test is whether a new device needs a change below Einzel.Library. A
/// mirror and a quadrupole differ here only in which of these they use and where
/// they put them, which is the point.
/// </para>
/// </remarks>
public enum ElectrodeShape
{
    /// <summary>An axis-aligned block held at one potential.</summary>
    Rectangle,

    /// <summary>A filled circle held at one potential. A rod, in cross-section.</summary>
    Disc,

    /// <summary>
    /// A domain edge whose potential varies along it, given as a piecewise-linear
    /// profile.
    /// </summary>
    EdgeProfile,
}

/// <summary>One point of a piecewise-linear potential profile along an edge.</summary>
/// <param name="At">Position along the edge.</param>
/// <param name="Potential">Potential held there.</param>
public sealed record ProfilePointDocument(QuantityValue? At, QuantityValue? Potential);

/// <summary>An electrode, as it appears in a model document.</summary>
public sealed record ElectrodeDocument
{
    /// <summary>A name, used in reporting and as the basis-field label.</summary>
    public string? Name { get; init; }

    /// <summary>One of <c>rectangle</c>, <c>disc</c>, or <c>edgeProfile</c>.</summary>
    public string? Shape { get; init; }

    /// <summary>Rectangle: lower x bound.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Rectangle: lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Rectangle: upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Rectangle: upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Disc: centre x.</summary>
    public QuantityValue? CentreX { get; init; }

    /// <summary>Disc: centre y.</summary>
    public QuantityValue? CentreY { get; init; }

    /// <summary>Disc: radius.</summary>
    public QuantityValue? Radius { get; init; }

    /// <summary>Edge profile: which edge, one of <c>left</c>, <c>right</c>, <c>bottom</c>, <c>top</c>.</summary>
    public string? Edge { get; init; }

    /// <summary>Edge profile: the piecewise-linear potential along the edge.</summary>
    public IReadOnlyList<ProfilePointDocument>? Profile { get; init; }

    /// <summary>Rectangle and disc: the potential held.</summary>
    public QuantityValue? Potential { get; init; }
}

/// <summary>An electrode, validated and reduced to SI.</summary>
public sealed record CompiledElectrode
{
    /// <summary>The electrode's name.</summary>
    public required string Name { get; init; }

    /// <summary>Which primitive this is.</summary>
    public required ElectrodeShape Shape { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MinX { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MinY { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MaxX { get; init; }

    /// <summary>Rectangle bounds, in metres.</summary>
    public double MaxY { get; init; }

    /// <summary>Disc centre, in metres.</summary>
    public double CentreX { get; init; }

    /// <summary>Disc centre, in metres.</summary>
    public double CentreY { get; init; }

    /// <summary>Disc radius, in metres.</summary>
    public double Radius { get; init; }

    /// <summary>Edge profile: which edge.</summary>
    public GridEdge Edge { get; init; }

    /// <summary>Edge profile: position and potential pairs, in SI, sorted by position.</summary>
    public IReadOnlyList<(double At, double Potential)> Profile { get; init; } = [];

    /// <summary>Rectangle and disc: the potential held, in volts.</summary>
    public double Potential { get; init; }

    /// <summary>The potential this electrode applies at a position along its edge.</summary>
    /// <param name="along">Position along the edge, in metres.</param>
    /// <returns>The potential, in volts, by linear interpolation between profile points.</returns>
    public double ProfileAt(double along)
    {
        if (Profile.Count == 0)
        {
            return Potential;
        }

        if (along <= Profile[0].At)
        {
            return Profile[0].Potential;
        }

        for (var k = 1; k < Profile.Count; k++)
        {
            if (along <= Profile[k].At)
            {
                var span = Profile[k].At - Profile[k - 1].At;
                var t = span > 0.0 ? (along - Profile[k - 1].At) / span : 0.0;
                return Profile[k - 1].Potential + (t * (Profile[k].Potential - Profile[k - 1].Potential));
            }
        }

        return Profile[^1].Potential;
    }
}

/// <summary>A two-dimensional solved field, as it appears in a model document.</summary>
/// <remarks>
/// The field element that makes a device a document rather than a class. It
/// carries the solve domain, the resolution, the edge conditions, and the
/// electrodes — everything the solver in Einzel.Fields needs, and nothing about
/// what the arrangement is called.
/// </remarks>
public sealed record SolvedFieldDocument
{
    /// <summary>Lower x bound of the solve domain.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Node spacing. Rounded to make the interval count a power of two.</summary>
    public QuantityValue? CellSize { get; init; }

    /// <summary>Condition on the x-minimum edge. Dirichlet by default.</summary>
    public string? LeftEdge { get; init; }

    /// <summary>Condition on the x-maximum edge.</summary>
    public string? RightEdge { get; init; }

    /// <summary>Condition on the y-minimum edge.</summary>
    public string? BottomEdge { get; init; }

    /// <summary>Condition on the y-maximum edge.</summary>
    public string? TopEdge { get; init; }

    /// <summary>The electrodes.</summary>
    public IReadOnlyList<ElectrodeDocument>? Electrodes { get; init; }

    /// <summary>
    /// Whether the field genuinely jumps at the domain edge. False where the
    /// domain was drawn wide enough that the field has decayed there.
    /// </summary>
    public bool BoundaryIsDiscontinuous { get; init; } = true;

    /// <summary>Relative residual the solve must reach.</summary>
    public double Tolerance { get; init; } = 1e-12;

    /// <summary>
    /// Reflect the solved field through a plane normal to x, at this position, and
    /// superpose it with the original.
    /// </summary>
    /// <remarks>
    /// A mirror pair is one mirror and its reflection, and expressing that here
    /// rather than in code means both halves are the same solve by construction.
    /// Omitted for a geometry that is not reflected.
    /// </remarks>
    public QuantityValue? ReflectAboutX { get; init; }
}

/// <summary>A two-dimensional solved field, validated and reduced to SI.</summary>
public sealed record CompiledSolvedField
{
    /// <summary>Solve domain, in metres.</summary>
    public required double MinX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MinY { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxY { get; init; }

    /// <summary>Node spacing, in metres.</summary>
    public required double CellSize { get; init; }

    /// <summary>Condition on the x-minimum edge.</summary>
    public BoundaryKind LeftEdge { get; init; }

    /// <summary>Condition on the x-maximum edge.</summary>
    public BoundaryKind RightEdge { get; init; }

    /// <summary>Condition on the y-minimum edge.</summary>
    public BoundaryKind BottomEdge { get; init; }

    /// <summary>Condition on the y-maximum edge.</summary>
    public BoundaryKind TopEdge { get; init; }

    /// <summary>The electrodes.</summary>
    public required IReadOnlyList<CompiledElectrode> Electrodes { get; init; }

    /// <summary>Whether the domain edge is a genuine field discontinuity.</summary>
    public bool BoundaryIsDiscontinuous { get; init; }

    /// <summary>Relative residual for the solve.</summary>
    public double Tolerance { get; init; }

    /// <summary>Reflection plane, in metres, or null when the field is not reflected.</summary>
    public double? ReflectAboutX { get; init; }
}
