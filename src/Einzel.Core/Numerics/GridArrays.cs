using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Einzel.Core.Numerics;

/// <summary>Told about an array straight after <see cref="GridArrays"/> allocates it.</summary>
/// <param name="array">The array.</param>
/// <param name="bytes">What its elements occupy, in bytes.</param>
internal delegate void GridArrayWatcher(Array array, long bytes);

/// <summary>
/// The one place a solve or a density run allocates an array the size of a grid, so that a test
/// can read what it holds at that instant from inside it.
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
/// one with <see cref="Resize{T}"/>; so do the densities, the step's coefficients and face
/// operator, the self-field and the well cache of a diffusive run. <c>GridArraySourceTests</c>
/// parses <c>Einzel.Fields</c> and <c>Einzel.Transport</c> and refuses storage the size of a
/// grid allocated any other way; its remarks say exactly what it can and cannot show.
/// </para>
/// <para>
/// <b>Allocating is public and watching is not.</b> The arrays are allocated in
/// <c>Einzel.Fields</c>, <c>Einzel.Transport</c>, <c>Einzel.Io</c> and <c>Einzel.Commands</c>,
/// and <c>Einzel.Core</c> is the one assembly all of them already reference, so the allocator
/// is here and its two methods are public: no assembly is given sight of another's internals
/// to reach it. Starting and stopping a watch stay internal, visible to the test assemblies
/// that measure and to the memory harness and to nothing that ships - an allocation hook is
/// not an extension point.
/// </para>
/// <para>
/// Nothing is watching outside the memory tests, which watch only in a child process started
/// for the purpose, and then an allocation here costs one read of a static field and a branch -
/// per array, never per node - and allocates nothing but the array.
/// </para>
/// </remarks>
public static class GridArrays
{
    private static volatile Watch? watch;

    /// <summary>Whether a watch is on, in this process.</summary>
    internal static bool Watched => watch is not null;

    /// <summary>Allocates a zeroed array the size of a grid, and tells whoever is watching.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="length">How many elements.</param>
    /// <returns>The array.</returns>
    /// <exception cref="OverflowException"><paramref name="length"/> is negative or more than an array can hold.</exception>
    public static T[] New<T>(long length)
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
    public static void Resize<T>([NotNull] ref T[]? array, int length)
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
    /// Why what the watcher measured does not cover what was allocated - arrays allocated on
    /// other threads, which it was not shown, or an exception from the watcher itself - or null.
    /// </returns>
    internal static string? StopWatching()
    {
        var stopped = Interlocked.Exchange(ref watch, null);

        return stopped?.Invalid;
    }

    private sealed class Watch(int thread, GridArrayWatcher watcher)
    {
        private long _offThreadArrays;
        private long _offThreadBytes;
        private int _firstThread;

        private string? _watcherFailed;

        public string? Invalid => (OffThread, Volatile.Read(ref _watcherFailed)) switch
        {
            (null, null) => null,
            (var offThread, null) => offThread,
            (null, var failed) => $"the watcher failed, so the measurement stopped there: {failed}",
            (var offThread, var failed) => $"{offThread} And the watcher failed: {failed}",
        };

        private string? OffThread => Interlocked.Read(ref _offThreadArrays) == 0
            ? null
            : Invariant(Interlocked.Read(ref _offThreadArrays)) + " grid array(s), "
                + Invariant(Interlocked.Read(ref _offThreadBytes)) + " bytes, were allocated on thread "
                + Invariant(Volatile.Read(ref _firstThread)) + " (and perhaps others) while thread " + Invariant(thread)
                + " was measuring what a build holds. The live set is read on the measuring thread at its own allocations, "
                + "so arrays a solve allocates on a worker are neither read at nor bounded: allocate them on the calling "
                + "thread, or make the measurement follow the workers.";

        private static string Invariant(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        public void Allocated(Array array, long bytes)
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                if (Volatile.Read(ref _watcherFailed) is not null)
                {
                    return;
                }

                // Whatever a test does at an allocation, the solver's allocation must not fail
                // because of it: recorded, and reported when the watch stops.
#pragma warning disable CA1031 // Any exception: the point is that none reaches the allocating code.
                try
                {
                    watcher(array, bytes);
                }
                catch (Exception failed)
                {
                    Volatile.Write(ref _watcherFailed, failed.ToString());
                }
#pragma warning restore CA1031

                return;
            }

            Interlocked.CompareExchange(ref _firstThread, Environment.CurrentManagedThreadId, 0);
            Interlocked.Add(ref _offThreadBytes, bytes);
            Interlocked.Increment(ref _offThreadArrays);
        }
    }
}
