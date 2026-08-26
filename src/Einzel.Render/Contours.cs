namespace Einzel.Render;

/// <summary>
/// Level sets of a scalar sampled over a plane, as polylines.
/// </summary>
/// <remarks>
/// <para>
/// Marching squares. One routine serves two jobs that look unrelated and are not:
/// an equipotential is a level set of the potential, and an <em>electrode outline
/// is the zero level set of its signed distance</em>. Drawing outlines that way is
/// what keeps the renderer free of device knowledge (architecture invariant 2) - a
/// rod, a plate, a ring and a sphere are all contoured by the same code, and a
/// shape added to the model format needs no change here at all.
/// </para>
/// <para>
/// Segments are joined into runs before they are returned, because an unjoined
/// soup of segments cannot be dashed, cannot be decimated to a bounded deviation,
/// and makes an output file several times larger than it needs to be.
/// </para>
/// </remarks>
public static class Contours
{
    /// <summary>A contour, in the plane's own coordinates.</summary>
    /// <param name="Points">The vertices, in metres.</param>
    /// <param name="Closed">Whether the run closes on itself.</param>
    public sealed record Run(IReadOnlyList<(double U, double V)> Points, bool Closed);

    /// <summary>Traces one level of a scalar sampled on a regular grid.</summary>
    /// <param name="values">Samples, indexed [column, row].</param>
    /// <param name="minU">Coordinate of column zero, in metres.</param>
    /// <param name="minV">Coordinate of row zero, in metres.</param>
    /// <param name="stepU">Column spacing, in metres.</param>
    /// <param name="stepV">Row spacing, in metres.</param>
    /// <param name="level">The value to trace.</param>
    /// <returns>The contour runs, in no particular order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    public static IReadOnlyList<Run> Trace(
        double[,] values, double minU, double minV, double stepU, double stepV, double level)
    {
        ArgumentNullException.ThrowIfNull(values);

        var columns = values.GetLength(0);
        var rows = values.GetLength(1);

        var segments = new List<((double U, double V) A, (double U, double V) B)>();

        for (var j = 0; j + 1 < rows; j++)
        {
            for (var i = 0; i + 1 < columns; i++)
            {
                // Corners anticlockwise from the bottom left, which is the ordering
                // the case table below is written against.
                var bottomLeft = values[i, j];
                var bottomRight = values[i + 1, j];
                var topRight = values[i + 1, j + 1];
                var topLeft = values[i, j + 1];

                if (!double.IsFinite(bottomLeft) || !double.IsFinite(bottomRight)
                    || !double.IsFinite(topRight) || !double.IsFinite(topLeft))
                {
                    continue;
                }

                var code = 0;

                if (bottomLeft > level)
                {
                    code |= 1;
                }

                if (bottomRight > level)
                {
                    code |= 2;
                }

                if (topRight > level)
                {
                    code |= 4;
                }

                if (topLeft > level)
                {
                    code |= 8;
                }

                if (code is 0 or 15)
                {
                    continue;
                }

                var u0 = minU + (i * stepU);
                var v0 = minV + (j * stepV);
                var u1 = u0 + stepU;
                var v1 = v0 + stepV;

                (double U, double V) Bottom() => (Lerp(u0, u1, bottomLeft, bottomRight, level), v0);
                (double U, double V) Top() => (Lerp(u0, u1, topLeft, topRight, level), v1);
                (double U, double V) Left() => (u0, Lerp(v0, v1, bottomLeft, topLeft, level));
                (double U, double V) Right() => (u1, Lerp(v0, v1, bottomRight, topRight, level));

                switch (code)
                {
                    case 1 or 14: segments.Add((Left(), Bottom())); break;
                    case 2 or 13: segments.Add((Bottom(), Right())); break;
                    case 3 or 12: segments.Add((Left(), Right())); break;
                    case 4 or 11: segments.Add((Right(), Top())); break;
                    case 6 or 9: segments.Add((Bottom(), Top())); break;
                    case 7 or 8: segments.Add((Left(), Top())); break;

                    // The two ambiguous cases, where opposite corners are on one
                    // side of the level and the other pair on the other. Resolved by
                    // the cell average, which is the standard disambiguation and the
                    // one that keeps a saddle from being drawn as a crossing.
                    case 5:
                    {
                        var centre = 0.25 * (bottomLeft + bottomRight + topRight + topLeft);

                        if (centre > level)
                        {
                            segments.Add((Left(), Top()));
                            segments.Add((Bottom(), Right()));
                        }
                        else
                        {
                            segments.Add((Left(), Bottom()));
                            segments.Add((Right(), Top()));
                        }

                        break;
                    }

                    case 10:
                    {
                        var centre = 0.25 * (bottomLeft + bottomRight + topRight + topLeft);

                        if (centre > level)
                        {
                            segments.Add((Left(), Bottom()));
                            segments.Add((Right(), Top()));
                        }
                        else
                        {
                            segments.Add((Left(), Top()));
                            segments.Add((Bottom(), Right()));
                        }

                        break;
                    }

                    default: break;
                }
            }
        }

        return Join(segments, 1e-9 * Math.Max(Math.Abs(stepU), Math.Abs(stepV)));
    }

    /// <summary>Samples a scalar over a plane on a regular grid.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="minU">Lower in-plane u, in metres.</param>
    /// <param name="minV">Lower in-plane v, in metres.</param>
    /// <param name="stepU">Column spacing, in metres.</param>
    /// <param name="stepV">Row spacing, in metres.</param>
    /// <param name="columns">Sample columns.</param>
    /// <param name="rows">Sample rows.</param>
    /// <param name="scalar">What to sample, given a point in space.</param>
    /// <returns>The samples, indexed [column, row].</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static double[,] Sample(
        SectionPlane plane,
        double minU,
        double minV,
        double stepU,
        double stepV,
        int columns,
        int rows,
        Func<Core.Geometry.Vec3, double> scalar)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(scalar);

        var values = new double[columns, rows];

        for (var j = 0; j < rows; j++)
        {
            for (var i = 0; i < columns; i++)
            {
                values[i, j] = scalar(plane.At(minU + (i * stepU), minV + (j * stepV)));
            }
        }

        return values;
    }

    private static double Lerp(double a, double b, double fa, double fb, double level)
    {
        var span = fb - fa;

        // A cell with no gradient across the edge has no crossing to locate, and the
        // midpoint is the only answer that does not depend on which way the
        // arithmetic rounded.
        if (span == 0.0)
        {
            return 0.5 * (a + b);
        }

        return a + ((b - a) * ((level - fa) / span));
    }

    private static List<Run> Join(
        List<((double U, double V) A, (double U, double V) B)> segments, double tolerance)
    {
        var runs = new List<Run>();

        if (segments.Count == 0)
        {
            return runs;
        }

        // Bucketed by rounded endpoint, so joining is linear in the segment count
        // rather than quadratic. A contour over a fine sample grid is tens of
        // thousands of segments and the quadratic form is minutes.
        var scale = tolerance > 0.0 ? 1.0 / (tolerance * 1e3) : 1e12;

        (long, long) Key((double U, double V) p) =>
            ((long)Math.Round(p.U * scale), (long)Math.Round(p.V * scale));

        // Undirected, because marching squares does not orient its segments
        // consistently: a cell and its complement produce the same pair in the same
        // order, so a contour crossing between them reverses. Matching head-to-tail
        // only, a rectangular conductor came out as four separate runs instead of
        // one - which looks identical until the path is filled or dashed.
        var at = new Dictionary<(long, long), List<(int Segment, bool AtB)>>();

        for (var i = 0; i < segments.Count; i++)
        {
            Add(at, Key(segments[i].A), (i, false));
            Add(at, Key(segments[i].B), (i, true));
        }

        var used = new bool[segments.Count];

        for (var seed = 0; seed < segments.Count; seed++)
        {
            if (used[seed])
            {
                continue;
            }

            used[seed] = true;

            var points = new LinkedList<(double U, double V)>();
            points.AddLast(segments[seed].A);
            points.AddLast(segments[seed].B);

            // Grow from the tail, then from the head, taking whichever end of a
            // candidate segment touches and appending its other end.
            for (var head = 0; head < 2; head++)
            {
                var extended = true;

                while (extended)
                {
                    extended = false;

                    var tip = head == 0 ? points.Last!.Value : points.First!.Value;

                    if (!at.TryGetValue(Key(tip), out var candidates))
                    {
                        break;
                    }

                    foreach (var (segment, atB) in candidates)
                    {
                        if (used[segment])
                        {
                            continue;
                        }

                        used[segment] = true;

                        var far = atB ? segments[segment].A : segments[segment].B;

                        if (head == 0)
                        {
                            points.AddLast(far);
                        }
                        else
                        {
                            points.AddFirst(far);
                        }

                        extended = true;
                        break;
                    }
                }
            }

            var closed = points.Count > 2 && Key(points.First!.Value) == Key(points.Last!.Value);

            if (closed)
            {
                points.RemoveLast();
            }

            runs.Add(new Run([.. points], closed));
        }

        return runs;
    }

    private static void Add(
        Dictionary<(long, long), List<(int Segment, bool AtB)>> map,
        (long, long) key,
        (int Segment, bool AtB) entry)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(entry);
    }
}
