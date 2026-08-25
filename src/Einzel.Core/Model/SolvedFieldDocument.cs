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

    /// <summary>
    /// Signed distance to this electrode's surface: negative inside the
    /// conductor, positive outside, zero on it.
    /// </summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <returns>
    /// The signed distance, in metres, or positive infinity for a shape that has
    /// no sub-cell surface to locate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// What lets the solver place a boundary between nodes rather than snapping it
    /// to the nearest one. A rasterised boundary moves in whole cells, which makes
    /// the discrete operator a staircase function of electrode position — fatal
    /// for shape derivatives, and the reason the FLD-1 spike failed. With a signed
    /// distance the crossing point on each grid segment is known continuously, and
    /// the operator moves with it.
    /// </para>
    /// <para>
    /// Edge profiles return infinity: they lie along a domain edge, exactly on
    /// grid lines, so there is no sub-cell position for them to occupy.
    /// </para>
    /// </remarks>
    public double SignedDistance(double x, double y)
    {
        switch (Shape)
        {
            case ElectrodeShape.Disc:
            {
                var dx = x - CentreX;
                var dy = y - CentreY;
                return Math.Sqrt((dx * dx) + (dy * dy)) - Radius;
            }

            case ElectrodeShape.Rectangle:
            {
                // Distance to an axis-aligned box: outside is the length of the
                // positive part, inside is the largest negative coordinate.
                var dx = Math.Max(MinX - x, x - MaxX);
                var dy = Math.Max(MinY - y, y - MaxY);

                if (dx <= 0.0 && dy <= 0.0)
                {
                    return Math.Max(dx, dy);
                }

                var ox = Math.Max(dx, 0.0);
                var oy = Math.Max(dy, 0.0);
                return Math.Sqrt((ox * ox) + (oy * oy));
            }

            default:
                return double.PositiveInfinity;
        }
    }

    /// <summary>Whether a point lies within this electrode's conductor.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <returns><see langword="true"/> when inside or on the surface.</returns>
    public bool Contains(double x, double y) => SignedDistance(x, y) <= 0.0;

    /// <summary>
    /// Where an axis-aligned segment first enters this electrode's conductor, as a
    /// fraction of the segment.
    /// </summary>
    /// <param name="fromX">Segment start x, in metres.</param>
    /// <param name="fromY">Segment start y, in metres.</param>
    /// <param name="toX">Segment end x, in metres.</param>
    /// <param name="toY">Segment end y, in metres.</param>
    /// <returns>
    /// The fraction along the segment at which the conductor is first met, or null
    /// when the segment misses it entirely.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Entry rather than crossing, and closed form rather than bisection, for one
    /// reason each. Entry, because an electrode thinner than a cell lies wholly
    /// between two nodes: neither end of the segment is inside it, so a test that
    /// asks whether the endpoints straddle the surface reports nothing and the
    /// electrode disappears. That is not an exotic case — it is every coarse level
    /// of a multigrid hierarchy, which is where the geometry used to dissolve.
    /// </para>
    /// <para>
    /// Closed form, because bisection on the signed distance has the same blind
    /// spot at a different scale: it can only find a crossing it already knows is
    /// bracketed, and sampling to find the bracket would miss features smaller
    /// than the sample interval. The algebra below is exact for any segment and
    /// any thickness.
    /// </para>
    /// </remarks>
    public double? FirstEntry(double fromX, double fromY, double toX, double toY)
    {
        // The interval of the segment parameter over which each shape is entered.
        double low;
        double high;

        switch (Shape)
        {
            case ElectrodeShape.Rectangle:
            {
                if (!Slab(fromX, toX, MinX, MaxX, out var xLow, out var xHigh)
                    || !Slab(fromY, toY, MinY, MaxY, out var yLow, out var yHigh))
                {
                    return null;
                }

                low = Math.Max(xLow, yLow);
                high = Math.Min(xHigh, yHigh);
                break;
            }

            case ElectrodeShape.Disc:
            {
                var dx = toX - fromX;
                var dy = toY - fromY;
                var px = fromX - CentreX;
                var py = fromY - CentreY;

                var a = (dx * dx) + (dy * dy);
                var b = 2.0 * ((dx * px) + (dy * py));
                var c = (px * px) + (py * py) - (Radius * Radius);

                if (a == 0.0)
                {
                    return null;
                }

                var discriminant = (b * b) - (4.0 * a * c);

                if (discriminant < 0.0)
                {
                    return null;
                }

                var root = Math.Sqrt(discriminant);
                low = (-b - root) / (2.0 * a);
                high = (-b + root) / (2.0 * a);
                break;
            }

            default:
                // An edge profile lies along a domain edge, on grid lines already.
                return null;
        }

        if (low > high || high < 0.0 || low > 1.0)
        {
            return null;
        }

        return Math.Max(low, 0.0);
    }

    /// <summary>The segment parameters over which one coordinate lies within a slab.</summary>
    /// <remarks>
    /// A segment along one axis holds the other coordinate constant, so that
    /// coordinate's slab test has no parameter interval at all: it either admits
    /// the whole line or none of it. Returning an unbounded interval for the
    /// constant case lets the caller intersect the two axes uniformly.
    /// </remarks>
    private static bool Slab(double from, double to, double minimum, double maximum, out double low, out double high)
    {
        var delta = to - from;

        if (delta == 0.0)
        {
            low = double.NegativeInfinity;
            high = double.PositiveInfinity;
            return from >= minimum && from <= maximum;
        }

        var a = (minimum - from) / delta;
        var b = (maximum - from) / delta;

        low = Math.Min(a, b);
        high = Math.Max(a, b);
        return true;
    }

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

    /// <summary>
    /// What the plane is a cross-section of: <c>translational</c> or
    /// <c>cylindrical</c>. Translational when omitted.
    /// </summary>
    /// <remarks>
    /// SYM-1. Cylindrical makes x the axis of rotation and y the radius, so the
    /// domain must lie at y greater than or equal to zero. It changes the operator,
    /// not the presentation: a field solved with the wrong one converges contentedly
    /// to the wrong answer.
    /// </remarks>
    public string? Symmetry { get; init; }

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

    /// <summary>What the plane is a cross-section of (SYM-1).</summary>
    public SolveSymmetry Symmetry { get; init; }

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
