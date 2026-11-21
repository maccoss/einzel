namespace Einzel.Testing;

/// <summary>
/// What the last full blocking collection left live: the one reading every memory measurement
/// in this repository takes, kept in one place.
/// </summary>
/// <remarks>
/// <para>
/// Linked as source into <c>Einzel.Fields.Tests</c>, <c>Einzel.Cli.Tests</c> and
/// <c>tools/memory-harness</c>, so a correction to how the live set is read is made once. It
/// had three copies, and the first correction it needed - reading the collection's own record
/// rather than <see cref="GC.GetTotalMemory(bool)"/> - was found in one of them.
/// </para>
/// <para>
/// <b>From the collection's own record</b>: the generations' sizes after it, less the free space
/// between their objects. <see cref="GC.GetTotalMemory(bool)"/> read after the collection counts
/// whatever was allocated since as well, and a reader descheduled between the two calls counted
/// a build's next arrays as held.
/// </para>
/// </remarks>
internal static class LiveSetReading
{
    /// <summary>Bytes the last full blocking collection left live, from its own record.</summary>
    /// <returns>The live set after that collection, in bytes.</returns>
    internal static long SurvivedLastFullCollection()
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
