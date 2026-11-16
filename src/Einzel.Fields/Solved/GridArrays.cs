namespace Einzel.Fields.Solved;

/// <summary>
/// The one place a solve allocates an array the size of a grid, so that a test can read what
/// the solve holds at that instant from inside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a build holds can only rise at an allocation</b>, so the most it ever holds is held
/// immediately after one. A test that reads the live set at every per-node allocation therefore
/// reads the peak itself, on the thread that made it, whatever the machine is doing - where a
/// second thread sampling the heap has to land inside a window that on a small grid is a
/// millisecond wide, and on a loaded CI runner did not (<c>docs/lessons.md</c>, "The most a
/// build holds is held just after an allocation").
/// </para>
/// <para>
/// <b>Allocating and reporting are one call</b>, so that neither can be done without the other:
/// the fields, masks, cut links and operators here get every array from <see cref="New{T}"/>
/// and grow one with <see cref="Resize{T}"/>, and a per-node array allocated any other way in
/// this directory fails <c>GridArraySourceTests</c>, which reads the source. There is no report
/// to forget, to make twice, or to make with the wrong size.
/// </para>
/// <para>
/// <b>A watch is the process's, and belongs to one thread.</b> The watcher is called on the
/// thread that allocates, and what it does there - force a collection, read the live set, read
/// that thread's allocation counter - is only a measurement of the build if the build is on
/// that thread. So an array allocated here on any other thread while a watch is on is refused,
/// by name: a solver that starts allocating its arrays on workers stops being measurable this
/// way, and has to say so rather than read low.
/// </para>
/// <para>
/// Nothing is watching outside the memory tests, and then an allocation here costs one read of
/// a static field and a branch - per array, never per node - and allocates nothing more than
/// the array.
/// </para>
/// </remarks>
internal static class GridArrays
{
    private static volatile Watch? watch;

    /// <summary>Whether a watch is on, in this process.</summary>
    internal static bool Watched => watch is not null;

    /// <summary>Allocates a zeroed array, and tells whoever is watching.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="length">How many elements.</param>
    /// <returns>The array.</returns>
    /// <exception cref="InvalidOperationException">
    /// A watch is on and this is not the thread that started it.
    /// </exception>
    internal static T[] New<T>(long length)
        where T : unmanaged
    {
        var array = new T[length];

        watch?.Allocated(array);

        return array;
    }

    /// <summary>
    /// Moves an array's leading elements into a new one of another length, as
    /// <see cref="Array.Resize{T}(ref T[], int)"/> does.
    /// </summary>
    /// <remarks>
    /// The new array is reported while the old one is still held beside it, which is the most
    /// the owner ever holds for it.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The array, replaced.</param>
    /// <param name="length">The new length.</param>
    internal static void Resize<T>(ref T[] array, int length)
        where T : unmanaged
    {
        var resized = New<T>(length);

        Array.Copy(array, resized, Math.Min(array.Length, length));

        array = resized;
    }

    /// <summary>Starts telling <paramref name="watcher"/> about every array allocated here.</summary>
    /// <param name="watcher">Called with each array, on the calling thread, straight after it is allocated.</param>
    /// <exception cref="InvalidOperationException">A watch is already on.</exception>
    internal static void StartWatching(Action<Array> watcher)
    {
        var started = new Watch(Environment.CurrentManagedThreadId, watcher);

        if (Interlocked.CompareExchange(ref watch, started, null) is not null)
        {
            throw new InvalidOperationException("grid arrays are already being watched in this process");
        }
    }

    /// <summary>Stops the watch.</summary>
    internal static void StopWatching() => watch = null;

    private sealed class Watch(int thread, Action<Array> watcher)
    {
        public void Allocated(Array array)
        {
            if (Environment.CurrentManagedThreadId != thread)
            {
                throw new InvalidOperationException(
                    $"a grid array of {Buffer.ByteLength(array):N0} bytes was allocated on thread "
                    + $"{Environment.CurrentManagedThreadId} while thread {thread} is measuring what a build holds: "
                    + "the live set is read on the measuring thread at its own allocations, so arrays a solve "
                    + "allocates on a worker are neither read at nor bounded. Allocate them on the calling thread, "
                    + "or make the measurement follow the workers.");
            }

            watcher(array);
        }
    }
}
