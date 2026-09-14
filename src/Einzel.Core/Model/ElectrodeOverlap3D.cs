using Einzel.Core.Errors;

namespace Einzel.Core.Model;

/// <summary>
/// Refuses two conductors in a volume geometry that occupy the same space and
/// disagree about what they hold.
/// </summary>
/// <remarks>
/// <para>
/// The same argument as <see cref="ElectrodeOverlap"/> one dimension up, and the same
/// allowances: a Dirichlet mask is written electrode by electrode, so where two
/// overlap the last one wins. Agreement is harmless and often deliberate - a fillet
/// or a shoulder is built from primitives that share metal - and tangency is a
/// legitimate design, which is how a segmented chain is written: stripe <c>k</c> and
/// stripe <c>k+1</c> share a face exactly and hold different potentials.
/// </para>
/// <para>
/// <b>It searches for a witness rather than proving disjointness, and that is the
/// design rather than a shortcut.</b> A point strictly inside both conductors
/// <em>proves</em> the geometry is ill-posed, so a refusal here is never wrong. The
/// opposite claim - that no such point exists anywhere - is far more expensive,
/// because two conductors sharing a face have <c>max(dA, dB)</c> equal to zero over a
/// whole surface and a subdivision would have to cover it. Since the doctrine one
/// dimension down is that a check must <em>miss rather than falsely refuse</em>, the
/// expensive half is the half not worth buying: a budget running out here means "no
/// violation found", not "inconclusive, refuse anyway".
/// </para>
/// <para>
/// <b>Uniform over every shape, and over a sixth nobody has written yet.</b> It asks
/// each primitive only for its bounding box and its signed distance, both of which
/// the model format already requires for the solver and the ion absorber. That is
/// architecture invariant 2 in a new place: the fifteen exact pair tests five shapes
/// would otherwise need - a tilted box against a revolved hyperbola has no closed
/// form worth writing - become one search, and a new primitive needs no change here
/// at all.
/// </para>
/// <para>
/// <b>A bounding-box screen alone will not do.</b> The C-trap's five rods are nested
/// arcs about one axis, so <c>rodInnerUpper</c>'s box sits entirely inside
/// <c>rodOuter</c>'s while the metal is nowhere near it. The screen is still worth
/// having in front of the search - on the shipped Astral it settles 3,204 of 3,328
/// disagreeing pairs at no cost - but it settles them the safe way, by proving
/// disjointness.
/// </para>
/// <para>
/// <b>A refusal rests on the sign of a signed distance and on nothing else.</b> The
/// Lipschitz bound decides only which boxes are worth opening, so a primitive whose
/// distance is a conservative under-estimate rather than exact can cost this search a
/// witness - never a false refusal, because the witness itself is a point that both
/// primitives report as interior. That is the safe direction, and it is the same one
/// the plane check took when it answered false for the pairs it cannot test exactly.
/// </para>
/// </remarks>
public static class ElectrodeOverlap3D
{
    /// <summary>
    /// How deep inside both conductors a point must be before it counts as a witness,
    /// as a fraction of the pair's extent.
    /// </summary>
    /// <remarks>
    /// <b>Load-bearing, and for the reason the flat-face defect taught.</b> Two faces
    /// meant to coincide are written as two different expressions over the parameter
    /// surface - <c>bendRadius + rodHalfWidth</c> against
    /// <c>bendRadius + inscribedRadius * sqrt(1 + 0)</c> - and agree to a few ulps
    /// rather than exactly. Testing a computed quantity against zero is what made a
    /// symmetric electrode solve to an asymmetric field, and here it would refuse a
    /// geometry whose author did nothing wrong. This sits seven orders above that
    /// rounding and six below the shallowest overlap anyone means: the one this check
    /// found on its first run is 642 um on a 730 mm instrument, which is 9e-4.
    /// </remarks>
    private const double DepthFraction = 1e-9;

    /// <summary>Probes one pair may spend before the answer is "none found".</summary>
    /// <remarks>
    /// Reached only by a pair that genuinely touches, where the search is driven into
    /// the contact and there is nothing to find. A real interpenetration is found in far
    /// fewer - the Astral's buried drift stripe is found by the very first probe, its own
    /// center - and <c>ElectrodeOverlap3DTests</c> measures how thin a sliver still is:
    /// 1 um of shared metal on a 10 mm box. That is a slab, where two faces overlap over
    /// their whole area; a near-tangency between two <em>curved</em> surfaces shares a
    /// lens rather than a slab and is harder, and that limit is not measured.
    /// </remarks>
    private const int PairBudget = 3000;

