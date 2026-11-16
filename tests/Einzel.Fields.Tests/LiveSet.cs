using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>What a build held at its most, read from inside it, as an interval.</summary>
/// <param name="Least">
/// The least the build can have held at its most, in bytes above what was live before it.
/// </param>
/// <param name="Bound">The most the build can have held at any instant, in the same terms.</param>
/// <param name="Held">The largest live set of the process read during the build, above the baseline.</param>
/// <param name="Tracked">The most the build's own grid arrays came to at a reading, in bytes.</param>
/// <param name="Readings">How many grid arrays the build allocated, each one a reading.</param>
/// <param name="Unreported">
/// The most the build thread allocated between two readings other than through
/// <see cref="GridArrays"/>, in bytes.
/// </param>
/// <param name="Silent">Everything the build thread allocated other than through <see cref="GridArrays"/>.</param>
/// <param name="Foreign">Bytes other threads allocated while the measurement ran.</param>
/// <param name="OffThread">
/// What <see cref="GridArrays"/> allocated on other threads during the measurement, or null.
/// A measurement with this set does not cover the build, and is a failure by name.
/// </param>
internal sealed record LivePeak(
    double Least,
    double Bound,
    double Held,
    double Tracked,
    int Readings,
    long Unreported,
    long Silent,
    long Foreign,
    string? OffThread);

/// <summary>
/// The most a build holds reachable at once, read on the build's own thread at each allocation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A build's live set can only rise at an allocation, so its peak is held just after one.</b>
/// Every per-node array a solve allocates comes from <see cref="GridArrays"/>, which tells
/// whoever is watching; here each one forces a full blocking collection and reads what
/// survived. Nothing is sampled, so there is no window to miss, no second thread whose
/// priority matters, and nothing that depends on how long the solve runs.
/// </para>
/// <para>
/// <b>Each measurement is an interval, and both ends come two ways.</b> The process's live set
/// is one way, and it is exact only if nothing else in the process allocates. The other uses
/// nothing but the build's own thread, so another thread cannot move it:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Least</b> is the larger, at any reading, of the build's own grid arrays still alive there -
/// each is followed by a weak handle, and after a full blocking collection a handle is cleared
/// exactly when its array is unreachable, so this is an exact part of what the build held - and
/// of the process's reading less everything other threads had allocated since the baseline, the
/// most of theirs that could be in it.
/// </item>
/// <item>
/// <b>Bound</b> is, at each instant, the smaller of the process's live set plus what the build
/// thread has since allocated, and the build's grid arrays alive at the last reading plus
/// everything the build thread has allocated other than through <see cref="GridArrays"/> since
/// the baseline, live or not. The first is tight in a quiet process; the second is tight
/// whatever other threads do, as long as the build allocates little outside
/// <see cref="GridArrays"/>.
/// </item>
/// </list>
/// <para>
/// So a thread allocating and keeping memory beside a build moves neither end, and nothing is
/// retaken or voted on. The second bound rests on the build allocating on its own thread:
/// <see cref="GridArrays"/> records any grid array allocated on another while a watch is on, and
/// that is reported in <see cref="LivePeak.OffThread"/> and fails by name; an array allocated
/// on a worker some other way is refused in the source by <c>GridArraySourceTests</c>. And both
/// ends assume the objects live at the baseline stay live, which in a process doing nothing
/// else they do.
/// </para>
/// <para>
/// <b>When the interval is still too wide to decide, that is said, not guessed at.</b>
/// <see cref="MemoryVerdict"/> fails an edge as "not billed" or "bills what is not held" only if
/// other threads could not account for the excess; otherwise it fails as a disturbed
/// measurement, with the numbers.
/// </para>
/// <para>
/// <b>The count of other threads' bytes starts just before the baseline's last collection</b>,
/// not before the first and not after the last. Before the first would charge the build with
/// what finalizers allocate while the baseline settles - live at the baseline or dead, in no
/// reading either way. After the last would miss a block allocated between that collection and
/// the count, which is in every reading and was in no count.
/// </para>
/// </remarks>
internal static class LiveSet
{
    /// <summary>The most grid arrays one build may allocate.</summary>
    internal const int Capacity = 1 << 14;

