namespace Einzel.Fields.Solved;

/// <summary>What <see cref="AllocationProbe"/> tells its observer: the arrays just allocated.</summary>
/// <param name="arrays">The arrays, each allocated since the last report on this thread.</param>
internal delegate void AllocationObserver(ReadOnlySpan<Array> arrays);

/// <summary>
/// Where a solve says it has just allocated something the size of a grid, so that a test can
/// read what the solve holds at that instant from inside it.
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
/// <b>The rule a call site follows</b>: report straight after the allocation, the arrays
/// themselves, and before anything they replace is let go. The arrays and not a byte count, so
/// that a site cannot state a size: the observer works out what they hold, compares it with
/// what the thread's own counter says was allocated, and treats the rest as allocated without
/// a report - which bounds how far above the largest reading the true peak can be. An
/// allocation made with no report shows up there, as slack, and fails the memory tests by
/// name; so does an array reported twice, or one not allocated since the last report.
/// </para>
/// <para>
/// <b>Per thread, which is a limit.</b> An observer is told about allocations on its own thread
/// and no other, so a test observing its own solve is not told about another's - and so an
/// array a solve allocated on a worker thread would be neither read at nor bounded. The solvers
/// allocate on the thread that calls them. The memory tests count what other threads allocate
/// and fail by name if it is a large part of what a build held, which is what a solver that
/// moved its arrays to workers would look like; the observer would then have to follow it.
/// </para>
/// <para>
/// Nothing is observing outside those tests, and then a report is one thread-static read and a
/// branch per call - per allocation, never per node - and allocates nothing.
/// </para>
/// </remarks>
internal static class AllocationProbe
{
    [ThreadStatic]
    private static AllocationObserver? observer;

    /// <summary>The observer of this thread's reports, or null for none.</summary>
    internal static AllocationObserver? Observer
    {
        get => observer;
        set => observer = value;
    }

    /// <summary>Reports that this thread has just allocated an array.</summary>
    internal static void Allocated(Array array)
    {
        if (observer is { } watching)
        {
            watching([array]);
        }
    }

    /// <summary>Reports that this thread has just allocated two arrays.</summary>
    internal static void Allocated(Array first, Array second)
    {
        if (observer is { } watching)
        {
            watching([first, second]);
        }
    }

    /// <summary>Reports that this thread has just allocated three arrays.</summary>
    internal static void Allocated(Array first, Array second, Array third)
    {
        if (observer is { } watching)
        {
            watching([first, second, third]);
        }
    }

    /// <summary>Reports that this thread has just allocated four arrays.</summary>
    internal static void Allocated(Array first, Array second, Array third, Array fourth)
    {
        if (observer is { } watching)
        {
            watching([first, second, third, fourth]);
        }
    }
}
