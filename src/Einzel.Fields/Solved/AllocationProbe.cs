using System.Runtime.CompilerServices;

namespace Einzel.Fields.Solved;

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
/// <b>The rule a call site follows</b>: report straight after the allocation, with its size,
/// and before anything it replaces is let go. The observer is told the bytes so that it can
/// account for what the thread allocated <i>without</i> saying so, which bounds how far above
/// the largest reading the true peak can be; an allocation made here with no report shows up
/// there, as slack, and fails the memory tests by name.
/// </para>
/// <para>
/// Nothing is observing outside those tests, and then a report is one thread-static read and a
/// branch per array - per allocation, never per node - and allocates nothing. Per thread, so a
/// test observing its own solve is not told about another's.
/// </para>
/// </remarks>
internal static class AllocationProbe
{
    [ThreadStatic]
    private static Action<long>? observer;

    /// <summary>The observer of this thread's reports, or null for none.</summary>
    internal static Action<long>? Observer
    {
        get => observer;
        set => observer = value;
    }

    /// <summary>Reports that this thread has just allocated arrays of this many bytes.</summary>
    /// <param name="bytes">The arrays' elements, in bytes.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Allocated(long bytes) => observer?.Invoke(bytes);
}