    /// <summary>Measures one build.</summary>
    /// <param name="build">The build; what it returns is held until the measurement ends.</param>
    /// <returns>What it held.</returns>
    internal static LivePeak Peak(Func<object> build)
    {
        // Allocated before the baseline, so that following the build's arrays is not in a
        // reading. A weak handle is a slot in the runtime's handle table, not an object.
        var handles = new GCHandle[Capacity];
        var sizes = new long[Capacity];
        var count = 0;

        try
        {
            return Measure(build, handles, sizes, ref count);
        }
        finally
        {
            for (var at = 0; at < count; at++)
            {
                handles[at].Free();
            }
        }
    }

    private static LivePeak Measure(Func<object> build, GCHandle[] handles, long[] sizes, ref int counted)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        var processAtOthers = GC.GetTotalAllocatedBytes(precise: true);
        var threadAtOthers = GC.GetAllocatedBytesForCurrentThread();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var baseline = Survived();

        var count = 0;
        double least = 0, bound = 0, held = 0, tracked = 0;
        double previous = 0, trackedPrevious = 0;
        long unreported = 0, silent = 0, foreign = 0;
        var threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();

        // What other threads allocated since this was last asked: the process's count less
        // this thread's own.
        void Others()
        {
            var process = GC.GetTotalAllocatedBytes(precise: true);
            var thread = GC.GetAllocatedBytesForCurrentThread();

            foreign += Math.Max(0L, (process - processAtOthers) - (thread - threadAtOthers));

            processAtOthers = process;
            threadAtOthers = thread;
        }

        // Up to and including the instant before the array arrived: the live set then was at most
        // the last reading plus what the thread allocated since, and at most the grid arrays alive
        // at the last reading plus everything the thread has allocated outside GridArrays.
        void Stretch(long since)
        {
            silent += since;
            unreported = Math.Max(unreported, since);
            bound = Math.Max(bound, Math.Min(previous + since, trackedPrevious + silent));
        }

        void Read(Array array, long bytes)
        {
            if (count == handles.Length)
            {
                throw new InvalidOperationException($"a build allocated more than {handles.Length:N0} grid arrays");
            }

            // An array carries a header, so this is never negative.
            Stretch(Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious - bytes));

            handles[count] = GCHandle.Alloc(array, GCHandleType.Weak);
            sizes[count] = bytes;
            count++;

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

            // After the collection: whatever of another thread's is in the reading was allocated
            // by now.
            Others();

            var reading = Survived() - baseline;
            var alive = Alive(handles, sizes, count);

            held = Math.Max(held, reading);
            tracked = Math.Max(tracked, alive);
            least = Math.Max(least, Math.Max(alive, reading - foreign));
            bound = Math.Max(bound, Math.Min(reading, alive + silent));

            previous = reading;
            trackedPrevious = alive;

            // Read last, so that what this reading allocated for itself is not charged to the build.
            threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();
        }

        object? built = null;
        string? offThread;

        GridArrays.StartWatching(Read);

        try
        {
            built = build();
        }
        finally
        {
            offThread = GridArrays.StopWatching();
            counted = count;
        }

        // The stretch after the last array, which has no reading of its own.
        Stretch(Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious));
        Others();

        GC.KeepAlive(built);

        return new LivePeak(least, Math.Max(bound, least), held, tracked, count, unreported, silent, foreign, offThread);
    }

    /// <summary>What the followed arrays still alive come to, in bytes.</summary>
    /// <remarks>
    /// In a frame of its own, not inlined: reading a handle's target puts the array in a local,
    /// and unoptimized code keeps a local to the end of its method. Done in the frame that then
    /// collects, that would keep an array the build had let go alive through the next reading.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Alive(GCHandle[] handles, long[] sizes, int count)
    {
        var alive = 0.0;

        for (var at = 0; at < count; at++)
        {
            if (handles[at].Target is not null)
            {
                alive += sizes[at];
            }
        }

        return alive;
    }

    /// <summary>
    /// What the last full blocking collection left live, read from that collection's own
    /// record: the generations' sizes after it, less the free space between their objects.
    /// </summary>
    /// <remarks>
    /// Not <see cref="GC.GetTotalMemory(bool)"/>, which counts everything allocated since the
    /// collection as well.
    /// </remarks>
    private static long Survived()
    {
        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        var live = 0L;

        foreach (var generation in info.GenerationInfo)
        {
            live += generation.SizeAfterBytes - generation.FragmentationAfterBytes;
        }

        return live;
    }
}

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

    /// <summary>Fails a measurement that does not cover the build, by name.</summary>
    internal static void Covered(string what, LivePeak peak)
    {
        if (peak.OffThread is { } offThread)
        {
            Assert.Fail($"{what}: {offThread}");
        }
    }
}
