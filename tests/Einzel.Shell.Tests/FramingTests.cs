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
