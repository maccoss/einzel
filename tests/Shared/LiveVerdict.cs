using System.Globalization;

namespace Einzel.Testing;

/// <summary>
/// What a measurement of a live set says, as a sentence or as nothing: the logic the memory
/// tests assert with and the memory harness reports with, in one place.
/// </summary>
/// <remarks>
/// A measurement is an interval. An edge it is wholly past is a finding about what was measured;
/// an edge it straddles is a finding about the measurement only if other threads allocated
/// enough to account for the difference, and then it is said to be inconclusive, by name, with
/// the numbers.
/// </remarks>
internal static class LiveVerdict
{
    /// <summary>What a disturbed measurement's sentence begins with.</summary>
    internal const string Disturbed = "INCONCLUSIVE, the measurement was disturbed";

    /// <summary>
    /// The most another thread may allocate between two readings before the measurement is taken
    /// not to be the build's alone.
    /// </summary>
    /// <remarks>
    /// What the runtime's own threads do is a few hundred bytes a collection. 64 kB is two orders
    /// over that and under the smallest grid array the memory tests build.
    /// </remarks>
    internal const long MostForeignBurst = 64 * 1024;

    /// <summary>Why a figure in bytes a node does not lie between two edges, or null if it does.</summary>
    /// <param name="what">What the figure is, for the sentence.</param>
    /// <param name="lower">The least it can be.</param>
    /// <param name="upper">The most it can be.</param>
    /// <param name="least">The lower edge.</param>
    /// <param name="most">The upper edge.</param>
    /// <param name="nodes">The nodes the figure is per.</param>
    /// <param name="foreign">Bytes other threads allocated during the measurements it rests on.</param>
    internal static string? Against(string what, double lower, double upper, double least, double most, double nodes, long foreign)
    {
        // An interval wholly past an edge is a finding whatever other threads did: its lower
        // end is at least the build's own arrays still alive, and its upper end at most those
        // plus what the build's own thread allocated, and neither is something another thread
        // can add to. Only an edge the interval straddles can be the measurement's doing.
        if (lower > most)
        {
            return ($"{what} is at least {F2(lower)} B/node against an edge of {F2(most)}: something held is not billed "
                + $"({N0((lower - most) * nodes)} bytes over, in the build's own arrays)");
        }

        if (upper < least)
        {
            return ($"{what} is at most {F2(upper)} B/node against an edge of {F2(least)}: the statement bills what is not held");
        }

        // The upper edge first: an array allocated outside GridArrays widens the interval both
        // ways, and it is the upper edge's sentence that says so.
        if (upper > most)
        {
            var excess = (upper - most) * nodes;

            return foreign >= excess
                ? ($"{Disturbed}: {what} is at most {F2(upper)} B/node, over the edge of {F2(most)} by {N0(excess)} bytes, and other "
                    + $"threads allocated {N0(foreign)} bytes during the measurement - enough to be the excess. What was measured "
                    + "may be fine or not; run it again in a quieter process.")
                : ($"{what} is at most {F2(upper)} B/node against an edge of {F2(most)}: something held is not billed, or is "
                    + $"allocated without GridArrays (other threads allocated only {N0(foreign)} of the {N0(excess)} bytes over)");
        }

        if (lower < least)
        {
            var shortfall = (least - lower) * nodes;

            return foreign >= shortfall
                ? ($"{Disturbed}: {what} is at least {F2(lower)} B/node, under the edge of {F2(least)} by {N0(shortfall)} bytes, and "
                    + $"other threads allocated {N0(foreign)} bytes during the measurement - enough to be the shortfall.")
                : ($"{what} is at least {F2(lower)} B/node against an edge of {F2(least)}: the statement bills what is not held");
        }

        return null;
    }

    private static string F2(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string N0(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Why a measurement does not cover its build, or null if it does.</summary>
    internal static string? NotCovered(string what, LivePeak peak) => peak.Invalid is { } invalid ? $"{what}: {invalid}" : null;

    /// <summary>
    /// Why a measurement cannot be taken as the build's alone, or null if it can: another thread
    /// allocated, between two readings, more than anything but a grid array would come to.
    /// </summary>
    /// <remarks>
    /// The backstop behind <c>GridArrays</c>, which records by name any grid array allocated off
    /// the measuring thread: an array a build allocated on a worker <i>without</i> going through
    /// it would be neither read at nor bounded, only counted among what other threads allocated.
    /// Asked of the most between two readings, not of the total, which grows with how many
    /// collections a build needs - twenty thousand, for one test - where an array allocated on a
    /// worker arrives all at once.
    /// </remarks>
    internal static string? NotItsOwn(string what, LivePeak peak) => peak.ForeignBurst <= MostForeignBurst
        ? null
        : ($"{what}: {Disturbed} - another thread allocated {N0(peak.ForeignBurst)} bytes between two readings "
            + $"({N0(peak.Foreign)} in all). Either the measuring process was not the build's alone, or the code measured "
            + "now allocates on worker threads outside GridArrays, where the live set is neither read nor bounded.");
}
