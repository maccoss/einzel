using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>What a build held at its most, read from inside it.</summary>
/// <param name="Least">
/// The least the build can have held, in bytes above what was live before it: the largest over
/// the readings of a reading less everything other threads had allocated by then, any of which
/// may have been live in it.
/// </param>
/// <param name="Held">The largest live set read during the build, above what was live before it.</param>
/// <param name="Bound">
/// The most the build can have held, in bytes: <paramref name="Held"/>, or a reading plus
/// whatever the thread then allocated without reporting it, whichever is larger.
/// </param>
/// <param name="Readings">How many allocations the build reported, each one a reading.</param>
/// <param name="Unreported">
/// The most the build thread allocated between two readings without reporting it, in bytes.
/// </param>
/// <param name="Foreign">
/// Bytes other threads allocated while the measurement ran, which a reading cannot tell from the
/// build's own.
/// </param>
/// <param name="ForeignBurst">The most of those allocated between two readings.</param>
/// <param name="Retaken">
/// Measurements taken again because another thread allocated in bulk during them. When every
/// attempt was disturbed this is the number of attempts, and the measurement returned is the
/// least disturbed of them.
/// </param>
internal sealed record LivePeak(
    double Least,
    double Held,
    double Bound,
    int Readings,
    long Unreported,
    long Foreign,
    long ForeignBurst,
    int Retaken);

/// <summary>
/// The most a build holds reachable at once, read on the build's own thread at each allocation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A build's live set can only rise at an allocation, so its peak is held just after one.</b>
/// Every per-node array a solve allocates comes from <see cref="GridArrays"/>, which tells
/// whoever is watching; here each one forces a full blocking collection and reads what survived it, from the collector's
/// own record of that collection. The largest reading is the peak, read at the instant it is
/// held - not sampled, so there is no window to miss, no second thread whose priority or
/// scheduling matters, and nothing that depends on how long the solve runs or how many cycles
/// it takes.
/// </para>
/// <para>
/// <b>What it cannot see it bounds.</b> An allocation that does not go through
/// <see cref="GridArrays"/> - a list, a small array, or a per-node array somebody allocates
/// directly - is not read at. The thread's own allocation counter says how many bytes were
/// allocated between two readings, and the array just handed over accounts for some of them,
/// so the live set between them was at most the earlier reading plus the rest.
/// <see cref="LivePeak.Bound"/> is the largest such figure, and an upper edge is asserted on
/// it: a per-node array allocated directly fails as slack rather than passing unseen - and
/// fails <c>GridArraySourceTests</c> before that, which reads the source for one. That counts
/// garbage as held, which is the price of not being able to tell them apart, and it is how
/// the enumerators and handle stubs a mask used to allocate for every node were found.
/// </para>
/// <para>
/// The array handed over is one <see cref="GridArrays"/> has just allocated, so there is no
/// size a site could state wrongly and no report it could make twice.
/// </para>
/// <para>
/// <b>Other threads cannot fail a measurement, and cannot make one look better than it is.</b>
/// A reading is of the process, and cannot tell another thread's live bytes from the build's.
/// What other threads have allocated is counted - the process's total less this thread's - and
/// two things follow from it, neither of them a guess about what a runner's threads do:
/// </para>
/// <list type="bullet">
/// <item>
/// A reading follows a full collection, so the only bytes of another thread's in it are ones
/// still reachable, and there cannot be more of those than that thread has allocated <i>by
/// then</i>. The count is taken after the collection, so it covers everything that could be in
/// the reading and perhaps a little that came after it; each reading is reduced by it, and
/// <see cref="LivePeak.Least"/> is the largest result: a lower bound that is true whatever the
/// other threads were doing, and that widens by what was measured rather than failing.
/// </item>
/// <item>
/// <see cref="LivePeak.Bound"/> is of the process, so it is an upper bound on the build as it
/// stands, and another thread's memory can only raise it. A measurement in which more than
/// <see cref="ForeignLimit"/> arrived between two readings is taken again, up to
/// <see cref="Attempts"/> times, and after that the least disturbed attempt is returned with
/// its counts: an upper edge it fails then says, in the bytes printed beside it, why.
/// </item>
/// </list>
/// <para>
/// Memory the <i>build</i> allocates on another thread would be neither read at nor bounded.
/// <see cref="GridArrays"/> refuses to allocate on any thread but the one measuring, by name,
/// for as long as a measurement runs; an array allocated on a worker some other way is counted
/// with the rest, and <see cref="MemoryCases"/> fails a build when that count is a large part
/// of what it held.
/// </para>
/// <para>
/// In a process that does nothing but measure, other threads allocate 336 bytes a forced
/// collection from the second build measured on - the same count in every run, optimized or
/// not; where it comes from is not established. A test host's threads allocated 10 to 550 kB
/// during one build, which is why <see cref="MemoryCases"/> measures in a process of its own.
/// </para>
/// </remarks>
internal static class LiveSet
{
    /// <summary>
    /// Bytes other threads may allocate between two readings before the measurement is retaken.
    /// </summary>
    internal const long ForeignLimit = 16 * 1024;

