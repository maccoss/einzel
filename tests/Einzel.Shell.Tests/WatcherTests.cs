using Einzel.Commands;
using Einzel.Core.Results;
using Einzel.Shell;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// What a watched run hands the viewport, and which frame it is left showing.
/// </summary>
public sealed class WatcherTests(ITestOutputHelper output)
{
    /// <summary>
    /// The cadence is the wall clock, so a frame is wanted at most once per interval
    /// however fast the steps come.
    /// </summary>
    /// <remarks>
    /// A diffusive step is a stability limit rather than a unit of time, so "every
    /// thousandth step" is a different cadence on every model and on every mesh. Asking the
    /// clock gives one cadence for all of them - and this is the only knob that decides
    /// which steps are looked at, which is what keeps watching from changing the answer.
    /// </remarks>
    [Fact]
    public void AFrameIsWantedNoOftenerThanTheInterval()
    {
        var watcher = new Watcher(_ => { }, TimeSpan.FromMilliseconds(120));

        var immediately = 0;

        // A thousand steps in a tight loop: on any machine these land inside one interval.
        for (var step = 0; step < 1000; step++)
        {
            if (watcher.Wants(step, step * 1e-9))
            {
                immediately++;
            }
        }

        output.WriteLine($"{immediately} frames wanted out of 1000 steps taken at once");

        // The first is wanted, because a watch that showed nothing until the first interval
        // had passed would open on an empty box.
        Assert.Equal(1, immediately);

        Thread.Sleep(150);

        Assert.True(watcher.Wants(1000, 1e-6), "no frame was wanted after the interval passed");
    }

    /// <summary>
    /// A finished run is left showing the last frame that had a packet in it, not the last
    /// frame.
    /// </summary>
    /// <remarks>
    /// <b>The end of a run is empty whenever the ions arrived.</b> Applying the final frame
    /// unconditionally spends the whole run drawing a packet and then replaces it with an
    /// empty box - the exact picture the density work exists to stop a diffusive model
    /// producing. The control is the first assertion: a run that still has a packet at the
    /// end is left showing that, so this is not "always show the second to last".
    /// </remarks>
    [Fact]
    public void AFinishedRunKeepsTheLastFrameThatHeldAPacket()
    {
        var shown = new List<ViewportOutcome>();
        var watcher = new Watcher(shown.Add, TimeSpan.Zero);

        watcher.Reached(Frame(shells: 3));
        watcher.Reached(Frame(shells: 5));

        Assert.Equal(5, watcher.LastWithPacket!.Density.Count);

        // And then the ions arrive and the box empties.
        watcher.Reached(Frame(shells: 0));

        output.WriteLine($"{shown.Count} frames shown, last with a packet had "
            + $"{watcher.LastWithPacket!.Density.Count} contours");

        Assert.Equal(3, shown.Count);
        Assert.Equal(5, watcher.LastWithPacket!.Density.Count);
    }

    /// <summary>Every frame is handed on, including the empty ones.</summary>
    /// <remarks>
    /// Keeping the last frame with a packet is about what the viewport is LEFT showing when
    /// the run ends. While it is running, an emptying box is the instrument doing its job
    /// and hiding it would be hiding the result.
    /// </remarks>
    [Fact]
    public void EveryFrameIsHandedOnWhileTheRunIsGoing()
    {
        var shown = new List<int>();
        var watcher = new Watcher(f => shown.Add(f.Density.Count), TimeSpan.Zero);

        foreach (var shells in new[] { 4, 2, 0 })
        {
            watcher.Reached(Frame(shells));
        }

        Assert.Equal([4, 2, 0], shown);
    }

    private static ViewportOutcome Frame(int shells) =>
        new(
            ModelPath: "probe.json",
            Trajectories: [],
            ProducesTrajectories: false,
            LowestEnergyEv: null,
            HighestEnergyEv: null,
            Conductors: [],
            Equipotentials: [],
            LowestPotentialVolts: null,
            HighestPotentialVolts: null,
            Density: [.. Enumerable.Range(0, shells).Select(d =>
                new DensityShell(1e10 / Math.Pow(10, d), d, [], [], []))],
            PeakDensityPerCubicMetre: shells > 0 ? 1e10 : null,
            DensityAtUs: 1.0,
            Ends: null,
            Warnings: Array.Empty<ValidityWarning>());
}
