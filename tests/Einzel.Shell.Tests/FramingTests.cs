using Einzel.Commands;
using Einzel.Core.Results;
using Einzel.Shell;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>Where the camera goes, and what it is told to hold.</summary>
public sealed class FramingTests(ITestOutputHelper output)
{
    /// <summary>
    /// The flight is measured as well as the metal, so a reflectron's turning point is on
    /// the page.
    /// </summary>
    /// <remarks>
    /// <b>This is a defect the vector renderer had for as long as it had sections.</b> A
    /// model with no declared solve domain took its extent from the instrument's own points,
    /// and in a reflectron the source and the detector are the SAME POINT - the ion is caught
    /// where it launched - so the page was a tenth of a millimetre around a flight of 1.3 m
    /// and the turning point was drawn a hundred metres off it. The control is the second
    /// assertion: conductors alone would frame this scene half as wide.
    /// </remarks>
    [Fact]
    public void TheFlightIsInsideTheFrameAndNotOnlyTheMetal()
    {
        // A conductor 10 mm across, and an ion that goes out to 100 mm and comes back.
        var scene = Scene(
            conductors: [Box(-5, 5)],
            path: [[0, 0, 0], [100, 0, 0], [0, 0, 0]]);

        var withFlight = Framing.Measure(scene, 0.0, 0.0);
        var metalOnly = Framing.Measure(Scene([Box(-5, 5)], null), 0.0, 0.0);

        output.WriteLine($"radius with the flight {withFlight.RadiusMm:F3} mm, "
            + $"metal alone {metalOnly.RadiusMm:F3} mm");

        Assert.True(
            withFlight.RadiusMm > 45.0,
            $"the frame is {withFlight.RadiusMm:F3} mm across a flight that reaches 100 mm");

        Assert.True(withFlight.RadiusMm > 5.0 * metalOnly.RadiusMm);
    }

    /// <summary>A scene with nothing in it frames without dividing by zero.</summary>
    /// <remarks>
    /// A diffusive model before its first frame has no conductors and no paths, and a
    /// viewport that threw there would fail exactly when somebody opened a model to watch.
    /// </remarks>
    [Fact]
    public void AnEmptySceneStillFrames()
    {
        var framing = Framing.Measure(Scene([], null), 0.0, 0.0);

        Assert.True(framing.RadiusMm > 0.0);
        Assert.All(framing.Project(1.5), v => Assert.True(float.IsFinite(v)));
    }

    /// <summary>
    /// Everything measured lands inside the clip box, at any aspect and from any angle.
    /// </summary>
    /// <remarks>
    /// The assertion that would pass on a broken projection is "the matrix is finite". What
    /// discriminates is carrying the scene's own corners through it and asking whether they
    /// are on the screen - a camera turned to an angle that sends half the instrument out of
    /// frame is precisely the failure a viewport must not have, and the named views turn it
    /// to several.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 0.0, 1.0)]
    [InlineData(-32.0, 24.0, 1.6)]
    [InlineData(90.0, 0.0, 0.7)]
    [InlineData(0.0, 90.0, 2.4)]
    public void TheWholeInstrumentIsInsideTheClipBox(double azimuth, double elevation, double aspect)
    {
        var scene = Scene([Box(-20, 30)], [[0, 0, 0], [25, 8, -8]]);
        var framing = Framing.Measure(scene, azimuth, elevation);
        var m = framing.Project(aspect);

        var worst = 0.0;

        foreach (var x in new[] { -20.0, 30.0 })
        {
            foreach (var y in new[] { -20.0, 30.0 })
            {
                foreach (var z in new[] { -20.0, 30.0 })
                {
                    // Column-major, so element [column * 4 + row].
                    var cx = (m[0] * x) + (m[4] * y) + (m[8] * z) + m[12];
                    var cy = (m[1] * x) + (m[5] * y) + (m[9] * z) + m[13];
                    var cz = (m[2] * x) + (m[6] * y) + (m[10] * z) + m[14];

                    worst = Math.Max(worst, Math.Max(Math.Abs(cx), Math.Max(Math.Abs(cy), Math.Abs(cz))));
                }
            }
        }

        output.WriteLine($"azimuth {azimuth}, elevation {elevation}, aspect {aspect}: "
            + $"worst corner at {worst:F4} of the clip box");

        Assert.True(worst <= 1.0, $"a corner of the instrument lands at {worst:F4}, outside the clip box");
    }