    /// <summary>How many times a disturbed measurement is taken before the best is kept.</summary>
    internal const int Attempts = 4;

    /// <summary>Measures one build.</summary>
    /// <param name="build">The build; what it returns is held until the measurement ends.</param>
    /// <returns>What it held.</returns>
    internal static LivePeak Peak(Func<object> build)
    {
        LivePeak? best = null;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var once = Once(build);

            if (once.ForeignBurst <= ForeignLimit)
            {
                return once with { Retaken = attempt };
            }

            if (best is null || once.ForeignBurst < best.ForeignBurst)
            {
                best = once;
            }
        }

        return best! with { Retaken = Attempts };
    }

    private static LivePeak Once(Func<object> build)
    {
        // Counted from before the baseline is read, not after: a block another thread allocates
        // between the two is in every reading and would otherwise be in no count.
        var processAtPrevious = GC.GetTotalAllocatedBytes(precise: true);
        var threadAtOthers = GC.GetAllocatedBytesForCurrentThread();

        var baseline = Settled();

        var peak = baseline;
        var least = baseline;
        var bound = baseline;
        var previous = baseline;
        var unreported = 0L;
        var foreign = 0L;
        var burst = 0L;
        var readings = 0;

        var threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();

        // What other threads allocated since this was last asked: the process's count less
        // this thread's own.
        void Others()
        {
            var process = GC.GetTotalAllocatedBytes(precise: true);
            var thread = GC.GetAllocatedBytesForCurrentThread();
            var others = Math.Max(0L, (process - processAtPrevious) - (thread - threadAtOthers));

            foreign += others;
            burst = Math.Max(burst, others);
            processAtPrevious = process;
            threadAtOthers = thread;
        }

        void Read(Array array)
        {
            // Everything the thread allocated since the last reading, less the array just
            // handed over, was live on top of the last reading at most. An array carries a
            // header, so this is never negative.
            var silent = GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious - Buffer.ByteLength(array);

            unreported = Math.Max(unreported, silent);
            bound = Math.Max(bound, previous + silent);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

            previous = Survived();

            // After the collection, not before it: whatever of another thread's is in the
            // reading was allocated by now, and counted before, bytes arriving between the
            // count and the collection would be in the reading and not taken off it.
            Others();

            peak = Math.Max(peak, previous);
            least = Math.Max(least, previous - foreign);
            readings++;

            // Read last, so that what this reading allocated for itself is not charged to the build.
            threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();
        }

        object? built = null;

        GridArrays.StartWatching(Read);

        try
        {
            built = build();
        }
        finally
        {
            GridArrays.StopWatching();
        }

        // The stretch after the last report, which has no reading of its own.
        var tail = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious);

        unreported = Math.Max(unreported, tail);
        bound = Math.Max(bound, previous + tail);

        Others();

        GC.KeepAlive(built);

        return new LivePeak(
            least - baseline, peak - baseline, Math.Max(bound, peak) - baseline, readings, unreported, foreign, burst, 0);
    }

    /// <summary>The live set with nothing pending: collected, finalized, collected.</summary>
    private static long Settled()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        return Survived();
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
