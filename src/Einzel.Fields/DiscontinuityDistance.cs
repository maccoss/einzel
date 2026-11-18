using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// How several declared discontinuities are composed: as members tracked separately for landing,
/// and as one combined measure for the distance to the nearest of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Landing is by member.</b> A field reports its discontinuity members
/// (<see cref="IElectrostaticField.DiscontinuityMembers"/>), sums and wrappers pass them through,
/// and the integrator lands on the earliest member to change sign. Two surfaces at one place are
/// then one landing - where, combined into one sign by a product, they cancelled and a real
/// surface was crossed with no landing (Stage 4's rule 1); and a member forced to change sign where
/// nothing jumps costs a landing more, never one less.
/// </para>
/// <para>
/// <b>The combined measure</b> (<see cref="Combine"/>) is what remains of the single-sign view: its
/// magnitude is the distance to the nearest member's surface, which is what a step cap reads. Its
/// sign is the product of the members', which flips for one crossing and not for two at one place,
/// so it is not what landing is decided by. Still a product rather than the nearer member's sign,
/// for a caller reading it alone: the nearer member's sign flips where the nearer surface changes,
/// with nothing crossed.
/// </para>
/// </remarks>
public static class DiscontinuityDistance
{
    /// <summary>
    /// Folds one more member's signed distance into a running composition.
    /// </summary>
    /// <param name="composed">
    /// The composition so far; positive infinity when no member has a discontinuity yet.
    /// </param>
    /// <param name="member">The next member's signed distance; non-finite means none.</param>
    /// <returns>The composition including <paramref name="member"/>.</returns>
    public static double Combine(double composed, double member)
    {
        if (!double.IsFinite(member))
        {
            return composed;
        }

        if (!double.IsFinite(composed))
        {
            return member;
        }

        var sign = (composed < 0.0 ? -1 : 1) * (member < 0.0 ? -1 : 1);

        return Math.Min(Math.Abs(composed), Math.Abs(member)) * sign;
    }

    /// <summary>Where each element's members start in a sum's numbering, and how many there are in all.</summary>
    /// <param name="elements">The elements, in order.</param>
    /// <returns>The first member of each element, then the total, as one array one longer than the elements.</returns>
    public static int[] MemberOffsets(IReadOnlyList<IElectrostaticField> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var offsets = new int[elements.Count + 1];

        for (var i = 0; i < elements.Count; i++)
        {
            offsets[i + 1] = offsets[i] + Math.Max(1, elements[i].DiscontinuityMembers);
        }

        return offsets;
    }

    /// <summary>One member of a sum: the element it belongs to, asked for its own member.</summary>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets (<see cref="MemberOffsets"/>).</param>
    /// <param name="member">The member, in the sum's numbering.</param>
    /// <param name="position">The point.</param>
    /// <returns>That member's measure.</returns>
    public static double Member(IReadOnlyList<IElectrostaticField> elements, int[] offsets, int member, in Vec3 position)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(offsets);

        for (var i = 0; i < elements.Count; i++)
        {
            if (member < offsets[i + 1])
            {
                return elements[i].MemberDistanceToDiscontinuity(member - offsets[i], in position);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(member), member, $"a sum of {offsets[^1]} members has no member {member}");
    }

    /// <summary>Every member of a sum, in one pass: each element fills its own slice.</summary>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets (<see cref="MemberOffsets"/>).</param>
    /// <param name="position">The point.</param>
    /// <param name="distances">Filled with every member's measure.</param>
    public static void Members(IReadOnlyList<IElectrostaticField> elements, int[] offsets, in Vec3 position, Span<double> distances)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(offsets);

        for (var i = 0; i < elements.Count; i++)
        {
            elements[i].MemberDistancesToDiscontinuity(in position, distances[offsets[i]..offsets[i + 1]]);
        }
    }
}
