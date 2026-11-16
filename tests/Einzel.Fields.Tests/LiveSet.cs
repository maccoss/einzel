using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>What a build held at its most, read from inside it.</summary>
/// <param name="Held">
/// The largest live set read during the build, above what was live before it, in bytes.
/// </param>
/// <param name="Bound">
/// The most the build can have held, in bytes: <paramref name="Held"/>, or a reading plus
/// whatever the thread then allocated without reporting it, whichever is larger.
/// </param>
/// <param name="Readings">How many allocations the build reported, each one a reading.</param>
/// <param name="Unreported">
/// The most the build thread allocated between two readings without reporting it, in bytes.
/// </param>
/// <param name="Foreign">
/// Bytes other threads allocated while the accepted measurement ran, which a reading cannot
/// tell from the build's own.
/// </param>
/// <param name="ForeignBurst">The most of those allocated between two readings.</param>
/// <param name="Discarded">Measurements taken again because another thread allocated in bulk during them.</param>
internal sealed record LivePeak(
    double Held, double Bound, int Readings, long Unreported, long Foreign, long ForeignBurst, int Discarded)
{
    /// <summary>
    /// The least the build can have held, in bytes: the largest reading, less everything other
    /// threads allocated, any of which may have been live in it.
    /// </summary>
    public double Least => Held - Foreign;
}

/// <summary>
/// The most a build holds reachable at once, read on the build's own thread at each allocation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A build's live set can only rise at an allocation, so its peak is held just after one.</b>
/// <see cref="AllocationProbe"/> is told at every per-node allocation a solve makes; here each
/// report forces a full blocking collection and reads what survived it, from the collector's
/// own record of that collection. The largest reading is the peak, read at the instant it is
/// held - not sampled, so there is no window to miss, no second thread whose priority or
/// scheduling matters, and nothing that depends on how long the solve runs or how many cycles
/// it takes.
/// </para>
/// <para>
/// <b>What it cannot see it bounds.</b> An allocation with no report - a list, a small array,
/// or a per-node array somebody adds without one - is not read at. The thread's own allocation
/// counter says how many bytes were allocated between two readings, and the report says how
/// many of those it accounts for, so the live set between them was at most the earlier reading
/// plus the difference. <see cref="LivePeak.Bound"/> is the largest such figure, and an upper
/// edge is asserted on it: an unreported per-node array fails as slack rather than passing
/// unseen.
/// </para>
/// <para>
/// <b>Other threads are the one thing left, and they are measured, not outvoted.</b> A reading
/// is of the process, and cannot tell another thread's live bytes from the build's. A solve
/// allocates on one thread, so whatever the process allocated beyond what this thread did is
/// not the build's, and it is counted between every two readings. All of it is taken off
/// <see cref="LivePeak.Least"/>, since any of it may have been live in the largest reading. And
/// a measurement in which more than <see cref="ForeignLimit"/> arrived between two readings is
/// taken again, since that much live would lift <see cref="LivePeak.Bound"/> by something the
/// build never held: discarded for a cause that was measured, never for its value.
/// </para>
/// <para>
/// What arrives in a process that does nothing but measure is 336 bytes a forced collection,
/// from the second build measured on - the same count in every run, with tiering on or off.
/// Where it comes from is not established: a bare process that only allocates and collects
/// shows none, with or without the shared array pool in use. A test host is another matter:
/// its threads allocated 10 to 550 kB during one build here, which is why
/// <see cref="MemoryCases"/> measures in a process of its own.
/// </para>
/// </remarks>
internal static class LiveSet
{
    /// <summary>
    /// Bytes other threads may allocate between two readings before the measurement is retaken.
    /// </summary>
    internal const long ForeignLimit = 16 * 1024;

    private const int Attempts = 8;

    /// <summary>Measures one build.</summary>
    /// <param name="build">The build; what it returns is held until the measurement ends.</param>
    /// <returns>What it held.</returns>
    internal static LivePeak Peak(Func<object> build)
    {
        LivePeak? last = null;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            last = Once(build) with { Discarded = attempt };

            if (last.ForeignBurst <= ForeignLimit)
            {
                return last;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"other threads allocated in bulk during each of {Attempts} measurements ({last!.ForeignBurst:N0} bytes "
            + $"between two readings of the last, {last.Foreign:N0} in all): the live set read is not the build's alone");
    }

    private static LivePeak Once(Func<object> build)
    {
        if (AllocationProbe.Observer is not null)
        {
            throw new InvalidOperationException("a measurement is already running on this thread");
        }

        var baseline = Settled();

        var peak = baseline;
        var bound = baseline;
        var previous = baseline;
        var unreported = 0L;
        var foreign = 0L;
        var burst = 0L;
        var readings = 0;

        var processAtPrevious = GC.GetTotalAllocatedBytes(precise: true);
        var threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();
        var threadAtOthers = threadAtPrevious;

        // What other threads allocated since the last reading: the process's count less this
        // thread's own.
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

        void Read(long reported)
        {
            // Everything the thread allocated since the last reading, less what this report
            // accounts for, was live on top of the last reading at most.
            var silent = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious - reported);

            unreported = Math.Max(unreported, silent);
            bound = Math.Max(bound, previous + silent);

            Others();

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

            previous = Survived();
            peak = Math.Max(peak, previous);
            readings++;

            // Read last, so that what this reading allocated for itself is not charged to the build.
            threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();
        }

        object? built = null;

        AllocationProbe.Observer = Read;

        try
        {
            built = build();
        }
        finally
        {
            AllocationProbe.Observer = null;
        }

        // The stretch after the last report, which has no reading of its own.
        var tail = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious);

        unreported = Math.Max(unreported, tail);
        bound = Math.Max(bound, previous + tail);

        Others();

        GC.KeepAlive(built);

        return new LivePeak(peak - baseline, Math.Max(bound, peak) - baseline, readings, unreported, foreign, burst, 0);
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
