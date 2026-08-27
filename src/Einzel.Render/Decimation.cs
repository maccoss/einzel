namespace Einzel.Render;

/// <summary>
/// Reduces a polyline to the fewest vertices that stay within a stated distance
/// of the original.
/// </summary>
/// <remarks>
/// <para>
/// Ramer-Douglas-Peucker. RND-5 requires trajectories to be decimated with a
/// stated geometric tolerance, because ten thousand polylines of a hundred
/// thousand points each is a file nothing will open - and ACC-7 sets the default
/// bound at 0.1% of the drawing's extent.
/// </para>
/// <para>
/// The tolerance is a <em>guarantee</em>, not a hint: no discarded point lies
/// further than it from the retained line. That is what makes it quotable in the
/// output per GRD-12, and it is why this is the recursive form rather than one of
/// the cheaper radial or nth-point schemes, which reduce point counts without
/// bounding anything.
/// </para>
/// </remarks>
public static class Decimation
{
    /// <summary>Decimates a polyline to a bounded deviation.</summary>
    /// <param name="points">The polyline.</param>
    /// <param name="tolerance">
    /// The greatest distance any discarded point may lie from the retained line,
    /// in the same units as the points.
    /// </param>
    /// <returns>The retained points, in order, always including both ends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    public static IReadOnlyList<PagePoint> Reduce(IReadOnlyList<PagePoint> points, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        if (points.Count <= 2 || tolerance == 0.0)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        Split(points, 0, points.Count - 1, tolerance, keep);

        var kept = new List<PagePoint>(points.Count);

        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                kept.Add(points[i]);
            }
        }

        return kept;
    }

    /// <summary>The worst deviation between a polyline and a decimation of it.</summary>
    /// <param name="points">The polyline.</param>
    /// <param name="reduced">A decimation of it.</param>
    /// <returns>The greatest distance from any original point to the reduced line.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// The measurement that makes the guarantee checkable rather than asserted. A
    /// decimator that quietly exceeded its tolerance would produce a figure that
    /// looks fine and is wrong by more than it says it is.
    /// </remarks>
    public static double WorstDeviation(
        IReadOnlyList<PagePoint> points, IReadOnlyList<PagePoint> reduced)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(reduced);

        if (reduced.Count < 2)
        {
            return 0.0;
        }

        var worst = 0.0;

        foreach (var point in points)
        {
            var nearest = double.PositiveInfinity;

            for (var i = 0; i + 1 < reduced.Count; i++)
            {
                nearest = Math.Min(nearest, DistanceToSegment(point, reduced[i], reduced[i + 1]));
            }

            worst = Math.Max(worst, nearest);
        }

        return worst;
    }

    private static void Split(
        IReadOnlyList<PagePoint> points, int first, int last, double tolerance, bool[] keep)
    {
        if (last <= first + 1)
        {
            return;
        }

        var worst = 0.0;
        var at = first;

        for (var i = first + 1; i < last; i++)
        {
            var distance = DistanceToSegment(points[i], points[first], points[last]);

            if (distance > worst)
            {
                worst = distance;
                at = i;
            }
        }

        if (worst <= tolerance)
        {
            return;
        }

        keep[at] = true;

        Split(points, first, at, tolerance, keep);
        Split(points, at, last, tolerance, keep);
    }

    private static double DistanceToSegment(PagePoint point, PagePoint a, PagePoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared == 0.0)
        {
            return Math.Sqrt(((point.X - a.X) * (point.X - a.X)) + ((point.Y - a.Y) * (point.Y - a.Y)));
        }

        // Clamped, so a point past either end measures to that end rather than to
        // the infinite line through them. A trajectory that turns round - which is
        // what a mirror is for - has its far end close to the infinite line and a
        // long way from the segment, and the unclamped form would decimate the
        // turning point away.
        var t = Math.Clamp((((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / lengthSquared, 0.0, 1.0);

        var px = a.X + (t * dx);
        var py = a.Y + (t * dy);

        return Math.Sqrt(((point.X - px) * (point.X - px)) + ((point.Y - py) * (point.Y - py)));
    }
}
