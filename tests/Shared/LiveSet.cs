using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Einzel.Core.Numerics;

namespace Einzel.Testing;

/// <summary>What a build held at its most, read from inside it, as an interval.</summary>
/// <param name="Least">
/// The least the build can have held at its most, in bytes above what was live before it.
/// </param>
/// <param name="Bound">The most the build can have held at any instant, in the same terms.</param>
/// <param name="Held">The largest live set of the process read during the build, above the baseline.</param>
/// <param name="Tracked">The most the build's own grid arrays came to at a reading, in bytes.</param>
/// <param name="Readings">How many grid arrays the build allocated.</param>
/// <param name="Collections">
/// How many of them were read: at the others the live set could not have passed what had
/// already been read.
/// </param>
/// <param name="Unreported">
/// The most the build thread allocated between two readings other than through
/// <see cref="GridArrays"/>, in bytes.
/// </param>
/// <param name="Silent">Everything the build thread allocated other than through <see cref="GridArrays"/>.</param>
/// <param name="Foreign">Bytes other threads allocated while the measurement ran.</param>
/// <param name="ForeignBurst">The most of those allocated between two readings.</param>
/// <param name="Retained">
/// What the build's own grid arrays still alive come to once the build has returned and what
/// it returned has been let go, in bytes: nothing, unless the build left an array somewhere
/// that outlives it - a static, a pool, a cache that grows by the run.
/// </param>
/// <param name="Invalid">
/// Why the measurement does not cover the build - grid arrays allocated on other threads, or
/// the watcher itself failing - or null. A measurement with this set is a failure by name.
/// </param>
internal sealed record LivePeak(
    double Least,
    double Bound,
    double Held,
    double Tracked,
    int Readings,
    int Collections,
    long Unreported,
    long Silent,
    long Foreign,
    long ForeignBurst,
    double Retained,
    string? Invalid);

