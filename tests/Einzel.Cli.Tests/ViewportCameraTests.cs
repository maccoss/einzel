using Einzel.Commands;
using Einzel.Core.Results;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The window's camera: turning between named views, and keeping a watched packet in frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect these guard against lived in a control that could not be tested.</b> The
/// window unioned each new view's frame with the previous view's, which are boxes in
/// different view coordinates, so the frame grew on every click of a named view and never
/// shrank. Taken out into <see cref="ViewportCamera"/> it is checked here without a GL
/// context.
/// </para>
/// </remarks>
public sealed class ViewportCameraTests(ITestOutputHelper output)
{
    private static readonly (double Azimuth, double Elevation) Iso = ViewportPicture.Views["iso"];
    private static readonly (double Azimuth, double Elevation) Side = ViewportPicture.Views["side"];
    private static readonly (double Azimuth, double Elevation) Front = ViewportPicture.Views["front"];

    /// <summary>
    /// Turning to another view and back gives the frame the camera started with, however many
    /// times it is done, and each view is framed as tightly as if it had been opened there.
    /// </summary>
    /// <remarks>
    /// The side view of a cube two millimeters across is one millimeter half-height. Carrying
    /// the iso box's corners into it made that about two, and each further round trip larger
    /// again.
    /// </remarks>
    [Fact]
    public void TurningAwayAndBackGivesTheFrameYouStartedWith()
    {
        var scene = Scene(Box(-1.0, 1.0), density: null);
        var camera = new ViewportCamera(scene, Iso);
        var opening = camera.Framing;

        for (var trip = 0; trip < 5; trip++)
        {
            var side = camera.Turn(Side);

            output.WriteLine($"trip {trip}: side half height {side.HalfHeightMm:F6} mm, "
                + $"half width {side.HalfWidthMm:F6} mm");

            Assert.Equal(1.0, side.HalfHeightMm, 9);
            Assert.Equal(1.0, side.HalfWidthMm, 9);

            var iso = camera.Turn(Iso);

            Assert.Equal(opening.HalfWidthMm, iso.HalfWidthMm, 9);
            Assert.Equal(opening.HalfHeightMm, iso.HalfHeightMm, 9);
            Assert.Equal(opening.HalfDepthMm, iso.HalfDepthMm, 9);
        }
    }

    /// <summary>
    /// A packet that drifted out of the box the scene opened in stays in frame when the
    /// camera turns - the reason the window keeps anything from before a turn at all.
    /// </summary>
    [Fact]
    public void APacketThatDriftedStaysInFrameWhenTheCameraTurns()
    {
        var scene = Scene(Box(-1.0, 1.0), density: null);
        var camera = new ViewportCamera(scene, Iso);

        // A frame of the run with the packet thirty millimeters downstream of everything the
        // scene held.
        camera.Take(Scene(Box(-1.0, 1.0), Shell(30.0, 40.0)));

        foreach (var view in new[] { Side, Front, Iso, Side })
        {
            var matrix = camera.Turn(view).Matrix(1.6);

            foreach (var (x, y, z) in new[] { (30.0, -1.0, -1.0), (40.0, 1.0, 1.0), (-1.0, -1.0, -1.0) })
            {
                var cx = (matrix[0] * x) + (matrix[4] * y) + (matrix[8] * z) + matrix[12];
                var cy = (matrix[1] * x) + (matrix[5] * y) + (matrix[9] * z) + matrix[13];

                Assert.InRange(cx, -1.0, 1.0);
                Assert.InRange(cy, -1.0, 1.0);
            }
        }

        // And turning after a watch is as repeatable as turning before one.
        var first = camera.Turn(Side);
        camera.Turn(Iso);
        var again = camera.Turn(Side);

        Assert.Equal(first.HalfWidthMm, again.HalfWidthMm, 9);
        Assert.Equal(first.HalfHeightMm, again.HalfHeightMm, 9);
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

    private static ViewportOutcome Scene(ConductorSurface conductor, DensityShell? density) =>
        new(
            ModelPath: "probe.json",
            Trajectories: [],
            ProducesTrajectories: density is null,
            LowestEnergyEv: null,
            HighestEnergyEv: null,
            Conductors: [conductor],
            Equipotentials: [],
            LowestPotentialVolts: null,
            HighestPotentialVolts: null,
            Density: density is null ? [] : [density],
            PeakDensityPerCubicMetre: null,
            DensityAtUs: null,
            Ends: null,
            Warnings: Array.Empty<ValidityWarning>());
}