    /// <summary>
    /// A diffusive model with no electrodes is framed by its packet, not by a millimetre at
    /// the origin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The case this misses is the one the watch exists for.</b> RND-8 means a diffusive
    /// model produces no trajectories, and it need not declare electrodes either - the drift
    /// tube these tests use has a uniform analytic field and no metal at all. Measured from
    /// conductors and paths alone that scene is empty, the framing falls back to a
    /// millimetre, and a packet spanning forty is drawn entirely outside the frustum.
    /// </para>
    /// <para>
    /// The control is the second assertion: the same scene WITHOUT the density still falls
    /// back, so this is the density being measured rather than the fallback having changed.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADensityWithNoMetalAroundItIsStillFramed()
    {
        var packet = Scene([], null) with { Density = [Shell(2.0, 40.0)] };

        var framing = Framing.Measure(packet, 0.0, 0.0);
        var nothing = Framing.Measure(Scene([], null), 0.0, 0.0);

        output.WriteLine($"radius with the packet {framing.RadiusMm:F3} mm, "
            + $"with neither metal nor packet {nothing.RadiusMm:F3} mm");

        Assert.True(
            framing.RadiusMm > 15.0,
            $"a packet spanning 38 mm was framed at {framing.RadiusMm:F3} mm");

        Assert.Equal(1.0, nothing.RadiusMm, 12);

        // And it is centred on the packet rather than on the origin it was launched from.
        Assert.Equal(21.0, framing.CenterMm.X, 6);
    }

    /// <summary>
    /// A frame only grows, so a packet that drifts out of its opening box is still drawn.
    /// </summary>
    /// <remarks>
    /// A TIMS elution is seeded near the entrance and elutes forty millimetres away. Framing
    /// each arriving frame on its own would keep the packet centred and make the camera
    /// breathe with it; framing once loses it. The union does neither.
    /// </remarks>
    [Fact]
    public void TheFrameGrowsWithThePacketAndNeverShrinks()
    {
        var opening = Framing.Measure(
            Scene([], null) with { Density = [Shell(1.0, 3.0)] }, 0.0, 0.0);

        var later = Framing.Measure(
            Scene([], null) with { Density = [Shell(38.0, 42.0)] }, 0.0, 0.0);

        var grown = opening.Union(later);

        output.WriteLine($"opening {opening.RadiusMm:F3} mm at {opening.CenterMm.X:F2}, "
            + $"later {later.RadiusMm:F3} at {later.CenterMm.X:F2}, "
            + $"grown {grown.RadiusMm:F3} at {grown.CenterMm.X:F2}");

        // Both instants are inside the grown frame, which is what "never loses the packet"
        // means - asserted as containment rather than as a radius, so it cannot pass by
        // being merely large.
        Assert.True(Holds(grown, opening), "the grown frame dropped where the packet started");
        Assert.True(Holds(grown, later), "the grown frame dropped where the packet ended");

        // And it does not shrink back when a later frame is smaller: a packet collected at
        // the detector leaves almost nothing, and the camera must not snap onto the remnant.
        Assert.True(Holds(grown.Union(opening), later));

        // Taking in something already inside changes nothing at all, so repeated application
        // cannot drift the frame outward over a run of thousands of steps.
        var settled = grown.Union(opening).Union(later).Union(opening);

        Assert.Equal(grown.RadiusMm, settled.RadiusMm, 12);
        Assert.Equal(grown.CenterMm.X, settled.CenterMm.X, 12);
    }

    private static bool Holds(Framing outer, Framing inner)
    {
        var (ax, ay, az) = outer.CenterMm;
        var (bx, by, bz) = inner.CenterMm;

        double dx = bx - ax, dy = by - ay, dz = bz - az;
        var apart = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        return apart + inner.RadiusMm <= outer.RadiusMm + 1e-9;
    }

    private static DensityShell Shell(double low, double high) =>
        new(
            DensityPerCubicMetre: 1e10,
            DecadesBelowPeak: 0,
            VerticesMm: [low, -1.0, -1.0, high, -1.0, -1.0, high, 1.0, 1.0, low, 1.0, 1.0],
            Normals: [.. Enumerable.Repeat(0.0, 12)],
            Triangles: [0, 1, 2, 0, 2, 3]);

    private static ConductorSurface Box(double low, double high) =>
        new(
            Name: "box",
            PotentialVolts: 0.0,
            DriveAmplitudeVolts: 0.0,
            VerticesMm:
            [
                low, low, low, high, low, low, high, high, low, low, high, low,
                low, low, high, high, low, high, high, high, high, low, high, high,
            ],
            Normals: [.. Enumerable.Repeat(0.0, 24)],
            Triangles: [0, 1, 2, 0, 2, 3]);

    private static ViewportOutcome Scene(
        IReadOnlyList<ConductorSurface> conductors, IReadOnlyList<double[]>? path) =>
        new(
            ModelPath: "probe.json",
            Trajectories: path is null
                ? []
                : [new TrajectoryPath([.. path], [.. path.Select(_ => 1.0)], "StopConditionMet")],
            ProducesTrajectories: path is not null,
            LowestEnergyEv: 1.0,
            HighestEnergyEv: 1.0,
            Conductors: conductors,
            Equipotentials: [],
            LowestPotentialVolts: null,
            HighestPotentialVolts: null,
            Density: [],
            PeakDensityPerCubicMetre: null,
            DensityAtUs: null,
            Ends: null,
            Warnings: Array.Empty<ValidityWarning>());
}
