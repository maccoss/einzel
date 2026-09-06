using Einzel.Core.Errors;

namespace Einzel.Core.Model;

/// <summary>
/// Refuses two conductors that occupy the same space and disagree about what they
/// hold.
/// </summary>
/// <remarks>
/// <para>
/// A Dirichlet mask is built by writing each electrode's nodes in turn, so where
/// two overlap the last one written wins. Where both hold the same potential and
/// the same drive that is harmless and often deliberate - a shape assembled from
/// overlapping primitives is a legitimate way to build a fillet or a shoulder. Where
/// they <em>disagree</em> it is ill-posed: the region is simultaneously at +V and
/// -V, the solve silently picks one, and the field it returns is the field of a
/// geometry nobody described.
/// </para>
/// <para>
/// Found by building a multipole guide. Denison's rod ratio of 1.1468 is the
/// classical value for a <em>quadrupole</em>, and applying it to six or eight rods
/// puts them through one another - the rods at 1.1468 need a centre circle 9.17 mm
/// across and a hexapole gives them 8.59 mm. That solved, converged in eight cycles,
/// and produced an acceptance measurement that was really a measurement of rods
/// closing in on the axis.
/// </para>
/// <para>
/// Only the shape pairs that can be tested exactly are tested: disc against disc,
/// rectangle against rectangle, and disc against rectangle. An edge profile lives on
/// the domain boundary and is skipped, which is a stated gap rather than an
/// oversight - a boundary profile and an interior electrode that touch are a
/// different question from two interior conductors intersecting.
/// </para>
/// </remarks>
public static class ElectrodeOverlap
{
    /// <summary>Checks a solved 2D geometry for contradictory overlaps.</summary>
    /// <param name="electrodes">The compiled electrodes, in declaration order.</param>
    /// <param name="path">JSON Pointer to the solve block, for the error object.</param>
    /// <param name="errors">Where a violation is recorded.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static void Check(
        IReadOnlyList<CompiledElectrode> electrodes, string path, List<EinzelError> errors)
    {
        ArgumentNullException.ThrowIfNull(electrodes);
        ArgumentNullException.ThrowIfNull(errors);

        for (var i = 0; i < electrodes.Count; i++)
        {
            for (var j = i + 1; j < electrodes.Count; j++)
            {
                var a = electrodes[i];
                var b = electrodes[j];

                if (Agrees(a, b) || !Intersects(a, b))
                {
                    continue;
                }

                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/electrodes",
                    Constraint =
                        $"'{a.Name}' and '{b.Name}' occupy the same space and hold different "
                        + $"excitations: {Describe(a)} against {Describe(b)}",
                    Suggestion =
                        "two conductors cannot be in one place at two potentials, and a mask built "
                        + "from them keeps whichever was written last - so the solve would return "
                        + "the field of a geometry nobody described. Move them apart or make them "
                        + "agree. A common cause is a rod ratio carried over from a different pole "
                        + "count: the largest non-overlapping ratio is sin(pi/N) / (1 - sin(pi/N)), "
                        + "which is 2.414 for four rods, 1.000 for six and 0.620 for eight",
                });

                // One report per geometry rather than one per pair: a ratio that is
                // wrong makes every adjacent pair wrong, and a list of nine
                // identical complaints is harder to read than one.
                return;
            }
        }
    }

    /// <summary>Whether two electrodes hold the same thing, so overlapping is harmless.</summary>
    /// <remarks>
    /// Over <em>every</em> tap, not over the first. An electrode may be fed by more
    /// than one generator, and comparing <c>DriveAmplitude</c> - which is the first
    /// tap - would call two electrodes identical when they agreed about the main RF
    /// and differed about a supplementary excitation. The mask keeps whichever was
    /// written last, so that is a field of a geometry nobody described, arrived at
    /// through the one check that exists to prevent it.
    /// </remarks>
    private static bool Agrees(CompiledElectrode a, CompiledElectrode b)
    {
        if (a.Potential != b.Potential || a.Taps.Count != b.Taps.Count)
        {
            return false;
        }

        // Order matters, and that is the conservative reading: two electrodes whose
        // taps are the same set in a different order really do hold the same thing,
        // and calling them different costs a spurious refusal rather than a silent
        // wrong field. Templates write their taps in one order anyway.
        for (var k = 0; k < a.Taps.Count; k++)
        {
            if (a.Taps[k].Drive != b.Taps[k].Drive
                || a.Taps[k].Amplitude != b.Taps[k].Amplitude
                || a.Taps[k].Phase != b.Taps[k].Phase)
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(CompiledElectrode e) =>
        e.IsDriven
            ? $"{e.Potential:G6} V DC with "
                + string.Join(
                    ", ",
                    e.Taps.Select(t =>
                        $"{t.Amplitude:G6} V of drive {t.Drive} at phase {t.Phase:G4}"))
            : $"{e.Potential:G6} V";

    /// <summary>Whether two electrodes share any point.</summary>
    /// <remarks>
    /// Exact for the pairs it handles, and false for the pairs it does not. A test
    /// that guessed at an edge profile would be a test that sometimes refuses a
    /// legitimate geometry, which is worse than one that sometimes misses.
    /// </remarks>
    private static bool Intersects(CompiledElectrode a, CompiledElectrode b) =>
        (a.Shape, b.Shape) switch
        {
            (ElectrodeShape.Disc, ElectrodeShape.Disc) => DiscDisc(a, b),
            (ElectrodeShape.Rectangle, ElectrodeShape.Rectangle) => RectangleRectangle(a, b),
            (ElectrodeShape.Disc, ElectrodeShape.Rectangle) => DiscRectangle(a, b),
            (ElectrodeShape.Rectangle, ElectrodeShape.Disc) => DiscRectangle(b, a),
            (ElectrodeShape.Polygon, ElectrodeShape.Polygon) => PolygonPolygon(a.Vertices, b.Vertices),
            (ElectrodeShape.Polygon, ElectrodeShape.Disc) => PolygonDisc(a, b),
            (ElectrodeShape.Disc, ElectrodeShape.Polygon) => PolygonDisc(b, a),
            (ElectrodeShape.Polygon, ElectrodeShape.Rectangle) => PolygonPolygon(a.Vertices, Corners(b)),
            (ElectrodeShape.Rectangle, ElectrodeShape.Polygon) => PolygonPolygon(Corners(a), b.Vertices),
            _ => false,
        };

    private static IReadOnlyList<(double X, double Y)> Corners(CompiledElectrode rectangle) =>
        [(rectangle.MinX, rectangle.MinY), (rectangle.MaxX, rectangle.MinY), (rectangle.MaxX, rectangle.MaxY), (rectangle.MinX, rectangle.MaxY)];

    /// <summary>
    /// Whether a disc's interior reaches into a polygon: its centre is nearer the
    /// polygon than its radius. Tangency is allowed, as for two discs.
    /// </summary>
    private static bool PolygonDisc(CompiledElectrode polygon, CompiledElectrode disc) =>
        polygon.SignedDistance(disc.CentreX, disc.CentreY) < disc.Radius * (1.0 - 1e-12);

    /// <summary>
    /// Whether two polygons share interior: an edge of one crosses an edge of the
    /// other properly, or a vertex of one lies strictly inside the other.
    /// </summary>
    /// <remarks>
    /// Proper crossings and strict containment, so two polygons sharing an edge -
    /// the two halves of a slotted rod with the slot closed, say - are tangent
    /// rather than overlapping, which is the same allowance two touching discs get.
    /// A vertex exactly on the other's surface counts as touching.
    /// </remarks>
    private static bool PolygonPolygon(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            var (ax, ay) = a[i];
            var (bx, by) = a[(i + 1) % a.Count];
            for (var j = 0; j < b.Count; j++)
            {
                var (cx, cy) = b[j];
                var (dx, dy) = b[(j + 1) % b.Count];
                var d1 = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
                var d2 = ((bx - ax) * (dy - ay)) - ((by - ay) * (dx - ax));
                var d3 = ((dx - cx) * (ay - cy)) - ((dy - cy) * (ax - cx));
                var d4 = ((dx - cx) * (by - cy)) - ((dy - cy) * (bx - cx));
                if (((d1 > 0.0 && d2 < 0.0) || (d1 < 0.0 && d2 > 0.0))
                    && ((d3 > 0.0 && d4 < 0.0) || (d3 < 0.0 && d4 > 0.0)))
                {
                    return true;
                }
            }
        }

        return StrictlyInside(a, b) || StrictlyInside(b, a);
    }

    private static bool StrictlyInside(IReadOnlyList<(double X, double Y)> points, IReadOnlyList<(double X, double Y)> outline)
    {
        var probe = new CompiledElectrode { Name = "probe", Shape = ElectrodeShape.Polygon, Vertices = outline };
        var scale = 0.0;
        foreach (var (x, y) in outline)
        {
            scale = Math.Max(scale, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        // Strictly, with a tolerance scaled to the geometry: a shared vertex lands
        // at a distance of rounding noise, not of zero.
        var tolerance = 1e-12 * Math.Max(scale, 1e-12);
        foreach (var (x, y) in points)
        {
            if (probe.SignedDistance(x, y) < -tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DiscDisc(CompiledElectrode a, CompiledElectrode b)
    {
        var dx = a.CentreX - b.CentreX;
        var dy = a.CentreY - b.CentreY;
        var reach = a.Radius + b.Radius;

        // Strictly inside, so two rods exactly touching are allowed: tangency is a
        // legitimate design and a floating-point equality is a poor thing to refuse
        // on.
        return (dx * dx) + (dy * dy) < reach * reach * (1.0 - 1e-12);
    }

    private static bool RectangleRectangle(CompiledElectrode a, CompiledElectrode b) =>
        a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;

    private static bool DiscRectangle(CompiledElectrode disc, CompiledElectrode rectangle)
    {
        var x = Math.Clamp(disc.CentreX, rectangle.MinX, rectangle.MaxX);
        var y = Math.Clamp(disc.CentreY, rectangle.MinY, rectangle.MaxY);

        var dx = disc.CentreX - x;
        var dy = disc.CentreY - y;

        return (dx * dx) + (dy * dy) < disc.Radius * disc.Radius * (1.0 - 1e-12);
    }
}
