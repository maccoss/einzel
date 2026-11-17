namespace Einzel.Testing;

/// <summary>Whether a measured figure lies inside its edges, said three ways.</summary>
/// <remarks>
/// A measurement is an interval. An edge it is wholly past is a finding about the solver; an
/// edge it straddles is a finding about the measurement. Which of those it is turns on whether
/// other threads allocated enough to account for the excess: if they did, the test cannot tell
/// and says so by name, with the numbers, rather than reporting the solver.
/// </remarks>
internal static class MemoryVerdict
{
    /// <summary>The message a disturbed measurement fails with.</summary>
    internal const string Disturbed = "INCONCLUSIVE, the measurement was disturbed";

    /// <summary>Asserts a figure in bytes a node lies between two edges.</summary>
    /// <param name="what">What the figure is, for the message.</param>
    /// <param name="lower">The least it can be.</param>
    /// <param name="upper">The most it can be.</param>
    /// <param name="least">The lower edge.</param>
    /// <param name="most">The upper edge.</param>
    /// <param name="nodes">The nodes the figure is per.</param>
    /// <param name="foreign">Bytes other threads allocated during the measurements it rests on.</param>
    internal static void Within(string what, double lower, double upper, double least, double most, double nodes, long foreign)
    {
        // An interval wholly past an edge is a finding whatever other threads did: its lower
        // end is at least the build's own arrays still alive, and its upper end at most those
        // plus what the build's own thread allocated, and neither is something another thread
        // can add to. Only an edge the interval straddles can be the measurement's doing.
        if (lower > most)
        {
            Assert.Fail(
                $"{what} is at least {lower:F2} B/node against an edge of {most:F2}: something held is not billed "
                + $"({(lower - most) * nodes:N0} bytes over, in the build's own arrays)");
        }

        if (upper < least)
        {
            Assert.Fail($"{what} is at most {upper:F2} B/node against an edge of {least:F2}: the statement bills what is not held");
        }

        // The upper edge first: an array allocated outside GridArrays widens the interval both
        // ways, and it is the upper edge's message that says so.
        if (upper > most)
        {
            var excess = (upper - most) * nodes;

            Assert.Fail(foreign >= excess
                ? $"{Disturbed}: {what} is at most {upper:F2} B/node, over the edge of {most:F2} by {excess:N0} bytes, and other "
                    + $"threads allocated {foreign:N0} bytes during the measurement - enough to be the excess. The solver may be "
                    + "fine or not; run it again in a quieter process."
                : $"{what} is at most {upper:F2} B/node against an edge of {most:F2}: something held is not billed, or is "
                    + $"allocated without GridArrays (other threads allocated only {foreign:N0} of the {excess:N0} bytes over)");
        }

        if (lower < least)
        {
            var shortfall = (least - lower) * nodes;

            Assert.Fail(foreign >= shortfall
                ? $"{Disturbed}: {what} is at least {lower:F2} B/node, under the edge of {least:F2} by {shortfall:N0} bytes, and "
                    + $"other threads allocated {foreign:N0} bytes during the measurement - enough to be the shortfall."
                : $"{what} is at least {lower:F2} B/node against an edge of {least:F2}: the statement bills what is not held");
        }
    }

    /// <summary>
    /// Fails a measurement during which another thread allocated, between two readings, more than
    /// anything but a grid array would come to.
    /// </summary>
    /// <remarks>
    /// The backstop behind <c>GridArrays</c>, which records by name any grid array allocated off
    /// the measuring thread: an array a build allocated on a worker <i>without</i> going through
    /// it would be neither read at nor bounded, only counted among what other threads allocated.
    /// Asked of the most between two readings, not of the total: what the runtime's own threads
    /// do is a few hundred bytes a collection, and a total grows with how many collections a build
    /// needs - twenty thousand, for one test here - where an array allocated on a worker arrives
    /// all at once. 64 kB is two orders over that housekeeping and under the smallest grid array
    /// the memory tests build.
    /// </remarks>
    internal static void TheBuildsOwn(string name, LivePeak peak) =>
        Assert.True(
            peak.ForeignBurst <= 64 * 1024,
            $"{name}: {Disturbed} - another thread allocated {peak.ForeignBurst:N0} bytes between two readings "
            + $"({peak.Foreign:N0} in all). Either the measuring process was not the build's alone, or the code measured "
            + "now allocates on worker threads outside GridArrays, where the live set is neither read nor bounded.");

    /// <summary>Fails a measurement that does not cover the build, by name.</summary>
    internal static void Covered(string what, LivePeak peak)
    {
        if (peak.Invalid is { } invalid)
        {
            Assert.Fail($"{what}: {invalid}");
        }
    }
}
