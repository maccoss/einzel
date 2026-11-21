using Einzel.Testing;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The room a measurement makes to follow a build's arrays may be outgrown, and what is read
/// after it has grown is still the truth.
/// </summary>
/// <remarks>
/// Measured in the child, where the process's own reading is the build's alone; beside
/// <c>GridArraysTests</c>, which holds what is tested in this process.
/// </remarks>
[Collection(nameof(MeasuredInAChild))]
public sealed class FollowedRoomTests(MeasuredBuilds builds, ITestOutputHelper output)
{
    /// <summary>
    /// A build that allocates more grid arrays than the measurement made room for is measured
    /// all the same: the storage for following them grows, outside what is charged to the build.
    /// </summary>
    /// <remarks>
    /// Measured in the child, where nothing else allocates, because what is compared is the
    /// process's own reading: in this test host other threads move it by more than the storage
    /// is worth.
    /// </remarks>
    [Fact]
    public void MoreArraysThanTheRoomMadeForThemAreMeasuredAllTheSame()
    {
        var roomy = builds.Optimized(output, "fields", LiveSet.InitialCapacity).Peak;
        var cramped = builds.Optimized(output, "fields", 1).Peak;

        Assert.Equal(40, cramped.Readings);
        Assert.Equal(40, cramped.Collections);
        Assert.Equal(roomy.Tracked, cramped.Tracked);

        // Grown six times to 64, the storage holds 1,072 bytes the build did not.
        Assert.InRange(cramped.Held, roomy.Held - 256, roomy.Held + 256);
        Assert.InRange(cramped.Bound, roomy.Bound - 256, roomy.Bound + 256);
    }

    /// <summary>
    /// A build that outgrows the room made for following its arrays, at the room's real size, is
    /// read at its true peak to within a few kilobytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The room made before the baseline is 16,384 arrays, 256 kB of storage. Growing it once
    /// replaces that storage; dropping what it replaced would take 256 kB the baseline counted out
    /// of every later reading, and the reading at this build's peak - taken after the growth - would
    /// be that much low. Kept, the reading is the truth.
    /// </para>
    /// <para>
    /// The build holds 8,000,000 bytes, holds and lets go another 8,000,000, allocates 20,000
    /// one-byte arrays it drops, then holds 8,800,000 more: its peak is the first and last together,
    /// and in the child, where nothing else allocates, a reading of it is exact to the few objects
    /// it keeps them in.
    /// </para>
    /// </remarks>
    [Fact]
    public void AReadingAfterTheRoomHasGrownIsTheTruth()
    {
        var peak = builds.Optimized(output, "grow-drop", 1).Peak;
        var truth = (double)(ArraySize.Of(8L * 1_000_000) + ArraySize.Of(8L * 1_100_000));

        // The drop let most of the small arrays be passed over; otherwise this would read twenty
        // thousand times.
        Assert.True(peak.Collections < 100, $"{peak.Collections} readings were taken");

        output.WriteLine($"truth {truth:N0}: {SolveMemoryTests.Describe(peak, 1)}");

        Assert.Equal(20_003, peak.Readings);
        Assert.InRange(peak.Held, truth, truth + 4096);
        Assert.InRange(peak.Least, truth - 4096, truth + 4096);
        Assert.InRange(peak.Bound, truth, truth + 4096);
    }

    /// <summary>
    /// A solve measured four times in one process holds the same each time and leaves nothing
    /// behind: nothing is kept from one solve into the next.
    /// </summary>
    [Theory]
    [InlineData("sphere", 0.000625)]
    [InlineData("rods", 0.0001)]
    public void ARepeatedSolveLeavesNothingBehind(string kind, double size)
    {
        var runs = builds.Optimized(output, kind, size, times: 4).Every;

        output.WriteLine(string.Join("; ", runs.Select(r => $"{r.Least:N0} to {r.Bound:N0}, {r.Retained:N0} left")));

        MemoryVerdict.Repeatable($"the {kind} build", runs);
    }
}
