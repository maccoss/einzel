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
    public static int[] MemberOffsets(IReadOnlyList<IElectrostaticField> elements) =>
        MemberOffsets(elements, static e => Math.Max(1, e.DiscontinuityMembers));

    /// <summary>One member of a sum: the element it belongs to, asked for its own member.</summary>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets (<see cref="MemberOffsets(IReadOnlyList{IElectrostaticField})"/>).</param>
    /// <param name="member">The member, in the sum's numbering.</param>
    /// <param name="position">The point.</param>
    /// <returns>That member's measure.</returns>
    public static double Member(IReadOnlyList<IElectrostaticField> elements, int[] offsets, int member, in Vec3 position) =>
        Member(elements, offsets, member, in position, static (e, m, in p) => e.MemberDistanceToDiscontinuity(m, in p));

    /// <summary>Every member of a sum, in one pass: each element fills its own slice.</summary>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets (<see cref="MemberOffsets(IReadOnlyList{IElectrostaticField})"/>).</param>
    /// <param name="position">The point.</param>
    /// <param name="distances">Filled with every member's measure.</param>
    public static void Members(IReadOnlyList<IElectrostaticField> elements, int[] offsets, in Vec3 position, Span<double> distances) =>
        Members(elements, offsets, in position, distances, static (e, in p, d) => e.MemberDistancesToDiscontinuity(in p, d));

    /// <summary>Every member's gate in a sum (<see cref="IElectrostaticField.DiscontinuityMemberGates"/>), in the sum's numbering.</summary>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets (<see cref="MemberOffsets(IReadOnlyList{IElectrostaticField})"/>).</param>
    /// <param name="gates">Filled with every member's gate.</param>
    public static void Gates(IReadOnlyList<IElectrostaticField> elements, int[] offsets, Span<int> gates) =>
        Gates(elements, offsets, gates, static (e, g) => e.DiscontinuityMemberGates(g));

    /// <summary>Reads every member's gate of an element into its slice.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="element">The element.</param>
    /// <param name="gates">Its slice, in its own numbering.</param>
    public delegate void GatesReader<in T>(T element, Span<int> gates);

    /// <summary>Every member's gate in a sum of any kind of element: each element's own, moved to the sum's numbering.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets.</param>
    /// <param name="gates">Filled with every member's gate, or -1.</param>
    /// <param name="read">Reads an element's gates into its slice.</param>
    public static void Gates<T>(IReadOnlyList<T> elements, int[] offsets, Span<int> gates, GatesReader<T> read)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(read);

        for (var i = 0; i < elements.Count; i++)
        {
            var slice = gates[offsets[i]..offsets[i + 1]];
            read(elements[i], slice);
            Shift(slice, offsets[i]);
        }
    }

    /// <summary>Moves gates read in a part's own numbering to where the part's members start.</summary>
    /// <param name="gates">The part's slice.</param>
    /// <param name="offset">Where its members start.</param>
    public static void Shift(Span<int> gates, int offset)
    {
        for (var k = 0; k < gates.Length; k++)
        {
            if (gates[k] >= 0)
            {
                gates[k] += offset;
            }
        }
    }

    /// <summary>Reads one member of an element.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="element">The element.</param>
    /// <param name="member">Its member, in its own numbering.</param>
    /// <param name="position">The point.</param>
    /// <returns>The measure.</returns>
    public delegate double MemberReader<in T>(T element, int member, in Vec3 position);

    /// <summary>Reads every member of an element into its slice.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="element">The element.</param>
    /// <param name="position">The point.</param>
    /// <param name="distances">Its slice of the sum's members.</param>
    public delegate void MembersReader<in T>(T element, in Vec3 position, Span<double> distances);

    /// <summary>
    /// Where each element's members start in a sum's numbering, for any kind of element: the one
    /// numbering an electric and a magnetic sum share.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="elements">The elements, in order.</param>
    /// <param name="count">How many members an element contributes; zero is allowed.</param>
    /// <returns>The first member of each element, then the total.</returns>
    public static int[] MemberOffsets<T>(IReadOnlyList<T> elements, Func<T, int> count)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(count);

        var offsets = new int[elements.Count + 1];

        for (var i = 0; i < elements.Count; i++)
        {
            offsets[i + 1] = offsets[i] + count(elements[i]);
        }

        return offsets;
    }

    /// <summary>One member of a sum of any kind of element.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets.</param>
    /// <param name="member">The member, in the sum's numbering.</param>
    /// <param name="position">The point.</param>
    /// <param name="read">Reads an element's own member.</param>
    /// <returns>That member's measure.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sum has no such member.</exception>
    public static double Member<T>(IReadOnlyList<T> elements, int[] offsets, int member, in Vec3 position, MemberReader<T> read)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(read);

        if (member >= 0)
        {
            for (var i = 0; i < elements.Count; i++)
            {
                if (member < offsets[i + 1])
                {
                    return read(elements[i], member - offsets[i], in position);
                }
            }
        }

        throw new ArgumentOutOfRangeException(nameof(member), member, $"a sum of {offsets[^1]} members has no member {member}");
    }

    /// <summary>Every member of a sum of any kind of element, in one pass.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="elements">The elements.</param>
    /// <param name="offsets">Their offsets.</param>
    /// <param name="position">The point.</param>
    /// <param name="distances">Filled with every member's measure.</param>
    /// <param name="read">Reads an element's members into its slice.</param>
    public static void Members<T>(IReadOnlyList<T> elements, int[] offsets, in Vec3 position, Span<double> distances, MembersReader<T> read)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(read);

        for (var i = 0; i < elements.Count; i++)
        {
            read(elements[i], in position, distances[offsets[i]..offsets[i + 1]]);
        }
    }
}
