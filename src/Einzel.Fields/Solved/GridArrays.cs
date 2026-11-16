using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Einzel.Fields.Solved;

/// <summary>Told about an array straight after <see cref="GridArrays"/> allocates it.</summary>
/// <param name="array">The array.</param>
/// <param name="bytes">What its elements occupy, in bytes.</param>
internal delegate void GridArrayWatcher(Array array, long bytes);

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
/// <b>Allocating and telling are one call</b>, so that neither can be done without the other,
/// and the size is worked out here from the element type rather than stated by a caller. The
/// fields, masks, cut links and operators get every array from <see cref="New{T}"/> and grow
/// one with <see cref="Resize{T}"/>. <c>GridArraySourceTests</c> parses <c>Einzel.Fields</c>
/// and refuses storage sized at run time and allocated any other way; its remarks say exactly
/// which forms it reads.
/// </para>
/// <para>
/// <b>A watch belongs to the thread that started it, and never makes anything throw.</b> The
/// watcher runs on the allocating thread, and what it does there - force a collection, read the
/// live set and that thread's allocation counter - measures the build only if the build is on
/// that thread. An array allocated here on any other thread while a watch is on is therefore not
/// shown to the watcher; it is recorded, and <see cref="StopWatching"/> hands the record back to
/// the measurement, which fails by name. Code that allocates goes on exactly as it would have.
/// </para>
/// <para>
/// Nothing is watching outside the memory tests, which watch only in a child process started
/// for the purpose, and then an allocation here costs one read of a static field and a branch -
/// per array, never per node - and allocates nothing but the array.
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
    internal static T[] New<T>(long length)
        where T : unmanaged
    {
        var array = new T[length];

        watch?.Allocated(array, Unsafe.SizeOf<T>() * length);

        return array;
    }

    /// <summary>
    /// Changes an array's length, as <see cref="Array.Resize{T}(ref T[], int)"/> does: a null
    /// array becomes a new one of that length, an array already that long is left as it is, and
    /// otherwise the leading elements are copied into a new array.
    /// </summary>
    /// <remarks>
    /// A new array is allocated through <see cref="New{T}"/> while the old one is still held
    /// beside it, which is the most the owner ever holds for it.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The array, replaced unless its length is already right.</param>
    /// <param name="length">The new length.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    internal static void Resize<T>([NotNull] ref T[]? array, int length)
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (array is null)
        {
            array = New<T>(length);
            return;
        }

        if (array.Length == length)
        {
            return;
        }

        var resized = New<T>(length);

        Array.Copy(array, resized, Math.Min(array.Length, length));

        array = resized;
    }

    /// <summary>Starts telling <paramref name="watcher"/> about every array allocated here on this thread.</summary>
    /// <param name="watcher">Called with each array, on this thread, straight after it is allocated.</param>
    /// <exception cref="InvalidOperationException">A watch is already on.</exception>
    internal static void StartWatching(GridArrayWatcher watcher)
    {
        var started = new Watch(Environment.CurrentManagedThreadId, watcher);

        if (Interlocked.CompareExchange(ref watch, started, null) is not null)
        {
            throw new InvalidOperationException("grid arrays are already being watched in this process");
        }
    }

    /// <summary>Stops the watch.</summary>
    /// <returns>
    /// What was allocated here on other threads while it was on, or null if nothing was: such
    /// arrays were not shown to the watcher, so whatever it measured does not include them.
    /// </returns>
    internal static string? StopWatching()
    {
        var stopped = Interlocked.Exchange(ref watch, null);

        return stopped?.OffThread;
    }

    private sealed class Watch(int thread, GridArrayWatcher watcher)
    {
        private long _offThreadArrays;
        private long _offThreadBytes;
        private int _firstThread;

        public string? OffThread => Interlocked.Read(ref _offThreadArrays) == 0
            ? null
            : $"{Interlocked.Read(ref _offThreadArrays):N0} grid array(s), {Interlocked.Read(ref _offThreadBytes):N0} bytes, "
                + $"were allocated on thread {Volatile.Read(ref _firstThread)} (and perhaps others) while thread {thread} "
                + "was measuring what a build holds. The live set is read on the measuring thread at its own allocations, "
                + "so arrays a solve allocates on a worker are neither read at nor bounded: allocate them on the calling "
                + "thread, or make the measurement follow the workers.";

        public void Allocated(Array array, long bytes)
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                watcher(array, bytes);
                return;
            }

            Interlocked.CompareExchange(ref _firstThread, Environment.CurrentManagedThreadId, 0);
            Interlocked.Add(ref _offThreadBytes, bytes);
            Interlocked.Increment(ref _offThreadArrays);
        }
    }
}
