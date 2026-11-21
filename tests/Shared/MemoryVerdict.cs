namespace Einzel.Testing;

/// <summary>Whether a measured figure lies inside its edges, said three ways.</summary>
/// <remarks>
/// A measurement is an interval. An edge it is wholly past is a finding about the solver; an
/// edge it straddles is a finding about the measurement. Which of those it is turns on whether
/// other threads allocated enough to account for the excess: if they did, the test cannot tell
/// and says so by name, with the numbers, rather than reporting the solver.
/// </remarks>
internal static class MemoryVerdict
{
    /// <summary>The message a disturbed measurement fails with.</summary>
    internal const string Disturbed = LiveVerdict.Disturbed;

    /// <summary>Asserts a figure in bytes a node lies between two edges.</summary>
    /// <param name="what">What the figure is, for the message.</param>
    /// <param name="lower">The least it can be.</param>
    /// <param name="upper">The most it can be.</param>
    /// <param name="least">The lower edge.</param>
    /// <param name="most">The upper edge.</param>
    /// <param name="nodes">The nodes the figure is per.</param>
    /// <param name="foreign">Bytes other threads allocated during the measurements it rests on.</param>
    internal static void Within(string what, double lower, double upper, double least, double most, double nodes, long foreign)
    {
        if (LiveVerdict.Against(what, lower, upper, least, most, nodes, foreign) is { } failure)
        {
            Assert.Fail(failure);
        }
    }

    /// <summary>
    /// Fails a measurement during which another thread allocated, between two occasions to read, more than
    /// anything but a grid array would come to (<see cref="LiveVerdict.NotItsOwn"/>).
    /// </summary>
    internal static void TheBuildsOwn(string name, LivePeak peak)
    {
        if (LiveVerdict.NotItsOwn(name, peak) is { } failure)
        {
            Assert.Fail(failure);
        }
    }

    /// <summary>
    /// Asserts that a build measured several times in one process held the same each time and
    /// left nothing behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What this is for is something kept from one run into the next: a pool, a cache, a static
    /// list that grows by the run. Each measurement takes its own baseline, so an array a run
    /// leaves behind is in the next run's baseline and no single reading shows it. Two things
    /// do. <see cref="LivePeak.Retained"/> is the build's own grid arrays still alive after the
    /// build has returned and its result has been let go, which is nothing unless something
    /// outlives the run. And the build's arrays alive at its peak are the same number of bytes
    /// every time, exactly - read at every array, which a repeated case is, rather than at the
    /// readings the bound calls for, whose choice other threads move.
    /// </para>
    /// <para>
    /// <b>And the least is the same every time, exactly.</b> The measurement is run once on
    /// nothing before the first build is measured, so what its own first use keeps is in every
    /// baseline. Without that the first reading was 32 bytes over the rest here and 152 on a
    /// Linux runner, inside a 56-byte interval here and outside it there - so agreement to
    /// within the interval's width passed locally what failed on CI.
    /// </para>
    /// </remarks>
    internal static void Repeatable(string what, IReadOnlyList<LivePeak> runs)
    {
        Assert.True(runs.Count >= 3, $"{what} was measured {runs.Count} time(s), which is not a repetition");

        for (var run = 0; run < runs.Count; run++)
        {
            Assert.True(
                runs[run].Retained == 0.0,
                $"{what}: run {run + 1} of {runs.Count} left {runs[run].Retained:N0} bytes of its grid arrays alive after it "
                + "returned and its result was let go - something that outlives a run is keeping them, and grows by the run");

            Assert.True(
                runs[run].Tracked == runs[0].Tracked,
                $"{what}: run {run + 1} held {runs[run].Tracked:N0} bytes of grid arrays at its most and the first "
                + $"{runs[0].Tracked:N0}: repetitions of one build held different arrays");

            Assert.True(
                runs[run].Least == runs[0].Least,
                $"{what}: run {run + 1} read at least {runs[run].Least:N0} bytes and the first {runs[0].Least:N0}: "
                + "repetitions of one build read differently");
        }
    }

    /// <summary>Fails a measurement that does not cover the build, by name.</summary>
    internal static void Covered(string what, LivePeak peak)
    {
        if (LiveVerdict.NotCovered(what, peak) is { } failure)
        {
            Assert.Fail(failure);
        }
    }
}