    /// <summary>Checks a solved 3D geometry for contradictory overlaps.</summary>
    /// <param name="electrodes">The compiled electrodes, in declaration order.</param>
    /// <param name="stages">The timed states, empty when the geometry holds one.</param>
    /// <param name="path">JSON Pointer to the solve block, for the error object.</param>
    /// <param name="errors">Where a violation is recorded.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <b>Over every state the instrument has, not over the one it is declared in.</b>
    /// A stage may change what an electrode holds - that is what a stage is for - and
    /// may not change where it is, which <c>SameGeometry3D</c> enforces. So "do these
    /// two agree" has as many answers as there are states, and asking it of the base
    /// state alone is a proxy that stops being equivalent the moment a document
    /// declares a sequence: two conductors sharing metal at one potential while held,
    /// and at two while pushing, would be a field of a geometry nobody described for
    /// exactly the duration of the push. The geometry is fixed across states, so the
    /// expensive half - the witness search - is still done once per pair.
    /// </remarks>
    public static void Check(
        IReadOnlyList<CompiledElectrode3D> electrodes,
        IReadOnlyList<CompiledStage3D> stages,
        string path,
        List<EinzelError> errors)
    {
        ArgumentNullException.ThrowIfNull(electrodes);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(errors);

        // Every state the pair is ever in, base first so an unsequenced geometry is
        // reported exactly as it was before stages were considered.
        var states = new List<IReadOnlyList<CompiledElectrode3D>> { electrodes };

        foreach (var stage in stages)
        {
            if (stage.Electrodes.Count == electrodes.Count)
            {
                states.Add(stage.Electrodes);
            }

            if (stage.EndElectrodes is { } end && end.Count == electrodes.Count)
            {
                states.Add(end);
            }
        }

        for (var i = 0; i < electrodes.Count; i++)
        {
            for (var j = i + 1; j < electrodes.Count; j++)
            {
                var disagreeing = states.FirstOrDefault(s => !Agrees(s[i], s[j]));

                if (disagreeing is null)
                {
                    continue;
                }

                if (Witness(electrodes[i], electrodes[j]) is not { } found)
                {
                    continue;
                }

                var a = disagreeing[i];
                var b = disagreeing[j];

                var when = ReferenceEquals(disagreeing, electrodes)
                    ? string.Empty
                    : $" during '{Named(stages, disagreeing)}'";

                var (x, y, z, depth) = found;

                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/electrodes",
                    Constraint =
                        $"'{a.Name}' and '{b.Name}' occupy the same space and hold different "
                        + $"excitations{when}: {Describe(a)} against {Describe(b)}. They share "
                        + $"the point ({x * 1e3:G6}, {y * 1e3:G6}, {z * 1e3:G6}) mm, which is "
                        + $"{depth * 1e6:G4} um inside both",
                    Observed = new ObservedValue(depth, "m"),
                    Suggestion =
                        "two conductors cannot be in one place at two potentials, and a mask "
                        + "built from them keeps whichever was written last - so the solve would "
                        + "return the field of a geometry nobody described. Move them apart or "
                        + "make them agree. Touching is allowed, so a shared face needs no gap; "
                        + "what is refused is the metal of one inside the other",
                });

                // One report per geometry rather than one per pair, as in the plane: a
                // ratio or an offset that is wrong makes every adjacent pair wrong, and
                // a list of identical complaints is harder to read than one.
                return;
            }
        }
    }

    /// <summary>Which stage a set of electrodes came from, for the message.</summary>
    private static string Named(
        IReadOnlyList<CompiledStage3D> stages, IReadOnlyList<CompiledElectrode3D> state)
    {
        foreach (var stage in stages)
        {
            if (ReferenceEquals(stage.Electrodes, state)
                || ReferenceEquals(stage.EndElectrodes, state))
            {
                return stage.Name;
            }
        }

        return "a stage";
    }

    /// <summary>Whether two electrodes hold the same thing, so overlapping is harmless.</summary>
    /// <remarks>
    /// Over <em>every</em> tap and in order, which is the plane check's own reading and
    /// the conservative one: two electrodes whose taps are the same set in a different
    /// order really do hold the same thing, and calling them different costs a spurious
    /// complaint rather than a silent wrong field.
    /// </remarks>
    private static bool Agrees(CompiledElectrode3D a, CompiledElectrode3D b)
    {
        if (a.Potential != b.Potential || a.Taps.Count != b.Taps.Count)
        {
            return false;
        }

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

    private static string Describe(CompiledElectrode3D e) =>
        e.IsDriven
            ? $"{e.Potential:G6} V DC with "
                + string.Join(
                    ", ",
                    e.Taps.Select(t =>
                        $"{t.Amplitude:G6} V of drive {t.Drive} at phase {t.Phase:G4}"))
            : $"{e.Potential:G6} V";

    /// <summary>
    /// A point strictly inside both electrodes, or null if the search did not find one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Branch and bound on <c>f = max(dA, dB)</c> over the intersection of the two
    /// bounding boxes. Both distances are signed and measured in metres, so <c>f</c> is
    /// negative exactly where both conductors have metal - which makes a single probe
    /// with <c>f</c> below the threshold a proof, needing no further search.
    /// </para>
    /// <para>
    /// <b>The bound is what makes it a search rather than a sampling.</b> A signed
    /// distance is 1-Lipschitz, so no point within <c>r</c> of a probe can have <c>f</c>
    /// below <c>f(probe) - r</c>; a box whose half-diagonal cannot carry its centre's
    /// value below the threshold contains no witness and is discarded whole. What is
    /// left is expanded deepest-bound-first, so the search walks toward the most
    /// promising region rather than dividing the box evenly.
    /// </para>
    /// <para>
    /// <b>Both centres are probed before any subdivision.</b> For the commonest real
    /// mistake - one conductor sitting inside another - that settles it in two
    /// evaluations. It is a seed and not a shortcut: <c>Centre</c> is the centre of a
    /// prism or revolve outline's own bounding box, which a concave profile need not
    /// contain, and a probe that lands outside costs one evaluation and nothing else.
    /// </para>
    /// </remarks>
    private static (double X, double Y, double Z, double Depth)? Witness(
        CompiledElectrode3D a, CompiledElectrode3D b)
    {
        var (aMinX, aMinY, aMinZ, aMaxX, aMaxY, aMaxZ) = a.Bounds;
        var (bMinX, bMinY, bMinZ, bMaxX, bMaxY, bMaxZ) = b.Bounds;

        double x0 = Math.Max(aMinX, bMinX), x1 = Math.Min(aMaxX, bMaxX);
        double y0 = Math.Max(aMinY, bMinY), y1 = Math.Min(aMaxY, bMaxY);
        double z0 = Math.Max(aMinZ, bMinZ), z1 = Math.Min(aMaxZ, bMaxZ);

        // Disjoint boxes prove disjoint solids, and that is the one direction in which a
        // box screen can prove anything. It settles the great majority of pairs.
        if (x1 <= x0 || y1 <= y0 || z1 <= z0)
        {
            return null;
        }

        var extent = Math.Max(
            Math.Max(aMaxX - aMinX, bMaxX - bMinX),
            Math.Max(
                Math.Max(aMaxY - aMinY, bMaxY - bMinY),
                Math.Max(aMaxZ - aMinZ, bMaxZ - bMinZ)));

        var depth = DepthFraction * extent;

        (double X, double Y, double Z, double Depth)? Try(double x, double y, double z)
        {
            var f = Math.Max(a.SignedDistance(x, y, z), b.SignedDistance(x, y, z));

            return f < -depth ? (x, y, z, -f) : null;
        }

        // Both centres first: for a convex primitive each lies inside its own electrode,
        // which settles containment - the commonest way a document gets this wrong -
        // immediately. A concave outline may put it outside, and then it is just a probe.
        var (acx, acy, acz) = a.Centre;
        var (bcx, bcy, bcz) = b.Centre;

        if (Try(acx, acy, acz) is { } inA)
        {
            return inA;
        }

        if (Try(bcx, bcy, bcz) is { } inB)
        {
            return inB;
        }

        var queue =
            new PriorityQueue<
                (double X0, double Y0, double Z0, double X1, double Y1, double Z1), double>();

        queue.Enqueue((x0, y0, z0, x1, y1, z1), double.NegativeInfinity);

        for (var probes = 0; probes < PairBudget && queue.Count > 0; probes++)
        {
            var (qx0, qy0, qz0, qx1, qy1, qz1) = queue.Dequeue();

            double cx = 0.5 * (qx0 + qx1), cy = 0.5 * (qy0 + qy1), cz = 0.5 * (qz0 + qz1);
            double hx = 0.5 * (qx1 - qx0), hy = 0.5 * (qy1 - qy0), hz = 0.5 * (qz1 - qz0);

            var f = Math.Max(a.SignedDistance(cx, cy, cz), b.SignedDistance(cx, cy, cz));

            if (f < -depth)
            {
                return (cx, cy, cz, -f);
            }

            // A signed distance moves by at most the distance travelled, so nothing in
            // this box can be deeper than this.
            var reach = Math.Sqrt((hx * hx) + (hy * hy) + (hz * hz));
            var bound = f - reach;

            if (bound >= -depth)
            {
                continue;
            }

            queue.Enqueue((qx0, qy0, qz0, cx, cy, cz), bound);
            queue.Enqueue((cx, qy0, qz0, qx1, cy, cz), bound);
            queue.Enqueue((qx0, cy, qz0, cx, qy1, cz), bound);
            queue.Enqueue((cx, cy, qz0, qx1, qy1, cz), bound);
            queue.Enqueue((qx0, qy0, cz, cx, cy, qz1), bound);
            queue.Enqueue((cx, qy0, cz, qx1, cy, qz1), bound);
            queue.Enqueue((qx0, cy, cz, cx, qy1, qz1), bound);
            queue.Enqueue((cx, cy, cz, qx1, qy1, qz1), bound);
        }

        return null;
    }
}