/// <summary>
/// The most a build holds reachable at once, read on the build's own thread at each allocation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A build's live set can only rise at an allocation, so its peak is held just after one.</b>
/// Every per-node array a solve or a density run allocates comes from <see cref="GridArrays"/>, which tells
/// whoever is watching; here each one that could raise the peak forces a full blocking
/// collection and reads what survived, and one that provably could not is passed over. Nothing
/// is sampled, so there is no window to miss, no second thread whose
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
/// that is reported in <see cref="LivePeak.Invalid"/> and fails by name; an array allocated
/// on a worker some other way is refused in the source by <c>GridArraySourceTests</c>. And both
/// ends assume the objects live at the baseline stay live, which in a process doing nothing
/// else they do.
/// </para>
/// <para>
/// <b>When the interval is still too wide to decide, that is said, not guessed at.</b>
/// <c>MemoryVerdict</c> fails an edge as "not billed" or "bills what is not held" only if
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
    /// <summary>How many grid arrays the storage for following them starts with.</summary>
    internal const int InitialCapacity = 1 << 14;

    /// <summary>Measures one build.</summary>
    /// <param name="build">The build; what it returns is held until the measurement ends.</param>
    /// <param name="collectEveryArray">
    /// Read at every grid array rather than only where the live set could pass what has
    /// already been read; for the test that the two give the same least.
    /// </param>
    /// <param name="capacity">How many arrays to make room for before the baseline.</param>
    /// <returns>What it held.</returns>
    internal static LivePeak Peak(Func<object> build, bool collectEveryArray = false, int capacity = InitialCapacity)
    {
        // Allocated before the baseline, so that following the build's arrays is not in a
        // reading. A weak handle is a slot in the runtime's handle table, not an object.
        var followed = new Followed(capacity);

        // Grown once, and dropped, before the baseline: what growing costs the first time - the
        // runtime loading the generic instantiations it uses - is then in the baseline, and what
        // growing during the build keeps is only the storage itself, which Own takes off.
        var warm = new Followed(1);
        warm.Grow();

        // And what an array occupies, measured once, before the baseline rather than inside it.
        _ = ArraySize.Of(0);

        try
        {
            return Measure(build, followed, collectEveryArray);
        }
        finally
        {
            followed.Free();

            // Kept to the end: it is in the baseline, and an object of the baseline that dies during
            // the build makes every later reading low by its size.
            GC.KeepAlive(warm);
        }
    }

    [ThreadStatic]
    private static Action? between;

    /// <summary>
    /// Offers the measurement running on this thread, if there is one, a reading between two of
    /// the build's arrays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build's live set can only rise at an allocation, and the allocations that are not grid
    /// arrays are bounded rather than read: everything the thread allocated since the last
    /// reading is taken as possibly live. That is a few kilobytes for a solve. A run that goes
    /// on for a thousand steps after its last array, each step allocating a little, has nothing
    /// to read at and its bound grows by all of it - megabytes on a megabyte. A build that can
    /// call out between steps calls this, which reads exactly as at an array, and only where
    /// the live set could by then have passed what has been read; the bound is then what one
    /// step allocates.
    /// </para>
    /// <para>
    /// It can only tighten the upper end. The least is a maximum over readings, and a reading
    /// more cannot lower it.
    /// </para>
    /// </remarks>
    internal static void Checkpoint() => between?.Invoke();

    private static LivePeak Measure(Func<object> build, Followed followed, bool collectEveryArray)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        var processAtOthers = GC.GetTotalAllocatedBytes(precise: true);
        var threadAtOthers = GC.GetAllocatedBytesForCurrentThread();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var baseline = Survived();

        double least = 0, bound = 0, held = 0, tracked = 0;
        double previous = 0, trackedPrevious = 0;
        long unreported = 0, silent = 0, foreign = 0, burst = 0, pending = 0;
        var collections = 0;
        var threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();

        // What other threads allocated since this was last asked: the process's count less
        // this thread's own.
        void Others()
        {
            var process = GC.GetTotalAllocatedBytes(precise: true);
            var thread = GC.GetAllocatedBytesForCurrentThread();

            var others = Math.Max(0L, (process - processAtOthers) - (thread - threadAtOthers));

            foreign += others;
            burst = Math.Max(burst, others);

            processAtOthers = process;
            threadAtOthers = thread;
        }

        void Read(Array? array, long bytes)
        {
            // Room to follow it, made outside what is charged to the build: the thread's counter
            // is moved past what growing the storage allocated.
            if (followed.Full)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                followed.Grow();
                threadAtPrevious += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            // What the array occupies, header and padding included, which is what the thread's
            // counter charged for it and what it holds in the live set.
            // A reading asked for between two arrays has none of its own.
            var occupies = array is null ? 0L : ArraySize.Of(bytes);

            if (array is not null)
            {
                followed.Add(array, occupies);
            }

            // Everything the thread allocated since the last reading. Of that, `pending` and this
            // array went through GridArrays; the rest, `since`, did not. Exact, so a build that
            // allocates nothing outside GridArrays reports nothing.
            var allocated = GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious;
            var since = Math.Max(0L, allocated - pending - occupies);

            // The most live at any instant from the last reading to now, this array included:
            // the last reading plus everything allocated since; or the grid arrays alive at the
            // last reading, plus those allocated since, plus everything ever allocated outside
            // GridArrays.
            var most = Math.Min(previous + allocated, trackedPrevious + pending + occupies + silent + since);

            // Read only where the live set could pass the least already read. Where it could
            // not, a reading here could not raise the least, which is a maximum of readings each
            // no more than the live set; and `most` covers this instant in the bound. So skipping
            // changes neither end that matters - the least is the same, and the bound is still
            // a bound - and it is proved, not assumed: `most` is an upper bound on the live set.
            if (!collectEveryArray && most <= least)
            {
                bound = Math.Max(bound, most);
                pending += occupies;
                return;
            }

            silent += since;
            unreported = Math.Max(unreported, since);
            bound = Math.Max(bound, Math.Min(previous + allocated - occupies, trackedPrevious + pending + silent));

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            collections++;

            // After the collection: whatever of another thread's is in the reading was allocated
            // by now.
            Others();

            var reading = Survived() - baseline - followed.Own;
            var alive = followed.Alive();

            held = Math.Max(held, reading);
            tracked = Math.Max(tracked, alive);
            least = Math.Max(least, Math.Max(alive, reading - foreign));
            bound = Math.Max(bound, Math.Min(reading, alive + silent));

            previous = reading;
            trackedPrevious = alive;
            pending = 0;

            // Read last, so that what this reading allocated for itself is not charged to the build.
            threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();
        }

        string? invalid;

        GridArrays.StartWatching(Read);
        between = () => Read(null, 0);

        // Counted from here: the watch itself and the delegate it calls are the measurement's, not
        // the build's.
        threadAtPrevious = GC.GetAllocatedBytesForCurrentThread();

        try
        {
            Run(build);
        }
        finally
        {
            between = null;
            invalid = GridArrays.StopWatching();
        }

        // The stretch after the last array, which has no reading of its own.
        var tail = GC.GetAllocatedBytesForCurrentThread() - threadAtPrevious;
        var tailSilent = Math.Max(0L, tail - pending);

        silent += tailSilent;
        unreported = Math.Max(unreported, tailSilent);
        bound = Math.Max(bound, Math.Min(previous + tail, trackedPrevious + pending + silent));
        Others();

        // What the build returned was let go when its frame returned. Whatever of its grid
        // arrays is alive after a collection now is held by something that outlives the build.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

        var retained = followed.Alive();

        return new LivePeak(
            least, Math.Max(bound, least), held, tracked, followed.Count, collections, unreported, silent, foreign, burst,
            retained, invalid);
    }

    /// <summary>Runs the build and lets go of what it returns.</summary>
    /// <remarks>
    /// In a frame of its own, not inlined, so that the result is unreachable when this returns
    /// in every build configuration: unoptimized code keeps a local to the end of its method.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(Func<object> build) => GC.KeepAlive(build());

    /// <summary>The grid arrays a build allocated, each followed by a weak handle.</summary>
    /// <remarks>
    /// <para>
    /// Storage made before the baseline is in it and so in no reading. Growing during the build
    /// replaces storage, and <b>nothing it replaces is dropped</b>: a dropped array the baseline
    /// counted would make every later reading low by its size - 256 kB for the initial room of
    /// 16,384 arrays - and a low reading lowers both the least and the process's half of the
    /// bound. So every array storage has ever had is kept until the measurement ends, and
    /// <see cref="Own"/> is exactly what growing allocated, as the thread's own counter measured
    /// it, all of it still held.
    /// </para>
    /// <para>
    /// <b>Room to keep them is made once and cannot run out.</b> Each growth doubles the room and
    /// keeps the two arrays it replaced, and room for more than <see cref="Array.MaxLength"/>
    /// arrays cannot exist: from the least room there can be, one, that is at most
    /// <see cref="MostGrowths"/> doublings, and <see cref="_kept"/> has two slots for each. Kept
    /// in a fixed array rather than a list, because a list grows by dropping its own storage,
    /// which is the defect this exists to prevent.
    /// </para>
    /// </remarks>
    private sealed class Followed
    {
        /// <summary>The most times room can double: past this it would exceed what an array can hold.</summary>
        internal const int MostGrowths = 31;

        private readonly Array[] _kept = new Array[2 * MostGrowths];
        private GCHandle[] _handles;
        private long[] _sizes;
        private int _keptCount;

        public Followed(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

            _handles = new GCHandle[capacity];
            _sizes = new long[capacity];
        }

        public int Count { get; private set; }

        public bool Full => Count == _handles.Length;

        /// <summary>Bytes allocated by growing after the baseline, all of it still held.</summary>
        public long Own { get; private set; }

        public void Grow()
        {
            // Checked before anything is replaced: doubling past what an array can hold throws here,
            // by name, and the watch records it - never an index past the end of what is kept.
            if (_handles.Length > Array.MaxLength / 2 || _keptCount + 2 > _kept.Length)
            {
                throw new InvalidOperationException(
                    $"a build allocated more than {_handles.Length:N0} grid arrays, and room to follow twice as many "
                    + "cannot be made");
            }

            var before = GC.GetAllocatedBytesForCurrentThread();

            var handles = new GCHandle[2 * _handles.Length];
            var sizes = new long[2 * _sizes.Length];

            Array.Copy(_handles, handles, Count);
            Array.Copy(_sizes, sizes, Count);

            // Kept, not dropped: see the remarks.
            _kept[_keptCount++] = _handles;
            _kept[_keptCount++] = _sizes;

            _handles = handles;
            _sizes = sizes;

            Own += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        public void Add(Array array, long bytes)
        {
            _handles[Count] = GCHandle.Alloc(array, GCHandleType.Weak);
            _sizes[Count] = bytes;
            Count++;
        }

        public double Alive() => LiveSet.Alive(_handles, _sizes, Count);

        public void Free()
        {
            for (var at = 0; at < Count; at++)
            {
                _handles[at].Free();
            }

            GC.KeepAlive(_kept);
        }
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

    /// <summary>What the last full blocking collection left live (<see cref="Einzel.Testing.LiveSetReading"/>).</summary>
    private static long Survived() => Einzel.Testing.LiveSetReading.SurvivedLastFullCollection();
}

/// <summary>What an array of primitive elements occupies on the heap, header and padding included.</summary>
/// <remarks>
/// Measured, not assumed: the thread's allocation counter around an empty array gives what the
/// runtime adds to the elements, and objects are aligned to the pointer size. Checked against
/// arrays of several lengths when first used, so a runtime that lays arrays out differently
/// fails here by name rather than charging every grid array's header to the build as allocated
/// outside GridArrays.
/// </remarks>
internal static class ArraySize
{
    private static readonly long Overhead = Measure();

    /// <summary>The heap size of an array whose elements occupy <paramref name="elementBytes"/>.</summary>
    internal static long Of(long elementBytes) => Align(Overhead + elementBytes);

    private static long Align(long size) => (size + IntPtr.Size - 1) & ~(long)(IntPtr.Size - 1);

    private static long Measure()
    {
        // Once, so that anything a first allocation does is done.
        GC.KeepAlive(new double[1]);

        // A length known only at run time: an empty array really allocated, not Array.Empty.
        var none = Environment.ProcessorCount - Environment.ProcessorCount;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var empty = new double[none];
        var overhead = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(empty);

        foreach (var length in (int[])[1, 3, 7, 1000])
        {
            var at = GC.GetAllocatedBytesForCurrentThread();
            var array = new byte[length];
            var size = GC.GetAllocatedBytesForCurrentThread() - at;

            GC.KeepAlive(array);

            if (size != Align(overhead + length))
            {
                throw new InvalidOperationException(
                    $"a byte array of {length} took {size} bytes, where an overhead of {overhead} aligned to "
                    + $"{IntPtr.Size} gives {Align(overhead + length)}: this runtime lays arrays out differently");
            }
        }

        return overhead;
    }
}
